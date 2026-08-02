using Comprexy.Application.Abstractions;
using Comprexy.Application.Configuration;
using Comprexy.Application.Models;
using Comprexy.Application.Services.CacheAlignment;
using Comprexy.Domain.Entities;
using Comprexy.Domain.Enums;
using Comprexy.Application.Services.Rules;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Comprexy.Application.Services.ChatTurn;

public sealed class InlineWrapUpRunner
{
    private readonly IConversationMessageRepository _messageRepository;
    private readonly IWorkingMemoryRepository _workingMemoryRepository;
    private readonly ICompressionEventRepository _compressionEventRepository;
    private readonly CompressionPromptFactory _compressionPromptFactory;
    private readonly IChatCompletionClient _chatCompletionClient;
    private readonly ITokenEstimator _tokenEstimator;
    private readonly ContextBuilder _contextBuilder;
    private readonly ICacheAlignmentService _cacheAlignment;
    private readonly RecentContextSelector _recentContextSelector;
    private readonly IConversationMetricsRecorder _metricsRecorder;
    private readonly IClock _clock;
    private readonly CacheAlignmentOptions _cacheAlignmentOptions;
    private readonly ILogger<InlineWrapUpRunner> _logger;

    public InlineWrapUpRunner(
        IConversationMessageRepository messageRepository,
        IWorkingMemoryRepository workingMemoryRepository,
        ICompressionEventRepository compressionEventRepository,
        CompressionPromptFactory compressionPromptFactory,
        IChatCompletionClient chatCompletionClient,
        ITokenEstimator tokenEstimator,
        ContextBuilder contextBuilder,
        ICacheAlignmentService cacheAlignment,
        RecentContextSelector recentContextSelector,
        IConversationMetricsRecorder metricsRecorder,
        IClock clock,
        IOptions<CacheAlignmentOptions> cacheAlignmentOptions,
        ILogger<InlineWrapUpRunner> logger)
    {
        _messageRepository = messageRepository;
        _workingMemoryRepository = workingMemoryRepository;
        _compressionEventRepository = compressionEventRepository;
        _compressionPromptFactory = compressionPromptFactory;
        _chatCompletionClient = chatCompletionClient;
        _tokenEstimator = tokenEstimator;
        _contextBuilder = contextBuilder;
        _cacheAlignment = cacheAlignment;
        _recentContextSelector = recentContextSelector;
        _metricsRecorder = metricsRecorder;
        _clock = clock;
        _cacheAlignmentOptions = cacheAlignmentOptions.Value;
        _logger = logger;
    }

    public async Task RunAsync(
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
        var unfoldedStored = storedMessages
            .Where(m => !m.IsFolded)
            .OrderBy(m => m.Sequence)
            .ToList();
        IReadOnlyList<ConversationMessage> closedPrefix = Array.Empty<ConversationMessage>();
        List<ConversationMessage> foldUniverse;
        if (wrapUpMode == InlineWrapUpMode.MidChainPrefix)
        {
            if (!WrapUpReadiness.TryEnsureWrapUpReady(
                    unfoldedStored,
                    out closedPrefix,
                    out var excludedOpen))
            {
                var failedEvent = CompressionEvent.Start(
                    prepared.Conversation.Id,
                    CompressionMode.Inline,
                    prepared.PreFollowUpEstimatedTokens,
                    existingWorkingMemory?.Version,
                    foldedMessageCount: 0,
                    now: acceptStartedAt);
                failedEvent.Fail("wrapup_fold_unrepairable", _clock.UtcNow);
                _compressionEventRepository.Add(failedEvent);
                _logger.LogWarning(
                    "Inline mid-chain prefix wrap-up soft-failed for conversation {ConversationId}: wrapup_fold_unrepairable.",
                    prepared.Conversation.Id);
                return;
            }

            foldUniverse = closedPrefix.OrderBy(m => m.Sequence).ToList();
            if (excludedOpen.Count > 0)
            {
                _logger.LogInformation(
                    "Inline mid-chain prefix wrap-up excluded open messages for conversation {ConversationId}: sequences={Sequences}.",
                    prepared.Conversation.Id,
                    string.Join(',', excludedOpen.OrderBy(m => m.Sequence).Select(m => m.Sequence)));
            }
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
            var wrapUpUser = _compressionPromptFactory.BuildInlineWrapUpUserMessage(prepared.RulesSnapshot);
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
                        ChatTurnMessageHelper.ParseOptionalWire(upstreamResult.AssistantMessageJson));
                }

                var projection = _cacheAlignment.ProjectWrapUp(
                    prepared.Conversation.Id,
                    wrapUpMode == InlineWrapUpMode.MidChainPrefix
                        ? CacheAlignmentWrapUpMode.MidChainPrefix
                        : CacheAlignmentWrapUpMode.StopTurn,
                    visibleAssistant,
                    wrapUpUser,
                    messagesById,
                    prepared.InlineOpenStoreEmergency
                        ? null
                        : prepared.UpstreamRequest.Messages);
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
                if (wrapUpMode == InlineWrapUpMode.MidChainPrefix
                    && prepared.InlineOpenStoreEmergency)
                {
                    wrapPrefix = _contextBuilder.BuildLivePrefix(
                        prepared.Conversation.SystemPrompt,
                        existingWorkingMemory,
                        closedPrefix);
                }
                else if (wrapUpMode == InlineWrapUpMode.StopTurn)
                {
                    // Prefer the upstream assistant wire object (incl. reasoning_content) so the follow-up
                    // continues the live turn's exact message shape for KV-cache prefix alignment.
                    var visibleAssistant = new ChatMessage(
                        MessageRole.Assistant,
                        visibleAssistantContent,
                        ChatTurnMessageHelper.ParseOptionalWire(upstreamResult.AssistantMessageJson));
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

        acceptedWorkingMemory = WorkingMemoryRulesSection.ReplaceRulesSection(
            acceptedWorkingMemory,
            prepared.RulesSnapshot?.FormatForWorkingMemory() ?? WorkingMemoryRulesSection.FormatSection([]));

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
}
