using Comprexy.Application.Abstractions;
using Comprexy.Application.Configuration;
using Comprexy.Application.Models;
using Comprexy.Application.Services.Settings;
using Comprexy.Domain.Entities;
using Microsoft.Extensions.Options;

namespace Comprexy.Application.Services;

public sealed class ConversationMetricsRecorder : IConversationMetricsRecorder
{
    private readonly IConversationTurnMetricRepository _turnMetricRepository;
    private readonly IConversationMetricsSummaryRepository _summaryRepository;
    private readonly IClock _clock;
    private readonly IEffectiveSettingsAccessor _effectiveSettings;
    private readonly IOptionsMonitor<MetricsOptions> _options;

    public ConversationMetricsRecorder(
        IConversationTurnMetricRepository turnMetricRepository,
        IConversationMetricsSummaryRepository summaryRepository,
        IClock clock,
        IEffectiveSettingsAccessor effectiveSettings,
        IOptionsMonitor<MetricsOptions> options)
    {
        _turnMetricRepository = turnMetricRepository;
        _summaryRepository = summaryRepository;
        _clock = clock;
        _effectiveSettings = effectiveSettings;
        _options = options;
    }

    /// <summary>Test / legacy ctor (internal so MS DI sees only the public primary).</summary>
    internal ConversationMetricsRecorder(
        IConversationTurnMetricRepository turnMetricRepository,
        IConversationMetricsSummaryRepository summaryRepository,
        IClock clock,
        IOptions<MetricsOptions> options)
        : this(
            turnMetricRepository,
            summaryRepository,
            clock,
            UnsetEffectiveSettingsAccessor.Instance,
            new FixedOptionsMonitor<MetricsOptions>(options))
    {
    }


    public bool IsEnabled =>
        _effectiveSettings.IsSet
            ? _effectiveSettings.Current.MetricsEnabled
            : _options.CurrentValue.Enabled;

    public async Task RecordSuccessfulTurnAsync(
        SuccessfulTurnMetricInput input,
        CancellationToken cancellationToken)
    {
        if (!IsEnabled)
        {
            return;
        }

        var now = _clock.UtcNow;
        var turnIndex = await _turnMetricRepository.GetMaxTurnIndexAsync(input.ConversationId, cancellationToken) + 1;
        var completionTokens = input.ActualCompletionTokens ?? input.EstimatedCompletionTokensFallback;

        var softExceeded = input.BudgetDecision is ContextBudgetDecision.ForwardWithHighPriorityCompression
            or ContextBudgetDecision.EmergencyCompressionRequired;
        var hardExceeded = input.BudgetDecision == ContextBudgetDecision.EmergencyCompressionRequired;

        var turn = ConversationTurnMetric.Create(
            input.ConversationId,
            turnIndex,
            input.RequestStartedAt,
            input.Model,
            input.RawInputTokensEstimated,
            input.CompressedInputTokensEstimated,
            input.ActualPromptTokens,
            completionTokens,
            softExceeded,
            hardExceeded,
            input.TrimTriggered,
            input.WorkingMemoryVersionUsed,
            input.RawMessageCount,
            input.SentMessageCount,
            input.RequestHash,
            input.SentPayloadHash,
            input.Timings.DurationMs,
            input.Timings.UpstreamDurationMs,
            input.Timings.PrepareDurationMs,
            now,
            input.IrFullInputTokensEstimated,
            preparedVirtualToolSchemaTokensEstimated: input.PreparedVirtualToolSchemaTokensEstimated,
            preparedClientToolSchemaTokensEstimated: input.PreparedClientToolSchemaTokensEstimated,
            preparedRulesTokensEstimated: input.PreparedRulesTokensEstimated);

        _turnMetricRepository.Add(turn);

        var summary = await _summaryRepository.FindByConversationIdAsync(input.ConversationId, cancellationToken);
        if (summary is null)
        {
            summary = ConversationMetricsSummary.Create(input.ConversationId, now);
            _summaryRepository.Add(summary);
        }

        summary.ApplyTurn(turn, now);
    }

    public async Task RecordCompressionOverheadAsync(
        Guid conversationId,
        int overheadTokens,
        CancellationToken cancellationToken)
    {
        if (!IsEnabled || overheadTokens <= 0)
        {
            return;
        }

        var now = _clock.UtcNow;
        var summary = await _summaryRepository.FindByConversationIdAsync(conversationId, cancellationToken);
        if (summary is null)
        {
            summary = ConversationMetricsSummary.Create(conversationId, now);
            _summaryRepository.Add(summary);
        }

        summary.ApplyCompressionOverhead(overheadTokens, now);
    }
}
