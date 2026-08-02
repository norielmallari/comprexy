using System.Diagnostics;
using Comprexy.Application.Abstractions;
using Comprexy.Application.Models;
using Comprexy.Application.Services.ChatTurn;
using Comprexy.Domain.Enums;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Comprexy.Application.Services;

/// <summary>
/// Orchestrates a single proxied chat completion request end to end: resolves conversation
/// identity, persists new messages, builds soft-budget-aware outgoing context, forwards to the
/// upstream model, and runs Inline wrap-up when eligible.
/// </summary>
public class ProxyChatCompletionService
{
    private readonly IConversationIdentityResolver _identityResolver;
    private readonly IConversationRequestGate _requestGate;
    private readonly IChatCompletionClient _chatCompletionClient;
    private readonly ToolSchemaOrchestrator _toolSchemaOrchestrator;
    private readonly ChatTurnPreparer _preparer;
    private readonly ChatTurnCompleter _completer;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IHostApplicationLifetime _hostApplicationLifetime;
    private readonly ILogger<ProxyChatCompletionService> _logger;

    public ProxyChatCompletionService(
        IConversationIdentityResolver identityResolver,
        IConversationRequestGate requestGate,
        IChatCompletionClient chatCompletionClient,
        ToolSchemaOrchestrator toolSchemaOrchestrator,
        ChatTurnPreparer preparer,
        ChatTurnCompleter completer,
        IUnitOfWork unitOfWork,
        IHostApplicationLifetime hostApplicationLifetime,
        ILogger<ProxyChatCompletionService> logger)
    {
        _identityResolver = identityResolver;
        _requestGate = requestGate;
        _chatCompletionClient = chatCompletionClient;
        _toolSchemaOrchestrator = toolSchemaOrchestrator;
        _preparer = preparer;
        _completer = completer;
        _unitOfWork = unitOfWork;
        _hostApplicationLifetime = hostApplicationLifetime;
        _logger = logger;
    }

    public async Task<ProxyChatCompletionResult> HandleAsync(IncomingChatRequest request, CancellationToken cancellationToken)
    {
        if (request.Messages.Count == 0)
        {
            throw new ArgumentException("At least one message is required.", nameof(request));
        }

        var conversationKey = _identityResolver.Resolve(request.ConversationIdHeader, request.Messages);
        await using var _ = await _requestGate.AcquireAsync(
            conversationKey,
            ConversationGateLeaseKind.Exclusive,
            cancellationToken);

        var turnStartedTimestamp = Stopwatch.GetTimestamp();
        var prepared = await _preparer.PrepareAsync(
            request,
            conversationKey,
            _unitOfWork.SaveChangesAsync,
            cancellationToken);
        var prepareDuration = Stopwatch.GetElapsedTime(turnStartedTimestamp);

        var upstreamStartedTimestamp = Stopwatch.GetTimestamp();
        UpstreamChatResult upstreamResult;
        try
        {
            upstreamResult = await ExecuteUpstreamWithToolSchemaAsync(
                prepared,
                request.Stream,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }

        var timing = new TurnPhaseTiming(
            turnStartedTimestamp,
            prepareDuration,
            Stopwatch.GetElapsedTime(upstreamStartedTimestamp));

        using var postMainCts = CancellationTokenSource.CreateLinkedTokenSource(
            _hostApplicationLifetime.ApplicationStopping);
        return await _completer.CompleteAsync(
            prepared,
            upstreamResult,
            timing,
            _unitOfWork.SaveChangesAsync,
            postMainCts.Token);
    }

    public async Task<ProxyChatCompletionResult> HandleStreamingAsync(
        IncomingChatRequest request,
        Action<Guid> onConversationReady,
        Func<string, CancellationToken, Task> onRawSseData,
        CancellationToken cancellationToken)
    {
        if (request.Messages.Count == 0)
        {
            throw new ArgumentException("At least one message is required.", nameof(request));
        }

        var conversationKey = _identityResolver.Resolve(request.ConversationIdHeader, request.Messages);
        await using var _ = await _requestGate.AcquireAsync(
            conversationKey,
            ConversationGateLeaseKind.Exclusive,
            cancellationToken);

        var turnStartedTimestamp = Stopwatch.GetTimestamp();
        var prepared = await _preparer.PrepareAsync(
            request,
            conversationKey,
            _unitOfWork.SaveChangesAsync,
            cancellationToken);
        var prepareDuration = Stopwatch.GetElapsedTime(turnStartedTimestamp);
        onConversationReady(prepared.Conversation.Id);

        // Inline eligible turns must not let the client act on the answer before the wrap-up
        // checkpoint finishes: hold [DONE] always, and hold the whole tool_calls tail (from the
        // first tool_calls delta through the finish frame) so tools run against post-fold context.
        var holdForWrapUp = prepared.InlineFollowUpEligible;
        var heldFrames = new List<string>();
        var holdingToolTail = false;
        var pendingDone = false;
        Func<string, CancellationToken, Task> forward = async (data, ct) =>
        {
            if (!holdForWrapUp)
            {
                await onRawSseData(data, ct);
                return;
            }

            if (data == "[DONE]")
            {
                pendingDone = true;
                return;
            }

            if (holdingToolTail || ToolCallWireHelper.StreamChunkHasToolCalls(data))
            {
                holdingToolTail = true;
                heldFrames.Add(data);
                return;
            }

            await onRawSseData(data, ct);
        };

        var upstreamStartedTimestamp = Stopwatch.GetTimestamp();
        UpstreamChatResult main;
        try
        {
            main = prepared.ToolSchema is not null
                ? (await _toolSchemaOrchestrator.RunStreamingLoopAsync(
                    prepared.ToolSchema.Session,
                    prepared.Endpoint,
                    prepared.UpstreamRequest,
                    forward,
                    cancellationToken)).FinalUpstreamResult
                : await _chatCompletionClient.StreamAsync(
                    prepared.Endpoint,
                    prepared.UpstreamRequest,
                    forward,
                    cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }

        var timing = new TurnPhaseTiming(
            turnStartedTimestamp,
            prepareDuration,
            Stopwatch.GetElapsedTime(upstreamStartedTimestamp));

        using var postMainCts = CancellationTokenSource.CreateLinkedTokenSource(
            _hostApplicationLifetime.ApplicationStopping);
        var result = await _completer.CompleteAsync(
            prepared,
            main,
            timing,
            _unitOfWork.SaveChangesAsync,
            postMainCts.Token);

        if (heldFrames.Count > 0)
        {
            _logger.LogInformation(
                "Inline wrap-up complete for conversation {ConversationId}; releasing {HeldFrameCount} held tool_calls SSE frame(s) to the client.",
                prepared.Conversation.Id,
                heldFrames.Count);
        }

        // Released on accept and on soft failure alike — the task must continue either way.
        try
        {
            foreach (var held in heldFrames)
            {
                await onRawSseData(held, CancellationToken.None);
            }

            if (pendingDone)
            {
                await onRawSseData("[DONE]", CancellationToken.None);
            }
        }
        catch
        {
            // Client may already be gone after consuming the visible answer.
        }

        return result;
    }

    private async Task<UpstreamChatResult> ExecuteUpstreamWithToolSchemaAsync(
        PreparedRequest prepared,
        bool stream,
        CancellationToken cancellationToken)
    {
        var upstreamRequest = prepared.UpstreamRequest with { Stream = stream };
        var upstreamResult = await _chatCompletionClient.CompleteAsync(
            prepared.Endpoint,
            upstreamRequest,
            cancellationToken);

        if (prepared.ToolSchema is null)
        {
            return upstreamResult;
        }

        var loop = await _toolSchemaOrchestrator.RunInternalLoopAsync(
            prepared.ToolSchema.Session,
            prepared.Endpoint,
            upstreamRequest,
            upstreamResult,
            cancellationToken);

        return loop.FinalUpstreamResult;
    }
}
