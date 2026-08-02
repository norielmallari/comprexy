using System.Text.Json;
using Comprexy.Application.Abstractions;
using Comprexy.Application.Configuration;
using Comprexy.Application.Mapping;
using Comprexy.Application.Models;
using Comprexy.Application.Services.CacheAlignment;
using Comprexy.Application.Tracing;
using Comprexy.Domain.Entities;
using Comprexy.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Comprexy.Application.Services.ChatTurn;

public sealed class ChatTurnPreparer
{
    private readonly IConversationRepository _conversationRepository;
    private readonly IConversationMessageRepository _messageRepository;
    private readonly IWorkingMemoryRepository _workingMemoryRepository;
    private readonly ICompressionEventRepository _compressionEventRepository;
    private readonly ITokenEstimator _tokenEstimator;
    private readonly ContextBuilder _contextBuilder;
    private readonly ICacheAlignmentService _cacheAlignment;
    private readonly ContextBudgetEvaluator _budgetEvaluator;
    private readonly CompressionPromptFactory _compressionPromptFactory;
    private readonly ToolSchemaOrchestrator _toolSchemaOrchestrator;
    private readonly ClientHistorySynchronizer _historySynchronizer;
    private readonly OutgoingContextMaterializer _contextMaterializer;
    private readonly ChatTurnMessageHelper _messageHelper;
    private readonly ProviderEndpointResolver _endpointResolver;
    private readonly IConversationMetricsRecorder _metricsRecorder;
    private readonly IClock _clock;
    private readonly ContextPolicyOptions _policy;
    private readonly ProxyOptions _proxyOptions;
    private readonly CacheAlignmentOptions _cacheAlignmentOptions;
    private readonly IPayloadTraceLogger _payloadTrace;
    private readonly IRequestTraceFileSession _requestTraceFiles;
    private readonly ILogger<ChatTurnPreparer> _logger;

    public ChatTurnPreparer(
        IConversationRepository conversationRepository,
        IConversationMessageRepository messageRepository,
        IWorkingMemoryRepository workingMemoryRepository,
        ICompressionEventRepository compressionEventRepository,
        ITokenEstimator tokenEstimator,
        ContextBuilder contextBuilder,
        ICacheAlignmentService cacheAlignment,
        ContextBudgetEvaluator budgetEvaluator,
        CompressionPromptFactory compressionPromptFactory,
        ToolSchemaOrchestrator toolSchemaOrchestrator,
        ClientHistorySynchronizer historySynchronizer,
        OutgoingContextMaterializer contextMaterializer,
        ChatTurnMessageHelper messageHelper,
        ProviderEndpointResolver endpointResolver,
        IConversationMetricsRecorder metricsRecorder,
        IClock clock,
        IOptions<ContextPolicyOptions> policy,
        IOptions<ProxyOptions> proxyOptions,
        IOptions<CacheAlignmentOptions> cacheAlignmentOptions,
        IPayloadTraceLogger payloadTrace,
        IRequestTraceFileSession requestTraceFiles,
        ILogger<ChatTurnPreparer> logger)
    {
        _conversationRepository = conversationRepository;
        _messageRepository = messageRepository;
        _workingMemoryRepository = workingMemoryRepository;
        _compressionEventRepository = compressionEventRepository;
        _tokenEstimator = tokenEstimator;
        _contextBuilder = contextBuilder;
        _cacheAlignment = cacheAlignment;
        _budgetEvaluator = budgetEvaluator;
        _compressionPromptFactory = compressionPromptFactory;
        _toolSchemaOrchestrator = toolSchemaOrchestrator;
        _historySynchronizer = historySynchronizer;
        _contextMaterializer = contextMaterializer;
        _messageHelper = messageHelper;
        _endpointResolver = endpointResolver;
        _metricsRecorder = metricsRecorder;
        _clock = clock;
        _policy = policy.Value;
        _proxyOptions = proxyOptions.Value;
        _cacheAlignmentOptions = cacheAlignmentOptions.Value;
        _payloadTrace = payloadTrace;
        _requestTraceFiles = requestTraceFiles;
        _logger = logger;
    }

    public async Task<PreparedRequest> PrepareAsync(
        IncomingChatRequest request,
        string conversationKey,
        Func<CancellationToken, Task> flushChatUnitAsync,
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
            _historySynchronizer.EnrichStoredMessagesFromClientHistory(storedMessages, request.Messages);
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
            await _historySynchronizer.ApplyClientSnapshotRewindAsync(
                conversation,
                storedMessages,
                keepNonSystemCount,
                flushChatUnitAsync,
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
                await flushChatUnitAsync(cancellationToken);
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
                newlyPersisted.Add(_messageHelper.PersistMessage(conversation.Id, nextSequence++, message, now));
            }

            // Inbound distill commit: persist rewritten tool observations (and staged result_shapes)
            // before isolated dual-id Complete (docs/ARCHITECTURE.md § Persistence — Unit of Work ownership).
            if (inboundRewrite.CompletedClientCallIds.Count > 0 ||
                inboundRewrite.StagedShapeClientToolNames.Count > 0)
            {
                await flushChatUnitAsync(cancellationToken);
                if (inboundRewrite.StagedShapeClientToolNames.Count > 0)
                {
                    _toolSchemaOrchestrator.ConfirmShapeMirrorPersisted(
                        conversation.Id,
                        inboundRewrite.StagedShapeClientToolNames);
                }
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
                newlyPersisted.Add(_messageHelper.PersistMessage(conversation.Id, nextSequence++, message, now));
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
        if (allMessages.Count == 0 || !ChatTurnMessageHelper.IsSameTip(allMessages[^1], requestTip))
        {
            if (virtualToolsInboundApplied &&
                ChatTurnMessageHelper.IsVirtualToolsExpectedTipMismatch(requestTip, nonSystemNewMessages))
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
                var repaired = _messageHelper.PersistMessage(conversation.Id, nextSequence++, requestTip, now);
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
        var recentRaw = _contextMaterializer.PrepareRecentRawForChatTemplate(
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
            outgoing = _contextMaterializer.MaterializeOutgoingViaCacheAlignment(
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

        outgoing = _contextMaterializer.EnsureOutgoingEndsAtTip(
            conversation.Id,
            outgoing,
            currentUserMessage,
            currentMessageEntity.Sequence);

        var toolSchema = await TryPrepareToolSchemaAsync(
            conversation.Id,
            outgoing,
            request.RawRequest,
            flushChatUnitAsync,
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
            flushChatUnitAsync,
            cancellationToken,
            toolSchema,
            metricsPrepare);
    }

    private async Task<ToolSchemaPrepareResult?> TryPrepareToolSchemaAsync(
        Guid conversationId,
        IReadOnlyList<ChatMessage> outgoingMessages,
        JsonElement? rawRequest,
        Func<CancellationToken, Task> flushChatUnitAsync,
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
            await flushChatUnitAsync(cancellationToken);
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
        Func<CancellationToken, Task> flushChatUnitAsync,
        CancellationToken cancellationToken,
        ToolSchemaPrepareResult? precomputedToolSchema = null,
        TurnMetricsPrepareData? metricsPrepare = null)
    {
        var toolSchema = precomputedToolSchema
            ?? await TryPrepareToolSchemaAsync(
                conversation.Id,
                outgoingMessages,
                rawRequest,
                flushChatUnitAsync,
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
}
