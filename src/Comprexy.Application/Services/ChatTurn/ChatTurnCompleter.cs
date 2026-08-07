using Comprexy.Application.Abstractions;
using Comprexy.Application.Configuration;
using Comprexy.Application.Models;
using Comprexy.Application.Services.Settings;
using Comprexy.Domain.Entities;
using Comprexy.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Comprexy.Application.Services.ChatTurn;

public sealed class ChatTurnCompleter
{
    private readonly IConversationMessageRepository _messageRepository;
    private readonly ToolSchemaOrchestrator _toolSchemaOrchestrator;
    private readonly IConversationMetricsRecorder _metricsRecorder;
    private readonly InlineWrapUpRunner _inlineWrapUpRunner;
    private readonly ChatTurnMessageHelper _messageHelper;
    private readonly ITokenEstimator _tokenEstimator;
    private readonly IClock _clock;
    private readonly IEffectiveSettingsAccessor _effectiveSettings;
    private readonly IOptionsMonitor<ContextPolicyOptions> _policy;
    private readonly ILogger<ChatTurnCompleter> _logger;

    public ChatTurnCompleter(
        IConversationMessageRepository messageRepository,
        ToolSchemaOrchestrator toolSchemaOrchestrator,
        IConversationMetricsRecorder metricsRecorder,
        InlineWrapUpRunner inlineWrapUpRunner,
        ChatTurnMessageHelper messageHelper,
        ITokenEstimator tokenEstimator,
        IClock clock,
        IEffectiveSettingsAccessor effectiveSettings,
        IOptionsMonitor<ContextPolicyOptions> policy,
        ILogger<ChatTurnCompleter> logger)
    {
        _messageRepository = messageRepository;
        _toolSchemaOrchestrator = toolSchemaOrchestrator;
        _metricsRecorder = metricsRecorder;
        _inlineWrapUpRunner = inlineWrapUpRunner;
        _messageHelper = messageHelper;
        _tokenEstimator = tokenEstimator;
        _clock = clock;
        _effectiveSettings = effectiveSettings;
        _policy = policy;
        _logger = logger;
    }

    /// <summary>Test / legacy ctor (internal so MS DI sees only the public primary).</summary>
    internal ChatTurnCompleter(
        IConversationMessageRepository messageRepository,
        ToolSchemaOrchestrator toolSchemaOrchestrator,
        IConversationMetricsRecorder metricsRecorder,
        InlineWrapUpRunner inlineWrapUpRunner,
        ChatTurnMessageHelper messageHelper,
        ITokenEstimator tokenEstimator,
        IClock clock,
        IOptions<ContextPolicyOptions> policy,
        ILogger<ChatTurnCompleter> logger)
        : this(
            messageRepository,
            toolSchemaOrchestrator,
            metricsRecorder,
            inlineWrapUpRunner,
            messageHelper,
            tokenEstimator,
            clock,
            UnsetEffectiveSettingsAccessor.Instance,
            new FixedOptionsMonitor<ContextPolicyOptions>(policy),
            logger)
    {
    }

    public async Task<ProxyChatCompletionResult> CompleteAsync(
        PreparedRequest prepared,
        UpstreamChatResult upstreamResult,
        TurnPhaseTiming timing,
        Func<CancellationToken, Task> flushChatUnitAsync,
        CancellationToken cancellationToken)
    {
        var sequence = prepared.NextSequence;
        if (prepared.ToolSchema is not null)
        {
            foreach (var turn in prepared.ToolSchema.Session.PendingPersistedTurns)
            {
                _messageHelper.PersistMessage(
                    prepared.Conversation.Id,
                    sequence++,
                    turn.AssistantMessage,
                    _clock.UtcNow);
                foreach (var toolMessage in turn.ToolMessages)
                {
                    _messageHelper.PersistMessage(
                        prepared.Conversation.Id,
                        sequence++,
                        toolMessage,
                        _clock.UtcNow);
                }
            }
        }

        var assistantContent = string.IsNullOrWhiteSpace(upstreamResult.Content)
            ? ChatTurnMessageHelper.SummarizeAssistantContent(upstreamResult.AssistantMessageJson)
            : upstreamResult.Content;
        var assistantWireJson = upstreamResult.AssistantMessageJson;

        var assistantMessage = new ChatMessage(
            MessageRole.Assistant,
            assistantContent,
            ChatTurnMessageHelper.ParseOptionalWire(assistantWireJson));
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
                _messageHelper.PersistMessage(
                    prepared.Conversation.Id,
                    sequence++,
                    toolResult,
                    _clock.UtcNow);
            }
        }

        prepared.Conversation.SetSyncedMessageCount(prepared.IncomingMessageCount + 1, _clock.UtcNow);

        if (prepared.MetricsPrepare is not null)
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
                    timing.ToTurnTimings(),
                    prepared.MetricsPrepare.IrFullInputTokensEstimated,
                    PreparedVirtualToolSchemaTokensEstimated:
                        prepared.MetricsPrepare.PreparedVirtualToolSchemaTokensEstimated,
                    PreparedClientToolSchemaTokensEstimated:
                        prepared.MetricsPrepare.PreparedClientToolSchemaTokensEstimated,
                    PreparedRulesTokensEstimated:
                        prepared.MetricsPrepare.PreparedRulesTokensEstimated),
                cancellationToken);
        }

        if (prepared.InlineFollowUpEligible)
        {
            // Phase 1: durable visible transcript + turn metrics before wrap-up.
            await flushChatUnitAsync(cancellationToken);

            var forceMidChain = prepared.InlineOpenStoreEmergency
                || ToolCallChainState.HasOpenToolCalls([assistantEntity]);
            var wrapUpMode = forceMidChain
                ? InlineWrapUpMode.MidChainPrefix
                : InlineWrapUpMode.StopTurn;
            if (prepared.InlineOpenStoreEmergency)
            {
                _logger.LogInformation(
                    "Inline mid-chain prefix wrap-up forced for conversation {ConversationId}: prepare observed an open stored tool chain; folding closed prefix only.",
                    prepared.Conversation.Id);
            }
            else if (wrapUpMode == InlineWrapUpMode.MidChainPrefix)
            {
                _logger.LogInformation(
                    "Inline mid-chain prefix wrap-up for conversation {ConversationId}: final assistant has open tool calls; folding closed prefix only.",
                    prepared.Conversation.Id);
            }

            await _inlineWrapUpRunner.RunAsync(
                prepared,
                upstreamResult,
                assistantContent,
                assistantEntity,
                wrapUpMode,
                cancellationToken);
            // Phase 2: CompressionEvent ± WM/fold (or Fail).
            await flushChatUnitAsync(cancellationToken);
        }
        else
        {
            await flushChatUnitAsync(cancellationToken);
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
                _effectiveSettings.IsSet
                    ? _effectiveSettings.Current.SoftLimitTokens
                    : _policy.CurrentValue.SoftLimitTokens,
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
}
