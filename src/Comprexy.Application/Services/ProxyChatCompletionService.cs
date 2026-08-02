using System.Diagnostics;
using System.Text.Json;
using Comprexy.Application.Abstractions;
using Comprexy.Application.Configuration;
using Comprexy.Application.Mapping;
using Comprexy.Application.Models;
using Comprexy.Application.Services.CacheAlignment;
using Comprexy.Application.Tracing;
using Comprexy.Domain.Entities;
using Comprexy.Domain.Enums;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
    private readonly IConversationRepository _conversationRepository;
    private readonly IConversationMessageRepository _messageRepository;
    private readonly IWorkingMemoryRepository _workingMemoryRepository;
    private readonly ITokenEstimator _tokenEstimator;
    private readonly ContextBuilder _contextBuilder;
    private readonly ICacheAlignmentService _cacheAlignment;
    private readonly ContextBudgetEvaluator _budgetEvaluator;
    private readonly RecentContextSelector _recentContextSelector;
    private readonly ProviderEndpointResolver _endpointResolver;
    private readonly IChatCompletionClient _chatCompletionClient;
    private readonly ICompressionEventRepository _compressionEventRepository;
    private readonly CompressionPromptFactory _compressionPromptFactory;
    private readonly ToolSchemaOrchestrator _toolSchemaOrchestrator;
    private readonly IConversationMetricsRecorder _metricsRecorder;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;
    private readonly ContextPolicyOptions _policy;
    private readonly ProxyOptions _proxyOptions;
    private readonly CacheAlignmentOptions _cacheAlignmentOptions;
    private readonly IHostApplicationLifetime _hostApplicationLifetime;
    private readonly IPayloadTraceLogger _payloadTrace;
    private readonly IRequestTraceFileSession _requestTraceFiles;
    private readonly ILogger<ProxyChatCompletionService> _logger;

    public ProxyChatCompletionService(
        IConversationIdentityResolver identityResolver,
        IConversationRequestGate requestGate,
        IConversationRepository conversationRepository,
        IConversationMessageRepository messageRepository,
        IWorkingMemoryRepository workingMemoryRepository,
        ITokenEstimator tokenEstimator,
        ContextBuilder contextBuilder,
        ICacheAlignmentService cacheAlignment,
        ContextBudgetEvaluator budgetEvaluator,
        RecentContextSelector recentContextSelector,
        ProviderEndpointResolver endpointResolver,
        IChatCompletionClient chatCompletionClient,
        ICompressionEventRepository compressionEventRepository,
        CompressionPromptFactory compressionPromptFactory,
        ToolSchemaOrchestrator toolSchemaOrchestrator,
        IConversationMetricsRecorder metricsRecorder,
        IUnitOfWork unitOfWork,
        IClock clock,
        IOptions<ContextPolicyOptions> policy,
        IOptions<ProxyOptions> proxyOptions,
        IOptions<CacheAlignmentOptions> cacheAlignmentOptions,
        IHostApplicationLifetime hostApplicationLifetime,
        IPayloadTraceLogger payloadTrace,
        IRequestTraceFileSession requestTraceFiles,
        ILogger<ProxyChatCompletionService> logger)
    {
        _identityResolver = identityResolver;
        _requestGate = requestGate;
        _conversationRepository = conversationRepository;
        _messageRepository = messageRepository;
        _workingMemoryRepository = workingMemoryRepository;
        _tokenEstimator = tokenEstimator;
        _contextBuilder = contextBuilder;
        _cacheAlignment = cacheAlignment;
        _budgetEvaluator = budgetEvaluator;
        _recentContextSelector = recentContextSelector;
        _endpointResolver = endpointResolver;
        _chatCompletionClient = chatCompletionClient;
        _compressionEventRepository = compressionEventRepository;
        _compressionPromptFactory = compressionPromptFactory;
        _toolSchemaOrchestrator = toolSchemaOrchestrator;
        _metricsRecorder = metricsRecorder;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _policy = policy.Value;
        _proxyOptions = proxyOptions.Value;
        _cacheAlignmentOptions = cacheAlignmentOptions.Value;
        _hostApplicationLifetime = hostApplicationLifetime;
        _payloadTrace = payloadTrace;
        _requestTraceFiles = requestTraceFiles;
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
        var prepared = await PrepareAsync(request, conversationKey, cancellationToken);
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
        return await CompleteAsync(prepared, upstreamResult, timing, postMainCts.Token);
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
        var prepared = await PrepareAsync(request, conversationKey, cancellationToken);
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
        var result = await CompleteAsync(prepared, main, timing, postMainCts.Token);

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

        _requestTraceFiles.SetConversationId(conversation.Id);

        // Client history shorter than our cursor (retry / snapshot rewind) — realign before diffing.
        var rewoundToSnapshot = conversation.SyncedMessageCount > request.Messages.Count;
        if (rewoundToSnapshot)
        {
            _logger.LogWarning(
                "Conversation {ConversationId} sync cursor ({Synced}) was ahead of client history ({ClientCount}); realigning for snapshot rewind.",
                conversation.Id,
                conversation.SyncedMessageCount,
                request.Messages.Count);
            conversation.SetSyncedMessageCount(request.Messages.Count, now);

            var keepNonSystemCount = request.Messages.Count(m => m.Role != MessageRole.System);
            await ApplyClientSnapshotRewindAsync(
                conversation,
                storedMessages,
                keepNonSystemCount,
                cancellationToken);
        }

        var syncedPrefixCount = Math.Min(conversation.SyncedMessageCount, request.Messages.Count);
        var clientSyncedPrefix = request.Messages.Take(syncedPrefixCount).ToList();
        var newClientMessages = request.Messages.Skip(conversation.SyncedMessageCount).ToList();
        var systemMessage = newClientMessages.FirstOrDefault(m => m.Role == MessageRole.System)
            ?? request.Messages.FirstOrDefault(m => m.Role == MessageRole.System);
        conversation.CaptureSystemPromptIfAbsent(systemMessage?.Content);

        var nonSystemNewMessages = newClientMessages.Where(m => m.Role != MessageRole.System).ToList();

        var nextSequence = storedMessages.Count == 0
            ? 0
            : storedMessages.Max(m => m.Sequence) + 1;
        var newlyPersisted = new List<ConversationMessage>();
        var virtualToolsInboundApplied = false;
        if (_toolSchemaOrchestrator.ShouldAttemptActivation(_proxyOptions.PassThrough))
        {
            var historyForValidation = storedMessages
                .Concat(newlyPersisted)
                .OrderBy(m => m.Sequence)
                .ToList();

            // Ensure MappingJson before staging so replaced/excluded native tools are known on the
            // first Virtual turn (client may dump read/glob/excluded history before any IR emit).
            var (replacedClientToolNames, catalogMutatedForInbound, inboundCatalogHash, inboundDisableToolIr) =
                await _toolSchemaOrchestrator.ResolveReplacedClientToolNamesAsync(
                    conversation.Id,
                    request.RawRequest,
                    cancellationToken);
            if (catalogMutatedForInbound)
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                ApplyCacheAlignmentCatalogMutation(
                    conversation.Id,
                    inboundCatalogHash,
                    inboundDisableToolIr);
            }

            // Rewrite Virtual Tools inbound results before persist so DB/model see IR observations.
            // Complete dual-id rows only after PersistMessage so a crash mid-batch can still retry.
            // clientSyncedPrefix heals snapshot rewind: announcing assistants often sit before the tip.
            // Replaced/excluded native tool assistants/results are dropped (never staged into IR transcript).
            var inboundRewrite = await _toolSchemaOrchestrator.ValidateAndRewriteInboundToolResultsAsync(
                conversation.Id,
                nonSystemNewMessages,
                historyForValidation,
                clientSyncedPrefix,
                cancellationToken,
                replacedClientToolNames);
            virtualToolsInboundApplied = true;

            foreach (var message in inboundRewrite.Messages)
            {
                newlyPersisted.Add(PersistMessage(conversation.Id, nextSequence++, message, now));
            }

            // Inbound distill commit: persist rewritten tool observations before isolated dual-id Complete
            // (docs/ARCHITECTURE.md § Persistence — Unit of Work ownership).
            if (inboundRewrite.CompletedClientCallIds.Count > 0)
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            foreach (var clientCallId in inboundRewrite.CompletedClientCallIds)
            {
                await _toolSchemaOrchestrator.CompleteInboundToolCallAsync(
                    conversation.Id,
                    clientCallId,
                    cancellationToken);
            }
        }
        else
        {
            foreach (var message in nonSystemNewMessages)
            {
                newlyPersisted.Add(PersistMessage(conversation.Id, nextSequence++, message, now));
            }
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
                RecentRawCount: 0,
                MetricsPrepare: null);
        }

        TurnMetricsPrepareData? metricsPrepare = null;
        if (_metricsRecorder.IsEnabled)
        {
            metricsPrepare = new TurnMetricsPrepareData(
                RequestStartedAt: now,
                RawInputTokensEstimated: _tokenEstimator.CountPromptTokens(request.Messages, request.RawRequest),
                RequestHash: MetricsPayloadHasher.HashJsonElement(request.RawRequest),
                RawMessageCount: request.Messages.Count,
                WorkingMemoryVersionUsed: null,
                TrimTriggered: false);
        }

        var allMessages = storedMessages.Concat(newlyPersisted).OrderBy(m => m.Sequence).ToList();
        var requestTip = request.Messages.LastOrDefault(m => m.Role != MessageRole.System)
            ?? throw new InvalidOperationException("Unable to resolve a current non-system message for this request.");

        // Ensure the outgoing tip is the client's latest non-system message (sync-repair).
        // Virtual Tools inbound rewrite intentionally changes tip wire (IR call_* / distilled body vs
        // client cur_* / native body). Re-persisting the client tip would leak replaced tool results
        // into the IR transcript — typically the last result in a parallel batch.
        var keepIrTipAfterVirtualInbound = false;
        if (allMessages.Count == 0 || !IsSameTip(allMessages[^1], requestTip))
        {
            if (virtualToolsInboundApplied &&
                IsVirtualToolsExpectedTipMismatch(requestTip, nonSystemNewMessages))
            {
                keepIrTipAfterVirtualInbound = true;
                _logger.LogDebug(
                    "Conversation {ConversationId} tip wire differs after Virtual Tools inbound rewrite; keeping IR tip.",
                    conversation.Id);
            }
            else
            {
                _logger.LogWarning(
                    "Conversation {ConversationId} tip mismatch with client history; persisting request tip.",
                    conversation.Id);
                var repaired = PersistMessage(conversation.Id, nextSequence++, requestTip, now);
                newlyPersisted.Add(repaired);
                allMessages.Add(repaired);
                conversation.SetSyncedMessageCount(request.Messages.Count, now);
            }
        }

        if (allMessages.Count == 0)
        {
            throw new InvalidOperationException("Unable to resolve a current user message for this request.");
        }

        var currentMessageEntity = allMessages[^1];
        // Prefer the live request tip so wire JSON / tool payloads match what the client just sent,
        // except after Virtual Tools distill/swallow where the IR tip is authoritative.
        var currentUserMessage = keepIrTipAfterVirtualInbound
            ? ConversationMessageMapper.ToChatMessage(currentMessageEntity)
            : requestTip;

        var workingMemory = await _workingMemoryRepository.GetLatestAsync(conversation.Id, cancellationToken);
        if (metricsPrepare is not null)
        {
            metricsPrepare = metricsPrepare with { WorkingMemoryVersionUsed = workingMemory?.Version };
        }

        // Always rebuild from stored (IR-side) messages. WM is optional — pre-first-compression
        // is the same path with workingMemory == null (never forward client wire history).
        // Folding happens only inside Inline wrap-up on complete.
        var useCacheAlignment = _cacheAlignmentOptions.Enabled;
        var recentRaw = PrepareRecentRawForChatTemplate(
            conversation.Id,
            allMessages
                .Where(m => !m.IsFolded && m.Sequence < currentMessageEntity.Sequence)
                .OrderBy(m => m.Sequence)
                .ToList(),
            currentUserMessage,
            allMessages,
            currentMessageEntity.Sequence,
            applyLiveDedupe: !useCacheAlignment);

        IReadOnlyList<ChatMessage> outgoing;
        if (useCacheAlignment)
        {
            outgoing = MaterializeOutgoingViaCacheAlignment(
                conversation,
                workingMemory,
                recentRaw,
                currentUserMessage,
                currentMessageEntity,
                allMessages);
        }
        else
        {
            outgoing = _contextBuilder.Build(
                conversation.SystemPrompt,
                workingMemory,
                recentRaw,
                currentUserMessage);
        }

        outgoing = EnsureOutgoingEndsAtTip(
            conversation.Id,
            outgoing,
            currentUserMessage,
            currentMessageEntity.Sequence);

        var toolSchema = await TryPrepareToolSchemaAsync(
            conversation.Id,
            outgoing,
            request.RawRequest,
            cancellationToken);
        var estimateMessages = toolSchema?.OutgoingMessages ?? outgoing;
        var estimatePayload = toolSchema?.RewrittenClientRequest ?? request.RawRequest;
        var estimatedTokens = _tokenEstimator.CountPromptTokens(estimateMessages, estimatePayload);
        var decision = _budgetEvaluator.Evaluate(estimatedTokens);
        var windowStart = recentRaw.Count > 0 ? recentRaw[0].Sequence : (int?)null;
        var windowEnd = currentMessageEntity.Sequence;
        LogContextBudget(
            conversation.Id,
            estimatedTokens,
            decision,
            windowStartSequence: windowStart,
            windowEndSequence: windowEnd,
            recentRawCount: recentRaw.Count);

        return await BuildPreparedRequestAsync(
            conversation,
            nextSequence,
            estimatedTokens,
            decision,
            endpoint,
            outgoing,
            request.RawRequest,
            request,
            allMessages,
            skipCompression: false,
            request.Messages.Count,
            windowStart,
            windowEnd,
            recentRaw.Count,
            replaceMessages: true,
            cancellationToken,
            toolSchema,
            metricsPrepare);
    }

    private async Task<ToolSchemaPrepareResult?> TryPrepareToolSchemaAsync(
        Guid conversationId,
        IReadOnlyList<ChatMessage> outgoingMessages,
        JsonElement? rawRequest,
        CancellationToken cancellationToken)
    {
        if (!_toolSchemaOrchestrator.ShouldAttemptActivation(_proxyOptions.PassThrough))
        {
            return null;
        }

        var outcome = await _toolSchemaOrchestrator.TryPrepareRewriteAsync(
            conversationId,
            outgoingMessages,
            rawRequest,
            cancellationToken);

        // Flush MappingJson success and DisableToolIr alike. Staged messages may also flush here
        // when CatalogMutated (same early-flush behavior as the prior Virtual success path).
        if (outcome.CatalogMutated)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            if (outcome.Result is not null)
            {
                ApplyCacheAlignmentCatalogMutation(
                    conversationId,
                    outcome.Result.Session.Mapping.SchemaHash,
                    disableToolIr: false);
            }
            else
            {
                ApplyCacheAlignmentCatalogMutation(
                    conversationId,
                    catalogHash: null,
                    disableToolIr: true);
            }
        }

        return outcome.Result;
    }

    private void ApplyCacheAlignmentCatalogMutation(
        Guid conversationId,
        string? catalogHash,
        bool disableToolIr)
    {
        if (!_cacheAlignmentOptions.Enabled)
        {
            return;
        }

        if (disableToolIr)
        {
            _cacheAlignment.Invalidate(conversationId);
            return;
        }

        if (!string.IsNullOrWhiteSpace(catalogHash))
        {
            _cacheAlignment.SetCatalogHash(conversationId, catalogHash);
        }
    }

    private IReadOnlyList<ChatMessage> MaterializeOutgoingViaCacheAlignment(
        Conversation conversation,
        WorkingMemory? workingMemory,
        List<ConversationMessage> recentRaw,
        ChatMessage currentUserMessage,
        ConversationMessage currentMessageEntity,
        IReadOnlyList<ConversationMessage> allMessages)
    {
        var messagesById = allMessages.ToDictionary(m => m.Id);
        var snapshot = _cacheAlignment.GetSnapshot(conversation.Id);
        var wmVersion = workingMemory?.Version ?? 0;

        if (snapshot is null
            || snapshot.WorkingMemoryVersion != wmVersion
            || snapshot.RetainFrontierWatermark > currentMessageEntity.Sequence)
        {
            // Cold ensure (or WM/watermark mismatch): rebuild wrap-up-ready Prefix from frontier.
            if (snapshot is not null)
            {
                _cacheAlignment.Invalidate(conversation.Id);
            }

            // Bake the failed-edit wire omit into the frozen Prefix instead of re-applying it every
            // turn during materialize: recentRaw excludes the tip, so the omit cannot drop the newest
            // message, and warm turns reuse stable Prefix bytes instead of rebuilding them.
            var frontierSource = ApplyLiveDuplicateFailedEditDedupe(
                conversation.Id,
                recentRaw,
                allMessages,
                currentMessageEntity.Sequence);

            if (!WrapUpReadiness.TryEnsureWrapUpReady(
                    frontierSource,
                    out var prefixFrontier,
                    out var excluded))
            {
                _logger.LogWarning(
                    "Cache Alignment EnsureWrapUpReady failed for conversation {ConversationId}; falling back to ContextBuilder.Build.",
                    conversation.Id);
                return _contextBuilder.Build(
                    conversation.SystemPrompt,
                    workingMemory,
                    frontierSource,
                    currentUserMessage);
            }

            var prefix = _contextBuilder.BuildLivePrefix(
                conversation.SystemPrompt,
                workingMemory,
                prefixFrontier);
            var prefixIds = prefixFrontier.Select(m => m.Id).ToList();
            var watermark = prefixFrontier.Count == 0
                ? 0
                : prefixFrontier.Max(m => m.Sequence);

            if (!_cacheAlignment.TryStorePrefix(
                    conversation.Id,
                    prefix,
                    prefixIds,
                    wmVersion,
                    watermark,
                    catalogHash: null))
            {
                _logger.LogWarning(
                    "Cache Alignment TryStorePrefix rejected for conversation {ConversationId}; falling back to ContextBuilder.Build.",
                    conversation.Id);
                return _contextBuilder.Build(
                    conversation.SystemPrompt,
                    workingMemory,
                    frontierSource,
                    currentUserMessage);
            }

            var suffixIds = excluded
                .Concat(new[] { currentMessageEntity })
                .Select(m => m.Id)
                .Distinct()
                .ToList();
            // Also include any unfolded messages after watermark not in Prefix (open tips).
            // Omitted duplicates above the watermark come back here by design: completeness wins
            // over savings outside the frozen Prefix.
            foreach (var message in allMessages.Where(m =>
                         !m.IsFolded &&
                         m.Sequence > watermark &&
                         m.Id != currentMessageEntity.Id))
            {
                if (!prefixIds.Contains(message.Id) && !suffixIds.Contains(message.Id))
                {
                    suffixIds.Add(message.Id);
                }
            }

            _cacheAlignment.ReplaceSuffix(conversation.Id, suffixIds);
        }
        else
        {
            // Warm: Suffix = unfolded after watermark (including tip); Prefix frozen.
            var suffixIds = allMessages
                .Where(m => !m.IsFolded && m.Sequence > snapshot.RetainFrontierWatermark)
                .OrderBy(m => m.Sequence)
                .Select(m => m.Id)
                .ToList();
            if (suffixIds.Count == 0 || suffixIds[^1] != currentMessageEntity.Id)
            {
                // Tip must be present even if sequence heuristic missed it.
                if (!suffixIds.Contains(currentMessageEntity.Id))
                {
                    suffixIds.Add(currentMessageEntity.Id);
                }
            }

            _cacheAlignment.ReplaceSuffix(conversation.Id, suffixIds);
        }

        // No materialize-time omit: Prefix ⊕ Suffix goes out verbatim so the tip is always present
        // and frozen Prefix bytes are never rewritten mid-conversation.
        return _cacheAlignment.MaterializeLive(conversation.Id, messagesById);
    }

    private async Task<PreparedRequest> BuildPreparedRequestAsync(
        Conversation conversation,
        int nextSequence,
        int estimatedTokens,
        ContextBudgetDecision decision,
        ProviderEndpoint endpoint,
        IReadOnlyList<ChatMessage> outgoingMessages,
        JsonElement? rawRequest,
        IncomingChatRequest request,
        IReadOnlyList<ConversationMessage> allMessages,
        bool skipCompression,
        int incomingMessageCount,
        int? windowStartSequence,
        int? windowEndSequence,
        int recentRawCount,
        bool replaceMessages,
        CancellationToken cancellationToken,
        ToolSchemaPrepareResult? precomputedToolSchema = null,
        TurnMetricsPrepareData? metricsPrepare = null)
    {
        var toolSchema = precomputedToolSchema
            ?? await TryPrepareToolSchemaAsync(
                conversation.Id,
                outgoingMessages,
                rawRequest,
                cancellationToken);

        var messages = outgoingMessages;
        var tokens = estimatedTokens;

        if (toolSchema is not null)
        {
            messages = toolSchema.OutgoingMessages;
            tokens = _tokenEstimator.CountPromptTokens(messages, toolSchema.RewrittenClientRequest);
        }

        // ToolSchema rewrites tools[] — always replace wire messages when active.
        var effectiveReplaceMessages = toolSchema is not null || replaceMessages;
        var preFollowUpEstimatedTokens = tokens;
        var inlineFollowUpEligible = false;
        var inlineOpenStoreEmergency = false;

        if (!skipCompression
            && decision != ContextBudgetDecision.ForwardImmediate
            && await IsInlineCooldownClearAsync(conversation.Id, allMessages, cancellationToken))
        {
            var unfolded = allMessages.Where(m => !m.IsFolded).OrderBy(m => m.Sequence).ToList();
            var chainAssessment = ToolCallChainState.Assess(unfolded);
            if (!chainAssessment.IsOpen)
            {
                var wrapUpUser = _compressionPromptFactory.BuildInlineWrapUpUserMessage();
                var wrapUpTipTokens = _tokenEstimator.CountTokens([wrapUpUser]);
                inlineFollowUpEligible = true;
                _logger.LogInformation(
                    "Inline follow-up wrap-up eligible for conversation {ConversationId}: estimatedTokens={EstimatedTokens} wrapUpTipTokens={WrapUpTipTokens}",
                    conversation.Id,
                    preFollowUpEstimatedTokens,
                    wrapUpTipTokens);
            }
            else
            {
                if (!WrapUpReadiness.TryEnsureWrapUpReady(
                        unfolded,
                        out var closedPrefix,
                        out var excludedOpen))
                {
                    _logger.LogWarning(
                        "Inline follow-up wrap-up skipped for conversation {ConversationId}: open_unrepairable; awaitingClientToolResults={IsAwaitingClientToolResults}.",
                        conversation.Id,
                        chainAssessment.IsAwaitingClientToolResults);
                }
                else if (closedPrefix.Count == 0)
                {
                    _logger.LogInformation(
                        "Inline follow-up wrap-up skipped for conversation {ConversationId}: no_closed_prefix; awaitingClientToolResults={IsAwaitingClientToolResults} excludedCount={ExcludedCount}.",
                        conversation.Id,
                        chainAssessment.IsAwaitingClientToolResults,
                        excludedOpen.Count);
                }
                else
                {
                    var wrapUpUser = _compressionPromptFactory.BuildInlineWrapUpUserMessage();
                    var wrapUpTipTokens = _tokenEstimator.CountTokens([wrapUpUser]);
                    inlineFollowUpEligible = true;
                    inlineOpenStoreEmergency = true;
                    _logger.LogInformation(
                        "Inline follow-up wrap-up eligible (open-store mid-chain emergency) for conversation {ConversationId}: estimatedTokens={EstimatedTokens} wrapUpTipTokens={WrapUpTipTokens} awaitingClientToolResults={IsAwaitingClientToolResults} excludedCount={ExcludedCount} closedPrefixCount={ClosedPrefixCount}.",
                        conversation.Id,
                        preFollowUpEstimatedTokens,
                        wrapUpTipTokens,
                        chainAssessment.IsAwaitingClientToolResults,
                        excludedOpen.Count,
                        closedPrefix.Count);
                }
            }
        }

        return new PreparedRequest(
            conversation,
            nextSequence,
            tokens,
            decision,
            endpoint,
            new UpstreamRequest(
                messages,
                request.Stream,
                request.RawRequest,
                request.CallOptions,
                ReplaceMessages: effectiveReplaceMessages,
                RewrittenClientRequest: toolSchema?.RewrittenClientRequest),
            skipCompression,
            incomingMessageCount,
            windowStartSequence,
            windowEndSequence,
            recentRawCount,
            toolSchema,
            metricsPrepare,
            inlineFollowUpEligible,
            inlineOpenStoreEmergency,
            preFollowUpEstimatedTokens);
    }

    private async Task<bool> IsInlineCooldownClearAsync(
        Guid conversationId,
        IReadOnlyList<ConversationMessage> allMessages,
        CancellationToken cancellationToken)
    {
        var latestSucceeded = await _compressionEventRepository.GetLatestSucceededAsync(
            conversationId,
            CompressionMode.Inline,
            cancellationToken);
        if (latestSucceeded?.CompletedAt is null)
        {
            return true;
        }

        var turnsSince = allMessages.Count(m =>
            m.Role == MessageRole.Assistant
            && m.CreatedAt > latestSucceeded.CompletedAt);

        if (turnsSince >= _policy.MinTurnsBetweenGenerations)
        {
            return true;
        }

        _logger.LogDebug(
            "Inline follow-up wrap-up skipped for conversation {ConversationId}: cooldown ({TurnsSince}/{MinTurns} assistant turns since last success).",
            conversationId,
            turnsSince,
            _policy.MinTurnsBetweenGenerations);
        return false;
    }

    /// <summary>
    /// Repairs unfolded context so tool turns always follow an assistant/tool predecessor:
    /// restore a folded parent assistant when the live tip is a tool result, then drop any
    /// remaining orphan tools. Optionally omits older identical failed-edit tool turns from the
    /// wire (does not mark folded); Cache Alignment omits them at Prefix build instead. Logs when
    /// recovery or live dedupe runs so bad retain folds stay visible.
    /// </summary>
    private List<ConversationMessage> PrepareRecentRawForChatTemplate(
        Guid conversationId,
        List<ConversationMessage> recentRaw,
        ChatMessage tip,
        IReadOnlyList<ConversationMessage> allMessages,
        int tipSequence,
        bool applyLiveDedupe = true)
    {
        var (withParent, restored) = ChatTemplateMessageOrder.EnsureToolTipHasParent(
            recentRaw,
            tip,
            allMessages,
            tipSequence);
        if (restored > 0)
        {
            _logger.LogWarning(
                "Restored {RestoredCount} folded parent message(s) for tool tip in conversation {ConversationId} (chat template order).",
                restored,
                conversationId);
        }

        var (sanitized, dropped) = ChatTemplateMessageOrder.RemoveOrphanToolMessages(withParent);
        if (dropped > 0)
        {
            _logger.LogWarning(
                "Dropped {DroppedCount} orphan tool message(s) from outgoing context for conversation {ConversationId} (tool must follow assistant or tool).",
                dropped,
                conversationId);
        }

        var list = sanitized as List<ConversationMessage> ?? sanitized.ToList();
        if (!applyLiveDedupe)
        {
            return list;
        }

        return ApplyLiveDuplicateFailedEditDedupe(conversationId, list, allMessages, tipSequence);
    }

    /// <summary>
    /// Wire-only: drop older identical failed file-edit tool results (path + old_string
    /// last-wins) from the outgoing retain window so StrReplace failure loops do not stack.
    /// Does not <c>MarkFoldedInto</c>. The tip entity joins the corpus so a re-failing tip can
    /// displace older copies, then rows from the tip onward are stripped — callers own the tip
    /// (<see cref="ContextBuilder.Build"/> appends it; Cache Alignment carries it in the Suffix).
    /// </summary>
    private List<ConversationMessage> ApplyLiveDuplicateFailedEditDedupe(
        Guid conversationId,
        List<ConversationMessage> recentRaw,
        IReadOnlyList<ConversationMessage> allMessages,
        int tipSequence)
    {
        if (!_policy.DedupeDuplicateFailedEdits || recentRaw.Count == 0)
        {
            return recentRaw;
        }

        var tipEntity = allMessages.FirstOrDefault(m => m.Sequence == tipSequence);
        IReadOnlyList<ConversationMessage> corpus = recentRaw;
        if (tipEntity is not null && recentRaw.TrueForAll(m => m.Sequence != tipSequence))
        {
            corpus = recentRaw.Append(tipEntity).OrderBy(m => m.Sequence).ToList();
        }

        var dedupe = DuplicateFailedEditDeduper.Apply(corpus, tipSequence);
        if (!dedupe.DroppedAny)
        {
            return recentRaw;
        }

        _logger.LogInformation(
            "duplicate_failed_edit_dedupe conversationId={ConversationId} phase=live_chat droppedCount={DroppedCount} keptKeys={KeptKeys} droppedSequences={DroppedSequences}",
            conversationId,
            dedupe.DroppedSequences.Count,
            string.Join(',', dedupe.KeptKeys),
            string.Join(',', dedupe.DroppedSequences));

        var keptPrior = dedupe.Retain
            .Where(m => m.Sequence < tipSequence)
            .OrderBy(m => m.Sequence)
            .ToList();

        var (sanitized, orphanDropped) = ChatTemplateMessageOrder.RemoveOrphanToolMessages(keptPrior);
        if (orphanDropped > 0)
        {
            _logger.LogWarning(
                "Dropped {DroppedCount} orphan tool message(s) after live duplicate-failed-edit dedupe for conversation {ConversationId}.",
                orphanDropped,
                conversationId);
        }

        return sanitized as List<ConversationMessage> ?? sanitized.ToList();
    }

    private List<ConversationMessage> SanitizeRecentRawForChatTemplate(
        Guid conversationId,
        List<ConversationMessage> recentRaw)
    {
        var (sanitized, dropped) = ChatTemplateMessageOrder.RemoveOrphanToolMessages(recentRaw);
        if (dropped > 0)
        {
            _logger.LogWarning(
                "Dropped {DroppedCount} orphan tool message(s) from outgoing context for conversation {ConversationId} (tool must follow assistant or tool).",
                dropped,
                conversationId);
        }

        return sanitized as List<ConversationMessage> ?? sanitized.ToList();
    }

    /// <summary>
    /// Discards persisted turns past the client snapshot and invalidates working-memory versions
    /// that absorbed any of those turns. Mutates <paramref name="storedMessages"/> in place.
    /// </summary>
    private async Task ApplyClientSnapshotRewindAsync(
        Conversation conversation,
        List<ConversationMessage> storedMessages,
        int keepNonSystemCount,
        CancellationToken cancellationToken)
    {
        if (keepNonSystemCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(keepNonSystemCount));
        }

        // Abandoned open IR→client rounds from the discarded branch must not block healing.
        if (_toolSchemaOrchestrator.ShouldAttemptActivation(_proxyOptions.PassThrough))
        {
            await _toolSchemaOrchestrator.ClearPendingToolCallMapsAsync(conversation.Id, cancellationToken);
        }

        var toDelete = storedMessages
            .Where(m => m.Sequence >= keepNonSystemCount)
            .OrderBy(m => m.Sequence)
            .ToList();

        int? invalidateWmFrom = null;
        foreach (var message in toDelete)
        {
            if (message.FoldedIntoWorkingMemoryVersion is int foldedVersion)
            {
                invalidateWmFrom = invalidateWmFrom is null
                    ? foldedVersion
                    : Math.Min(invalidateWmFrom.Value, foldedVersion);
            }
        }

        if (invalidateWmFrom is int fromVersion)
        {
            foreach (var kept in storedMessages.Where(m =>
                         m.Sequence < keepNonSystemCount &&
                         m.FoldedIntoWorkingMemoryVersion is int v &&
                         v >= fromVersion))
            {
                kept.ClearFold();
            }

            var removedWm = await _workingMemoryRepository.DeleteFromVersionAsync(
                conversation.Id,
                fromVersion,
                cancellationToken);
            _logger.LogInformation(
                "Snapshot rewind for conversation {ConversationId}: invalidated working memory from version {FromVersion} (deleted {DeletedCount} version row(s)).",
                conversation.Id,
                fromVersion,
                removedWm);
        }

        foreach (var message in toDelete)
        {
            _messageRepository.Remove(message);
            storedMessages.Remove(message);
        }

        if (toDelete.Count > 0)
        {
            _logger.LogInformation(
                "Snapshot rewind for conversation {ConversationId}: deleted {DeletedCount} stored message(s) from sequence {FromSequence} (keeping {KeepCount} non-system turn(s)).",
                conversation.Id,
                toDelete.Count,
                keepNonSystemCount,
                keepNonSystemCount);
        }

        if (_cacheAlignmentOptions.Enabled)
        {
            _cacheAlignment.Invalidate(conversation.Id);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
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

    /// <summary>
    /// Every wire projection (retain omit, Prefix ⊕ Suffix materialize) must still end at the tip.
    /// A dropped tip hides the client's newest turn from the model — typically a mid-chain
    /// interrupt — so surface it and re-append instead of forwarding a truncated turn.
    /// </summary>
    private IReadOnlyList<ChatMessage> EnsureOutgoingEndsAtTip(
        Guid conversationId,
        IReadOnlyList<ChatMessage> outgoing,
        ChatMessage tip,
        int tipSequence)
    {
        var lastNonSystem = outgoing.LastOrDefault(m => m.Role != MessageRole.System);
        if (lastNonSystem is not null && IsSameChatMessage(lastNonSystem, tip))
        {
            return outgoing;
        }

        _logger.LogWarning(
            "Outgoing context for conversation {ConversationId} did not end at tip sequence {TipSequence}; re-appending the tip.",
            conversationId,
            tipSequence);

        var repaired = new List<ChatMessage>(outgoing.Count + 1);
        repaired.AddRange(outgoing);
        repaired.Add(tip);
        return repaired;
    }

    private static bool IsSameChatMessage(ChatMessage left, ChatMessage right)
    {
        if (left.Role != right.Role)
        {
            return false;
        }

        if (left.RawWireMessage is { } leftRaw && right.RawWireMessage is { } rightRaw)
        {
            return string.Equals(leftRaw.GetRawText(), rightRaw.GetRawText(), StringComparison.Ordinal);
        }

        return string.Equals(left.Content, right.Content, StringComparison.Ordinal);
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

    /// <summary>
    /// True when <paramref name="requestTip"/> was an input to Virtual Tools inbound rewrite.
    /// Distill/swallow already accounted for it; tip sync must not re-stage native client wire.
    /// </summary>
    private static bool IsVirtualToolsExpectedTipMismatch(
        ChatMessage requestTip,
        IReadOnlyList<ChatMessage> nonSystemNewMessages)
    {
        if (requestTip.Role is not (MessageRole.Tool or MessageRole.Assistant))
        {
            return false;
        }

        for (var i = 0; i < nonSystemNewMessages.Count; i++)
        {
            if (ReferenceEquals(nonSystemNewMessages[i], requestTip))
            {
                return true;
            }
        }

        return false;
    }

    private void LogContextBudget(
        Guid conversationId,
        int estimatedTokens,
        ContextBudgetDecision decision,
        bool passThrough = false,
        int? windowStartSequence = null,
        int? windowEndSequence = null,
        int? recentRawCount = null)
    {
        var label = passThrough
            ? PayloadTraceLabels.ContextBudgetPassThrough
            : PayloadTraceLabels.ContextBudgetReassembled;

        _payloadTrace.LogOutput(label, new
        {
            conversationId,
            estimatedTokens,
            softLimitTokens = _policy.SoftLimitTokens,
            decision = decision.ToString(),
            compressionSkipped = passThrough,
            windowStartSequence,
            windowEndSequence,
            recentRawCount
        });

        _logger.LogInformation(
            "Context budget ({Label}): estimatedTokens={EstimatedTokens} softLimit={SoftLimitTokens} decision={Decision} window=[{WindowStart}..{WindowEnd}] recentRawCount={RecentRawCount}",
            label,
            estimatedTokens,
            _policy.SoftLimitTokens,
            decision,
            windowStartSequence?.ToString() ?? "-",
            windowEndSequence?.ToString() ?? "-",
            recentRawCount);
    }

    private async Task<ProxyChatCompletionResult> CompleteAsync(
        PreparedRequest prepared,
        UpstreamChatResult upstreamResult,
        TurnPhaseTiming timing,
        CancellationToken cancellationToken)
    {
        var sequence = prepared.NextSequence;
        if (prepared.ToolSchema is not null)
        {
            foreach (var turn in prepared.ToolSchema.Session.PendingPersistedTurns)
            {
                PersistMessage(
                    prepared.Conversation.Id,
                    sequence++,
                    turn.AssistantMessage,
                    _clock.UtcNow);
                PersistMessage(
                    prepared.Conversation.Id,
                    sequence++,
                    turn.ToolMessage,
                    _clock.UtcNow);
            }
        }

        var assistantContent = string.IsNullOrWhiteSpace(upstreamResult.Content)
            ? SummarizeAssistantContent(upstreamResult.AssistantMessageJson)
            : upstreamResult.Content;
        var assistantWireJson = upstreamResult.AssistantMessageJson;

        var assistantMessage = new ChatMessage(
            MessageRole.Assistant,
            assistantContent,
            ParseOptionalWire(assistantWireJson));
        var assistantTokenCount = upstreamResult.CompletionTokens
            ?? _tokenEstimator.CountTokens([assistantMessage]);
        var assistantEntity = ConversationMessage.Create(
            prepared.Conversation.Id,
            sequence,
            MessageRole.Assistant,
            assistantContent,
            assistantTokenCount,
            _clock.UtcNow,
            assistantWireJson);

        _messageRepository.Add(assistantEntity);
        sequence++;

        if (prepared.ToolSchema is not null)
        {
            foreach (var toolResult in prepared.ToolSchema.Session.PendingLocalToolResults)
            {
                PersistMessage(
                    prepared.Conversation.Id,
                    sequence++,
                    toolResult,
                    _clock.UtcNow);
            }
        }

        prepared.Conversation.SetSyncedMessageCount(prepared.IncomingMessageCount + 1, _clock.UtcNow);

        if (!prepared.SkipCompression && prepared.MetricsPrepare is not null)
        {
            var sentPayload = prepared.UpstreamRequest.RewrittenClientRequest
                ?? prepared.UpstreamRequest.OriginalClientRequest;
            await _metricsRecorder.RecordSuccessfulTurnAsync(
                new SuccessfulTurnMetricInput(
                    prepared.Conversation.Id,
                    prepared.Endpoint.ResolveOutboundModel(prepared.UpstreamRequest.OriginalClientRequest),
                    prepared.MetricsPrepare.RequestStartedAt,
                    prepared.MetricsPrepare.RawInputTokensEstimated,
                    prepared.EstimatedTokens,
                    upstreamResult.PromptTokens,
                    upstreamResult.CompletionTokens,
                    assistantTokenCount,
                    prepared.Decision,
                    prepared.MetricsPrepare.TrimTriggered,
                    prepared.MetricsPrepare.WorkingMemoryVersionUsed,
                    prepared.MetricsPrepare.RawMessageCount,
                    prepared.UpstreamRequest.Messages.Count,
                    prepared.MetricsPrepare.RequestHash,
                    MetricsPayloadHasher.HashJsonElement(sentPayload),
                    timing.ToTurnTimings()),
                cancellationToken);
        }

        if (prepared.InlineFollowUpEligible)
        {
            // Phase 1: durable visible transcript + turn metrics before wrap-up.
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var midChainPrefix = ToolCallChainState.HasOpenToolCalls([assistantEntity]);
            var wrapUpMode = midChainPrefix
                ? InlineWrapUpMode.MidChainPrefix
                : InlineWrapUpMode.StopTurn;
            if (wrapUpMode == InlineWrapUpMode.MidChainPrefix)
            {
                _logger.LogInformation(
                    "Inline mid-chain prefix wrap-up for conversation {ConversationId}: final assistant has open tool calls; folding closed prefix only.",
                    prepared.Conversation.Id);
            }

            await RunInlineWrapUpAndAcceptAsync(
                prepared,
                upstreamResult,
                assistantContent,
                assistantEntity,
                wrapUpMode,
                cancellationToken);
            // Phase 2: CompressionEvent ± WM/fold (or Fail).
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        else
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        if (prepared.SkipCompression)
        {
            _logger.LogDebug(
                "Post-response Inline wrap-up skipped for conversation {ConversationId} (pass-through mode).",
                prepared.Conversation.Id);
        }
        else if (!prepared.InlineFollowUpEligible)
        {
            _logger.LogDebug(
                "Post-response Inline wrap-up not eligible for conversation {ConversationId}: estimatedTokens={EstimatedTokens} softLimit={SoftLimitTokens} decision={Decision}.",
                prepared.Conversation.Id,
                prepared.EstimatedTokens,
                _policy.SoftLimitTokens,
                prepared.Decision);
        }

        var promptTokens = upstreamResult.PromptTokens ?? prepared.EstimatedTokens;
        await _toolSchemaOrchestrator.OnRequestCompletedAsync(
            prepared.Conversation.Id,
            assistantWireJson,
            cancellationToken);
        return new ProxyChatCompletionResult(
            prepared.Conversation.Id,
            assistantContent,
            upstreamResult.FinishReason,
            promptTokens,
            assistantTokenCount,
            prepared.Endpoint.ResolveOutboundModel(prepared.UpstreamRequest.OriginalClientRequest),
            prepared.EstimatedTokens,
            prepared.Decision,
            prepared.SkipCompression,
            upstreamResult.RawResponseJson);
    }

    private enum InlineWrapUpMode
    {
        StopTurn,
        MidChainPrefix
    }

    private async Task RunInlineWrapUpAndAcceptAsync(
        PreparedRequest prepared,
        UpstreamChatResult upstreamResult,
        string visibleAssistantContent,
        ConversationMessage assistantEntity,
        InlineWrapUpMode wrapUpMode,
        CancellationToken cancellationToken)
    {
        var acceptStartedAt = _clock.UtcNow;
        var existingWorkingMemory = await _workingMemoryRepository.GetLatestAsync(
            prepared.Conversation.Id,
            cancellationToken);
        var storedMessages = await _messageRepository.GetByConversationIdAsync(
            prepared.Conversation.Id,
            cancellationToken);
        var unfoldedStored = storedMessages.Where(m => !m.IsFolded);
        List<ConversationMessage> foldUniverse;
        if (wrapUpMode == InlineWrapUpMode.MidChainPrefix)
        {
            foldUniverse = unfoldedStored
                .Where(m => m.Id != assistantEntity.Id)
                .OrderBy(m => m.Sequence)
                .ToList();
        }
        else
        {
            foldUniverse = unfoldedStored.ToList();
            if (foldUniverse.TrueForAll(m => m.Id != assistantEntity.Id))
            {
                foldUniverse.Add(assistantEntity);
            }

            foldUniverse = foldUniverse.OrderBy(m => m.Sequence).ToList();
        }

        var keepRecent = _recentContextSelector.Select(foldUniverse).ToList();
        var keepIds = keepRecent.Select(m => m.Id).ToHashSet();
        // When later failed edits on path P remain unfolded, pin the last successful mutation
        // group for P so fold does not erase the post-edit tip the next hop needs.
        var pinnedSuccess = HotPathSuccessfulEditRetainer.SelectPinnedMessages(foldUniverse);
        if (pinnedSuccess.Count > 0)
        {
            var added = 0;
            foreach (var message in pinnedSuccess)
            {
                if (keepIds.Add(message.Id))
                {
                    added++;
                }
            }

            if (added > 0)
            {
                _logger.LogInformation(
                    "hot_path_successful_edit_retain conversationId={ConversationId} pinnedCount={PinnedCount} sequences={Sequences}",
                    prepared.Conversation.Id,
                    added,
                    string.Join(',', pinnedSuccess.Select(m => m.Sequence)));
            }
        }

        var foldSet = foldUniverse
            .Where(m => !keepIds.Contains(m.Id))
            .OrderBy(m => m.Sequence)
            .ToList();

        var compressionEvent = CompressionEvent.Start(
            prepared.Conversation.Id,
            CompressionMode.Inline,
            prepared.PreFollowUpEstimatedTokens,
            existingWorkingMemory?.Version,
            foldSet.Count,
            acceptStartedAt);
        _compressionEventRepository.Add(compressionEvent);

        UpstreamChatResult wrapResult;
        try
        {
            var wrapUpUser = _compressionPromptFactory.BuildInlineWrapUpUserMessage();
            IReadOnlyList<ChatMessage> wrapMessages;
            if (_cacheAlignmentOptions.Enabled && _cacheAlignment.GetSnapshot(prepared.Conversation.Id) is not null)
            {
                var messagesById = storedMessages.ToDictionary(m => m.Id);
                messagesById[assistantEntity.Id] = assistantEntity;
                ChatMessage? visibleAssistant = null;
                if (wrapUpMode == InlineWrapUpMode.StopTurn)
                {
                    visibleAssistant = new ChatMessage(
                        MessageRole.Assistant,
                        visibleAssistantContent,
                        ParseOptionalWire(upstreamResult.AssistantMessageJson));
                }

                var projection = _cacheAlignment.ProjectWrapUp(
                    prepared.Conversation.Id,
                    wrapUpMode == InlineWrapUpMode.MidChainPrefix
                        ? CacheAlignmentWrapUpMode.MidChainPrefix
                        : CacheAlignmentWrapUpMode.StopTurn,
                    visibleAssistant,
                    wrapUpUser,
                    messagesById,
                    prepared.UpstreamRequest.Messages);
                if (projection.SoftFailed)
                {
                    compressionEvent.Fail(
                        "wrapup_cache_alignment:" + (projection.SoftFailReason ?? "unknown"),
                        _clock.UtcNow);
                    _logger.LogInformation(
                        "Inline follow-up wrap-up soft-failed Cache Alignment projection for conversation {ConversationId}: {Reason}",
                        prepared.Conversation.Id,
                        projection.SoftFailReason);
                    return;
                }

                wrapMessages = projection.Messages;
            }
            else
            {
                IEnumerable<ChatMessage> wrapPrefix = prepared.UpstreamRequest.Messages;
                if (wrapUpMode == InlineWrapUpMode.StopTurn)
                {
                    // Prefer the upstream assistant wire object (incl. reasoning_content) so the follow-up
                    // continues the live turn's exact message shape for KV-cache prefix alignment.
                    var visibleAssistant = new ChatMessage(
                        MessageRole.Assistant,
                        visibleAssistantContent,
                        ParseOptionalWire(upstreamResult.AssistantMessageJson));
                    wrapPrefix = wrapPrefix.Append(visibleAssistant);
                }

                wrapMessages = wrapPrefix.Append(wrapUpUser).ToList();
            }

            // Reuse live sampling / chat_template_* for KV alignment, but omit tools so wrap-up
            // cannot continue the agent tool loop (tip alone is insufficient on local models).
            // Purpose=Compression selects compression trace labels. Stop-turn grows messages
            // (assistant + tip); mid-chain prefix tip only; stream forced off.
            var wrapRequest = prepared.UpstreamRequest with
            {
                Messages = wrapMessages,
                Stream = false,
                ReplaceMessages = true,
                Purpose = UpstreamRequestPurpose.Compression,
                // Keep live tools[] for provider KV (tools often render early in the chat template).
                // Disable further tool use via tool_choice/function_call = none.
                OriginalClientRequest = ClientRequestToolStripper.ForInlineWrapUp(
                    prepared.UpstreamRequest.OriginalClientRequest),
                RewrittenClientRequest = ClientRequestToolStripper.ForInlineWrapUp(
                    prepared.UpstreamRequest.RewrittenClientRequest)
            };

            // Stamp the live chat model onto the endpoint when Provider/Compression model are unset
            // (OriginalClientRequest already carries model for the wire body; this covers HasConfiguredModel).
            var wrapEndpoint = prepared.Endpoint.WithPreferredModel(
                prepared.Endpoint.ResolveOutboundModel(prepared.UpstreamRequest.OriginalClientRequest));
            if (!wrapEndpoint.HasConfiguredModel)
            {
                compressionEvent.Fail(
                    "wrapup_upstream:Compression requires a model. Set Provider:Model or Compression:Model, or send model on the chat request.",
                    _clock.UtcNow);
                _logger.LogInformation(
                    "Inline follow-up wrap-up skipped for conversation {ConversationId}: no outbound model.",
                    prepared.Conversation.Id);
                return;
            }

            wrapResult = await _chatCompletionClient.CompleteAsync(
                wrapEndpoint,
                wrapRequest,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            compressionEvent.Fail("wrapup_cancelled", _clock.UtcNow);
            _logger.LogInformation(
                "Inline follow-up wrap-up cancelled for conversation {ConversationId}.",
                prepared.Conversation.Id);
            return;
        }
        catch (TimeoutException)
        {
            compressionEvent.Fail("wrapup_timeout", _clock.UtcNow);
            _logger.LogInformation(
                "Inline follow-up wrap-up timed out for conversation {ConversationId}.",
                prepared.Conversation.Id);
            return;
        }
        catch (Exception ex)
        {
            var truncated = TruncateFailureMessage(ex.Message);
            compressionEvent.Fail("wrapup_upstream:" + truncated, _clock.UtcNow);
            _logger.LogInformation(
                ex,
                "Inline follow-up wrap-up upstream failed for conversation {ConversationId}.",
                prepared.Conversation.Id);
            return;
        }

        // The model ignored the protocol and kept driving the agent loop. Distinct from a malformed
        // summary: the wrap-up prompt lost authority, so surface it instead of reporting bad markdown.
        if (ToolCallWireHelper.ParseAssistantToolCalls(wrapResult.AssistantMessageJson).Count > 0
            || string.Equals(wrapResult.FinishReason, "tool_calls", StringComparison.OrdinalIgnoreCase))
        {
            compressionEvent.Fail("wrapup_tool_calls", _clock.UtcNow);
            _logger.LogWarning(
                "Inline follow-up wrap-up returned tool calls for conversation {ConversationId}: finishReason={FinishReason}. Wrap-up protocol lost authority over the live system prompt.",
                prepared.Conversation.Id,
                wrapResult.FinishReason);
            return;
        }

        var wrapBody = wrapResult.Content;
        if (string.IsNullOrWhiteSpace(wrapBody))
        {
            compressionEvent.Fail("wrapup_empty", _clock.UtcNow);
            _logger.LogInformation(
                "Inline follow-up wrap-up empty for conversation {ConversationId}.",
                prepared.Conversation.Id);
            return;
        }

        if (!WorkingMemorySanityChecker.TryAccept(wrapBody, out var acceptedWorkingMemory, out var rejectionReason))
        {
            compressionEvent.Fail($"sanity:{rejectionReason}", _clock.UtcNow);
            _logger.LogInformation(
                "Inline follow-up wrap-up failed for conversation {ConversationId}: sanity={Reason}",
                prepared.Conversation.Id,
                rejectionReason);
            return;
        }

        if (foldSet.Count == 0)
        {
            compressionEvent.Fail("empty_fold", _clock.UtcNow);
            _logger.LogInformation(
                "Inline follow-up wrap-up failed for conversation {ConversationId}: empty fold set.",
                prepared.Conversation.Id);
            return;
        }

        var compressedTokens = _tokenEstimator.CountTokens(acceptedWorkingMemory);
        var newVersion = (existingWorkingMemory?.Version ?? 0) + 1;
        var newWorkingMemory = WorkingMemory.Create(
            prepared.Conversation.Id,
            newVersion,
            acceptedWorkingMemory,
            compressedTokens,
            _clock.UtcNow);
        _workingMemoryRepository.Add(newWorkingMemory);

        foreach (var message in foldSet)
        {
            message.MarkFoldedInto(newVersion);
        }

        if (_cacheAlignmentOptions.Enabled)
        {
            var retained = foldUniverse
                .Where(m => keepIds.Contains(m.Id))
                .OrderBy(m => m.Sequence)
                .ToList();
            if (!WrapUpReadiness.TryEnsureWrapUpReady(retained, out var readyRetain, out _))
            {
                _logger.LogWarning(
                    "Cache Alignment CommitWorkingMemory EnsureWrapUpReady failed for conversation {ConversationId}; invalidating Prefix.",
                    prepared.Conversation.Id);
                _cacheAlignment.Invalidate(prepared.Conversation.Id);
            }
            else
            {
                var newPrefix = _contextBuilder.BuildLivePrefix(
                    prepared.Conversation.SystemPrompt,
                    newWorkingMemory,
                    readyRetain);
                var prefixIds = readyRetain.Select(m => m.Id).ToList();
                var watermark = readyRetain.Count == 0 ? 0 : readyRetain.Max(m => m.Sequence);
                var foldedIds = foldSet.Select(m => m.Id).ToHashSet();
                if (!_cacheAlignment.TryCommitWorkingMemory(
                        prepared.Conversation.Id,
                        newPrefix,
                        prefixIds,
                        newVersion,
                        watermark,
                        foldedIds))
                {
                    _logger.LogWarning(
                        "Cache Alignment TryCommitWorkingMemory rejected for conversation {ConversationId}; invalidating Prefix.",
                        prepared.Conversation.Id);
                    _cacheAlignment.Invalidate(prepared.Conversation.Id);
                }
            }
        }

        var tokensAreEstimated = !wrapResult.PromptTokens.HasValue || !wrapResult.CompletionTokens.HasValue;
        var promptTokens = wrapResult.PromptTokens ?? prepared.PreFollowUpEstimatedTokens;
        var completionTokens = wrapResult.CompletionTokens
            ?? _tokenEstimator.CountTokens(acceptedWorkingMemory);

        compressionEvent.Succeed(
            compressedTokens,
            newVersion,
            _clock.UtcNow,
            promptTokens,
            completionTokens,
            tokensAreEstimated);

        if (compressionEvent.TotalTokens is int overheadTokens and > 0)
        {
            await _metricsRecorder.RecordCompressionOverheadAsync(
                prepared.Conversation.Id,
                overheadTokens,
                cancellationToken);
        }

        _logger.LogInformation(
            "Inline follow-up wrap-up accepted for conversation {ConversationId}: version={Version} foldCount={FoldCount} compressedTokens={CompressedTokens}",
            prepared.Conversation.Id,
            newVersion,
            foldSet.Count,
            compressedTokens);
    }

    private static string TruncateFailureMessage(string message)
    {
        const int maxLength = 200;
        if (string.IsNullOrWhiteSpace(message))
        {
            return "unknown";
        }

        var trimmed = message.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    /// <summary>
    /// Phase clocks for the current turn. <c>TurnStartedTimestamp</c> is a
    /// <see cref="Stopwatch"/> tick so the total can be read at the metric write.
    /// </summary>
    private sealed record TurnPhaseTiming(
        long TurnStartedTimestamp,
        TimeSpan Prepare,
        TimeSpan Upstream)
    {
        public TurnTimings ToTurnTimings() => new(
            ToMilliseconds(Prepare),
            ToMilliseconds(Upstream),
            ToMilliseconds(Stopwatch.GetElapsedTime(TurnStartedTimestamp)));

        private static int ToMilliseconds(TimeSpan elapsed) =>
            (int)Math.Clamp(Math.Round(elapsed.TotalMilliseconds), 0d, int.MaxValue);
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
        int RecentRawCount,
        ToolSchemaPrepareResult? ToolSchema = null,
        TurnMetricsPrepareData? MetricsPrepare = null,
        bool InlineFollowUpEligible = false,
        bool InlineOpenStoreEmergency = false,
        int PreFollowUpEstimatedTokens = 0);
}
