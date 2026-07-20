using System.Text.Json;
using Comprexy.Application.Abstractions;
using Comprexy.Application.Configuration;
using Comprexy.Application.Exceptions;
using Comprexy.Application.Models;
using Comprexy.Application.Tracing;
using Comprexy.Domain.Entities;
using Comprexy.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Comprexy.Application.Services;

/// <summary>
/// Orchestrates a single proxied chat completion request end to end: resolves conversation
/// identity, persists new messages, builds the budget-aware outgoing context, forwards to the
/// upstream model, and queues follow-up compression work.
/// </summary>
public class ProxyChatCompletionService
{
    private readonly IConversationIdentityResolver _identityResolver;
    private readonly IConversationRequestGate _requestGate;
    private readonly IConversationRepository _conversationRepository;
    private readonly IConversationMessageRepository _messageRepository;
    private readonly IWorkingMemoryRepository _workingMemoryRepository;
    private readonly ITokenEstimator _tokenEstimator;
    private readonly ContextBuilder _contextBuilder;
    private readonly ContextBudgetEvaluator _budgetEvaluator;
    private readonly RecentContextSelector _recentContextSelector;
    private readonly ProviderEndpointResolver _endpointResolver;
    private readonly IChatCompletionClient _chatCompletionClient;
    private readonly ICompressionQueue _compressionQueue;
    private readonly ICompressionOrchestrator _compressionOrchestrator;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;
    private readonly ContextPolicyOptions _policy;
    private readonly ProxyOptions _proxyOptions;
    private readonly IPayloadTraceLogger _payloadTrace;
    private readonly ILogger<ProxyChatCompletionService> _logger;

    public ProxyChatCompletionService(
        IConversationIdentityResolver identityResolver,
        IConversationRequestGate requestGate,
        IConversationRepository conversationRepository,
        IConversationMessageRepository messageRepository,
        IWorkingMemoryRepository workingMemoryRepository,
        ITokenEstimator tokenEstimator,
        ContextBuilder contextBuilder,
        ContextBudgetEvaluator budgetEvaluator,
        RecentContextSelector recentContextSelector,
        ProviderEndpointResolver endpointResolver,
        IChatCompletionClient chatCompletionClient,
        ICompressionQueue compressionQueue,
        ICompressionOrchestrator compressionOrchestrator,
        IUnitOfWork unitOfWork,
        IClock clock,
        IOptions<ContextPolicyOptions> policy,
        IOptions<ProxyOptions> proxyOptions,
        IPayloadTraceLogger payloadTrace,
        ILogger<ProxyChatCompletionService> logger)
    {
        _identityResolver = identityResolver;
        _requestGate = requestGate;
        _conversationRepository = conversationRepository;
        _messageRepository = messageRepository;
        _workingMemoryRepository = workingMemoryRepository;
        _tokenEstimator = tokenEstimator;
        _contextBuilder = contextBuilder;
        _budgetEvaluator = budgetEvaluator;
        _recentContextSelector = recentContextSelector;
        _endpointResolver = endpointResolver;
        _chatCompletionClient = chatCompletionClient;
        _compressionQueue = compressionQueue;
        _compressionOrchestrator = compressionOrchestrator;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _policy = policy.Value;
        _proxyOptions = proxyOptions.Value;
        _payloadTrace = payloadTrace;
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

        var prepared = await PrepareAsync(request, conversationKey, cancellationToken);
        var upstreamResult = await _chatCompletionClient.CompleteAsync(
            prepared.Endpoint,
            prepared.UpstreamRequest,
            cancellationToken);

        return await CompleteAsync(prepared, upstreamResult, cancellationToken);
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

        var prepared = await PrepareAsync(request, conversationKey, cancellationToken);
        onConversationReady(prepared.Conversation.Id);
        var upstreamResult = await _chatCompletionClient.StreamAsync(
            prepared.Endpoint,
            prepared.UpstreamRequest,
            onRawSseData,
            cancellationToken);

        return await CompleteAsync(prepared, upstreamResult, cancellationToken);
    }

    private async Task<PreparedRequest> PrepareAsync(
        IncomingChatRequest request,
        string conversationKey,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var conversation = await _conversationRepository.FindByKeyAsync(conversationKey, cancellationToken);

        List<ConversationMessage> storedMessages;
        if (conversation is null)
        {
            conversation = Conversation.Create(conversationKey, now);
            _conversationRepository.Add(conversation);
            storedMessages = [];
        }
        else
        {
            storedMessages = await _messageRepository.GetByConversationIdAsync(conversation.Id, cancellationToken);
            EnrichStoredMessagesFromClientHistory(storedMessages, request.Messages);
        }

        // Client history shorter than our cursor (retry / rewind) — realign before diffing.
        if (conversation.SyncedMessageCount > request.Messages.Count)
        {
            _logger.LogWarning(
                "Conversation {ConversationId} sync cursor ({Synced}) was ahead of client history ({ClientCount}); realigning.",
                conversation.Id,
                conversation.SyncedMessageCount,
                request.Messages.Count);
            conversation.SetSyncedMessageCount(request.Messages.Count, now);
        }

        var newClientMessages = request.Messages.Skip(conversation.SyncedMessageCount).ToList();
        var systemMessage = newClientMessages.FirstOrDefault(m => m.Role == MessageRole.System)
            ?? request.Messages.FirstOrDefault(m => m.Role == MessageRole.System);
        conversation.CaptureSystemPromptIfAbsent(systemMessage?.Content);

        var nonSystemNewMessages = newClientMessages.Where(m => m.Role != MessageRole.System).ToList();

        var nextSequence = storedMessages.Count == 0
            ? 0
            : storedMessages.Max(m => m.Sequence) + 1;
        var newlyPersisted = new List<ConversationMessage>();
        foreach (var message in nonSystemNewMessages)
        {
            newlyPersisted.Add(PersistMessage(conversation.Id, nextSequence++, message, now));
        }

        // Absolute sync to this request's history length (avoids drift from partial advances).
        conversation.SetSyncedMessageCount(request.Messages.Count, now);

        var endpoint = _endpointResolver.ResolveUpstream();

        if (_proxyOptions.PassThrough)
        {
            var passThroughTokens = _tokenEstimator.CountPromptTokens(request.Messages, request.RawRequest);
            _logger.LogDebug(
                "Pass-through mode enabled for conversation {ConversationId}; forwarding original request fields without compression.",
                conversation.Id);
            LogContextBudget(
                conversation.Id,
                passThroughTokens,
                ContextBudgetDecision.ForwardImmediate,
                passThrough: true);

            return new PreparedRequest(
                conversation,
                nextSequence,
                passThroughTokens,
                ContextBudgetDecision.ForwardImmediate,
                endpoint,
                new UpstreamRequest(
                    request.Messages,
                    request.Stream,
                    request.RawRequest,
                    request.CallOptions,
                    ReplaceMessages: false),
                SkipCompression: true,
                request.Messages.Count,
                WindowStartSequence: null,
                WindowEndSequence: null,
                RecentRawCount: 0);
        }

        var allMessages = storedMessages.Concat(newlyPersisted).OrderBy(m => m.Sequence).ToList();
        var requestTip = request.Messages.LastOrDefault(m => m.Role != MessageRole.System)
            ?? throw new InvalidOperationException("Unable to resolve a current non-system message for this request.");

        // Ensure the outgoing tip is the client's latest non-system message (sync-repair).
        if (allMessages.Count == 0 || !IsSameTip(allMessages[^1], requestTip))
        {
            _logger.LogWarning(
                "Conversation {ConversationId} tip mismatch with client history; persisting request tip.",
                conversation.Id);
            var repaired = PersistMessage(conversation.Id, nextSequence++, requestTip, now);
            newlyPersisted.Add(repaired);
            allMessages.Add(repaired);
            conversation.SetSyncedMessageCount(request.Messages.Count, now);
        }

        if (allMessages.Count == 0)
        {
            throw new InvalidOperationException("Unable to resolve a current user message for this request.");
        }

        var currentMessageEntity = allMessages[^1];
        // Prefer the live request tip so wire JSON / tool payloads match what the client just sent.
        var currentUserMessage = requestTip;

        var workingMemory = await _workingMemoryRepository.GetLatestAsync(conversation.Id, cancellationToken);
        var ranPreCompressionEmergency = false;

        // Until the first successful compression, forward the client's full message list
        // (transparent messages). The retain/sliding window is applied only during compression.
        if (workingMemory is null)
        {
            var preCompressionTokens = _tokenEstimator.CountPromptTokens(request.Messages, request.RawRequest);
            var preCompressionDecision = _budgetEvaluator.Evaluate(preCompressionTokens);

            if (preCompressionDecision != ContextBudgetDecision.EmergencyCompressionRequired)
            {
                _logger.LogDebug(
                    "No working memory yet for conversation {ConversationId}; forwarding full client history until first compression.",
                    conversation.Id);
                LogContextBudget(
                    conversation.Id,
                    preCompressionTokens,
                    preCompressionDecision,
                    windowStartSequence: allMessages[0].Sequence,
                    windowEndSequence: currentMessageEntity.Sequence,
                    recentRawCount: allMessages.Count);

                return new PreparedRequest(
                    conversation,
                    nextSequence,
                    preCompressionTokens,
                    preCompressionDecision,
                    endpoint,
                    new UpstreamRequest(
                        request.Messages,
                        request.Stream,
                        request.RawRequest,
                        request.CallOptions,
                        ReplaceMessages: false),
                    SkipCompression: false,
                    request.Messages.Count,
                    allMessages[0].Sequence,
                    currentMessageEntity.Sequence,
                    allMessages.Count);
            }

            if (_policy.EmergencyCompression != EmergencyCompressionMode.Sync)
            {
                _logger.LogInformation(
                    "Hard limit exceeded before first compression for conversation {ConversationId}; emergency compression is {Mode}, rejecting.",
                    conversation.Id,
                    _policy.EmergencyCompression);
                throw new ContextBudgetExceededException(
                    conversation.Id,
                    preCompressionTokens,
                    _policy.HardLimitTokens);
            }

            _logger.LogInformation(
                "Hard limit exceeded before first compression for conversation {ConversationId}; running emergency compression.",
                conversation.Id);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _compressionOrchestrator.RunAsync(conversation.Id, CompressionMode.Emergency, cancellationToken);
            ranPreCompressionEmergency = true;
            workingMemory = await _workingMemoryRepository.GetLatestAsync(conversation.Id, cancellationToken);
            var refreshedAfterFirstCompression = await _messageRepository.GetByConversationIdAsync(
                conversation.Id,
                cancellationToken);
            if (refreshedAfterFirstCompression.Count > 0)
            {
                allMessages = refreshedAfterFirstCompression;
                currentMessageEntity = refreshedAfterFirstCompression.FirstOrDefault(m => m.Id == currentMessageEntity.Id)
                    ?? refreshedAfterFirstCompression.FirstOrDefault(m => m.Sequence == currentMessageEntity.Sequence)
                    ?? refreshedAfterFirstCompression[^1];
            }

            if (workingMemory is null)
            {
                throw new ContextBudgetExceededException(
                    conversation.Id,
                    preCompressionTokens,
                    _policy.HardLimitTokens);
            }
        }

        // After compression: send every still-unfolded message. Retain/window decisions happen
        // only inside CompressionOrchestrator when folding into working memory (except send-time
        // emergency trim below when still over the hard limit after compression).
        var recentRaw = allMessages
            .Where(m => !m.IsFolded && m.Sequence < currentMessageEntity.Sequence)
            .OrderBy(m => m.Sequence)
            .ToList();

        var outgoing = _contextBuilder.Build(conversation.SystemPrompt, workingMemory, recentRaw, currentUserMessage);
        var estimatedTokens = _tokenEstimator.CountPromptTokens(outgoing, request.RawRequest);
        var decision = ranPreCompressionEmergency
            ? ContextBudgetDecision.EmergencyCompressionRequired
            : _budgetEvaluator.Evaluate(estimatedTokens);
        var windowStart = recentRaw.Count > 0 ? recentRaw[0].Sequence : (int?)null;
        var windowEnd = currentMessageEntity.Sequence;
        LogContextBudget(
            conversation.Id,
            estimatedTokens,
            decision,
            postEmergency: ranPreCompressionEmergency,
            windowStartSequence: windowStart,
            windowEndSequence: windowEnd,
            recentRawCount: recentRaw.Count);

        if (!ranPreCompressionEmergency
            && decision == ContextBudgetDecision.EmergencyCompressionRequired
            && _policy.EmergencyCompression == EmergencyCompressionMode.Sync)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _compressionOrchestrator.RunAsync(conversation.Id, CompressionMode.Emergency, cancellationToken);

            workingMemory = await _workingMemoryRepository.GetLatestAsync(conversation.Id, cancellationToken);
            var refreshed = await _messageRepository.GetByConversationIdAsync(conversation.Id, cancellationToken);
            recentRaw = refreshed
                .Where(m => !m.IsFolded && m.Sequence < currentMessageEntity.Sequence)
                .OrderBy(m => m.Sequence)
                .ToList();

            outgoing = _contextBuilder.Build(conversation.SystemPrompt, workingMemory, recentRaw, currentUserMessage);
            estimatedTokens = _tokenEstimator.CountPromptTokens(outgoing, request.RawRequest);
            windowStart = recentRaw.Count > 0 ? recentRaw[0].Sequence : null;
            windowEnd = currentMessageEntity.Sequence;
            LogContextBudget(
                conversation.Id,
                estimatedTokens,
                decision,
                postEmergency: true,
                windowStartSequence: windowStart,
                windowEndSequence: windowEnd,
                recentRawCount: recentRaw.Count);
        }
        else if (!ranPreCompressionEmergency
                 && decision == ContextBudgetDecision.EmergencyCompressionRequired
                 && _policy.EmergencyCompression != EmergencyCompressionMode.Sync)
        {
            _logger.LogInformation(
                "Hard limit reached for conversation {ConversationId}; emergency compression is {Mode}, skipping sync compact.",
                conversation.Id,
                _policy.EmergencyCompression);
        }

        if (estimatedTokens >= _policy.HardLimitTokens)
        {
            (recentRaw, outgoing, estimatedTokens, windowStart) = ApplySendTimeEmergencyTrim(
                conversation,
                workingMemory,
                recentRaw,
                currentUserMessage,
                request.RawRequest);

            windowEnd = currentMessageEntity.Sequence;
            LogContextBudget(
                conversation.Id,
                estimatedTokens,
                decision,
                postEmergency: true,
                windowStartSequence: windowStart,
                windowEndSequence: windowEnd,
                recentRawCount: recentRaw.Count);

            if (estimatedTokens >= _policy.HardLimitTokens)
            {
                throw new ContextBudgetExceededException(
                    conversation.Id,
                    estimatedTokens,
                    _policy.HardLimitTokens);
            }
        }

        return new PreparedRequest(
            conversation,
            nextSequence,
            estimatedTokens,
            decision,
            endpoint,
            new UpstreamRequest(
                outgoing,
                request.Stream,
                request.RawRequest,
                request.CallOptions,
                ReplaceMessages: true),
            SkipCompression: false,
            request.Messages.Count,
            windowStart,
            windowEnd,
            recentRaw.Count);
    }

    /// <summary>
    /// Temporary wire-only retain trim (does not mark messages folded). Used when emergency
    /// compression left the full unfolded tip still over the hard limit.
    /// </summary>
    private (
        List<ConversationMessage> RecentRaw,
        IReadOnlyList<ChatMessage> Outgoing,
        int EstimatedTokens,
        int? WindowStart) ApplySendTimeEmergencyTrim(
        Conversation conversation,
        WorkingMemory? workingMemory,
        List<ConversationMessage> recentRaw,
        ChatMessage currentUserMessage,
        JsonElement? rawRequest)
    {
        var trimmed = _recentContextSelector
            .Select(recentRaw, emergency: true)
            .ToList();

        if (trimmed.Count < recentRaw.Count)
        {
            _logger.LogWarning(
                "Send-time emergency trim for conversation {ConversationId}: unfoldedPrior={Before} retained={After} (not folded).",
                conversation.Id,
                recentRaw.Count,
                trimmed.Count);
        }
        else
        {
            _logger.LogWarning(
                "Send-time emergency trim for conversation {ConversationId} could not drop unfolded messages (count={Count}).",
                conversation.Id,
                recentRaw.Count);
        }

        var outgoing = _contextBuilder.Build(
            conversation.SystemPrompt,
            workingMemory,
            trimmed,
            currentUserMessage);
        var estimatedTokens = _tokenEstimator.CountPromptTokens(outgoing, rawRequest);
        var windowStart = trimmed.Count > 0 ? trimmed[0].Sequence : (int?)null;
        return (trimmed, outgoing, estimatedTokens, windowStart);
    }

    private void EnrichStoredMessagesFromClientHistory(
        List<ConversationMessage> storedMessages,
        IReadOnlyList<ChatMessage> clientMessages)
    {
        var orderedStored = storedMessages.OrderBy(m => m.Sequence).ToList();
        var clientNonSystem = clientMessages.Where(m => m.Role != MessageRole.System).ToList();
        var count = Math.Min(orderedStored.Count, clientNonSystem.Count);

        for (var i = 0; i < count; i++)
        {
            var stored = orderedStored[i];
            var client = clientNonSystem[i];
            if (stored.Role != client.Role)
            {
                break;
            }

            if (stored.HasWireJson && !string.IsNullOrWhiteSpace(stored.Content))
            {
                continue;
            }

            var wire = client.RawWireMessage?.GetRawText();
            if (string.IsNullOrWhiteSpace(wire) && string.IsNullOrWhiteSpace(client.Content))
            {
                continue;
            }

            var content = string.IsNullOrWhiteSpace(client.Content)
                ? SummarizeAssistantContent(wire)
                : client.Content;
            var tokenCount = _tokenEstimator.CountTokens([client]);
            stored.EnrichFromClient(content, wire, tokenCount);
        }
    }

    private static string SummarizeAssistantContent(string? assistantMessageJson)
    {
        if (string.IsNullOrWhiteSpace(assistantMessageJson))
        {
            return string.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(assistantMessageJson);
            var root = document.RootElement;
            if (root.TryGetProperty("tool_calls", out var toolCalls) &&
                toolCalls.ValueKind == JsonValueKind.Array &&
                toolCalls.GetArrayLength() > 0)
            {
                var names = toolCalls.EnumerateArray()
                    .Select(call =>
                        call.TryGetProperty("function", out var function) &&
                        function.TryGetProperty("name", out var name) &&
                        name.ValueKind == JsonValueKind.String
                            ? name.GetString()
                            : null)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .ToList();

                return names.Count > 0
                    ? $"[tool_calls: {string.Join(", ", names)}]"
                    : "[tool_calls]";
            }
        }
        catch (JsonException)
        {
            // Fall through — leave content empty if wire is unreadable.
        }

        return string.Empty;
    }

    private static JsonElement? ParseOptionalWire(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private ConversationMessage PersistMessage(
        Guid conversationId,
        int sequence,
        ChatMessage message,
        DateTimeOffset now)
    {
        var tokenCount = _tokenEstimator.CountTokens([message]);
        var rawWireJson = message.RawWireMessage?.GetRawText();
        var entity = ConversationMessage.Create(
            conversationId,
            sequence,
            message.Role,
            message.Content,
            tokenCount,
            now,
            rawWireJson);
        _messageRepository.Add(entity);
        return entity;
    }

    private static bool IsSameTip(ConversationMessage persisted, ChatMessage incoming)
    {
        if (persisted.Role != incoming.Role)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(persisted.RawWireJson) && incoming.RawWireMessage is { } raw)
        {
            return string.Equals(persisted.RawWireJson, raw.GetRawText(), StringComparison.Ordinal);
        }

        return string.Equals(persisted.Content, incoming.Content, StringComparison.Ordinal);
    }

    private void LogContextBudget(
        Guid conversationId,
        int estimatedTokens,
        ContextBudgetDecision decision,
        bool passThrough = false,
        bool postEmergency = false,
        int? windowStartSequence = null,
        int? windowEndSequence = null,
        int? recentRawCount = null)
    {
        var label = passThrough
            ? PayloadTraceLabels.ContextBudgetPassThrough
            : postEmergency
                ? PayloadTraceLabels.ContextBudgetPostEmergency
                : PayloadTraceLabels.ContextBudgetReassembled;

        _payloadTrace.LogOutput(label, new
        {
            conversationId,
            estimatedTokens,
            softLimitTokens = _policy.SoftLimitTokens,
            hardLimitTokens = _policy.HardLimitTokens,
            decision = decision.ToString(),
            compressionSkipped = passThrough,
            windowStartSequence,
            windowEndSequence,
            recentRawCount
        });

        _logger.LogInformation(
            "Context budget ({Label}): estimatedTokens={EstimatedTokens} softLimit={SoftLimitTokens} hardLimit={HardLimitTokens} decision={Decision} window=[{WindowStart}..{WindowEnd}] recentRawCount={RecentRawCount}",
            label,
            estimatedTokens,
            _policy.SoftLimitTokens,
            _policy.HardLimitTokens,
            decision,
            windowStartSequence?.ToString() ?? "-",
            windowEndSequence?.ToString() ?? "-",
            recentRawCount);
    }

    private async Task<ProxyChatCompletionResult> CompleteAsync(
        PreparedRequest prepared,
        UpstreamChatResult upstreamResult,
        CancellationToken cancellationToken)
    {
        var assistantContent = string.IsNullOrWhiteSpace(upstreamResult.Content)
            ? SummarizeAssistantContent(upstreamResult.AssistantMessageJson)
            : upstreamResult.Content;
        var assistantMessage = new ChatMessage(
            MessageRole.Assistant,
            assistantContent,
            ParseOptionalWire(upstreamResult.AssistantMessageJson));
        var assistantTokenCount = upstreamResult.CompletionTokens
            ?? _tokenEstimator.CountTokens([assistantMessage]);
        var assistantEntity = ConversationMessage.Create(
            prepared.Conversation.Id,
            prepared.NextSequence,
            MessageRole.Assistant,
            assistantContent,
            assistantTokenCount,
            _clock.UtcNow,
            upstreamResult.AssistantMessageJson);
        _messageRepository.Add(assistantEntity);
        // Next client request should include this assistant as history[IncomingMessageCount].
        prepared.Conversation.SetSyncedMessageCount(prepared.IncomingMessageCount + 1, _clock.UtcNow);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (prepared.SkipCompression)
        {
            _logger.LogDebug(
                "Post-response compression skipped for conversation {ConversationId} (pass-through mode).",
                prepared.Conversation.Id);
        }
        else if (prepared.Decision == ContextBudgetDecision.ForwardImmediate)
        {
            _logger.LogDebug(
                "Post-response compression not enqueued for conversation {ConversationId}: estimatedTokens={EstimatedTokens} <= softLimit={SoftLimitTokens}.",
                prepared.Conversation.Id,
                prepared.EstimatedTokens,
                _policy.SoftLimitTokens);
        }
        else if (ToolCallChainState.HasOpenToolCalls([assistantEntity]))
        {
            _logger.LogInformation(
                "Post-response compression not enqueued for conversation {ConversationId}: assistant response has open tool calls.",
                prepared.Conversation.Id);
        }
        else
        {
            var jobMode = prepared.Decision == ContextBudgetDecision.ForwardWithHighPriorityCompression
                ? CompressionMode.HighPriorityBackground
                : CompressionMode.Background;
            _compressionQueue.Enqueue(new CompressionJob(prepared.Conversation.Id, jobMode));
            _logger.LogInformation(
                "Post-response compression enqueued for conversation {ConversationId}: mode={Mode} estimatedTokens={EstimatedTokens} softLimit={SoftLimitTokens}.",
                prepared.Conversation.Id,
                jobMode,
                prepared.EstimatedTokens,
                _policy.SoftLimitTokens);
        }

        var promptTokens = upstreamResult.PromptTokens ?? prepared.EstimatedTokens;
        return new ProxyChatCompletionResult(
            prepared.Conversation.Id,
            upstreamResult.Content,
            upstreamResult.FinishReason,
            promptTokens,
            assistantTokenCount,
            prepared.Endpoint.Model,
            prepared.EstimatedTokens,
            prepared.Decision,
            prepared.SkipCompression,
            upstreamResult.RawResponseJson);
    }

    private sealed record PreparedRequest(
        Conversation Conversation,
        int NextSequence,
        int EstimatedTokens,
        ContextBudgetDecision Decision,
        ProviderEndpoint Endpoint,
        UpstreamRequest UpstreamRequest,
        bool SkipCompression,
        int IncomingMessageCount,
        int? WindowStartSequence,
        int? WindowEndSequence,
        int RecentRawCount);
}
