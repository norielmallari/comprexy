using Comprexy.Application.Models.Telemetry;
using Comprexy.Application.Services;
using Comprexy.Domain.Entities;
using Comprexy.Domain.Enums;

namespace Comprexy.ControlApi.Contracts.Metrics;

public sealed class ConversationMetricsListItemDto
{
    public Guid ConversationId { get; init; }

    public int TotalTurns { get; init; }

    public long TotalRawInputTokensEstimated { get; init; }

    public long TotalActualTokensEstimated { get; init; }

    public long TotalNetTokensSaved { get; init; }

    public long TotalVirtualToolsTokensSaved { get; init; }

    public double AverageTokenSavingsRatio { get; init; }

    public long TotalCompressionOverheadTokens { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }

    public PromptTokenBasis PromptTokenBasis { get; init; }
}

public sealed class ConversationMetricsSummaryDto
{
    public Guid ConversationId { get; init; }

    public int TotalTurns { get; init; }

    public long TotalRawInputTokensEstimated { get; init; }

    public long TotalCompressedPromptTokens { get; init; }

    public long TotalCompletionTokens { get; init; }

    public long TotalCompressionOverheadTokens { get; init; }

    public long TotalBaselineTokensEstimated { get; init; }

    public long TotalActualTokensEstimated { get; init; }

    public long TotalNetTokensSaved { get; init; }

    public long TotalVirtualToolsTokensSaved { get; init; }

    public double AverageTokenSavingsRatio { get; init; }

    public int CompressionEventCount { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }

    public PromptTokenBasis PromptTokenBasis { get; init; }

    /// <summary>
    /// Sticky effective-settings JSON for this conversation, or null when unbound (UI shows N/A).
    /// </summary>
    public string? EffectiveSettingsJson { get; init; }
}

public sealed class ConversationTurnMetricDto
{
    public Guid Id { get; init; }

    public int TurnIndex { get; init; }

    public DateTimeOffset RequestStartedAt { get; init; }

    public string Model { get; init; } = string.Empty;

    public int RawInputTokensEstimated { get; init; }

    public int? IrFullInputTokensEstimated { get; init; }

    public int CompressedInputTokensEstimated { get; init; }

    /// <summary>
    /// Prepared-prompt split; named segments plus <see cref="HistoryTokensEstimated"/> sum to
    /// the prepared basis. Catalog/rules come from the turn row; system/WM are derived at query.
    /// </summary>
    public int SystemPromptTokensEstimated { get; init; }

    public int WorkingMemoryTokensEstimated { get; init; }

    public int PreparedVirtualToolSchemaTokensEstimated { get; init; }

    public int PreparedClientToolSchemaTokensEstimated { get; init; }

    public int PreparedRulesTokensEstimated { get; init; }

    public int HistoryTokensEstimated { get; init; }

    public int? ActualPromptTokens { get; init; }

    public int ActualCompletionTokens { get; init; }

    public int BaselineTotalTokensEstimated { get; init; }

    public int CompressedTotalTokensEstimated { get; init; }

    public int NetTokensSaved { get; init; }

    public double NetTokenSavingsRatio { get; init; }

    public int? VirtualToolsTokensSaved { get; init; }

    /// <summary>True when IrFull was not captured (pre-migration / mixed-axis SoftBudget).</summary>
    public bool IsLegacyMixedAxis { get; init; }

    public bool SoftBudgetExceeded { get; init; }

    public bool HardBudgetExceeded { get; init; }

    public bool TrimTriggered { get; init; }

    public int? WorkingMemoryVersionUsed { get; init; }

    public int RawMessageCount { get; init; }

    public int SentMessageCount { get; init; }

    /// <summary>
    /// Proxy turn wall clock (prepare + upstream + persist), excluding Inline wrap-up.
    /// Null on turns recorded before timing capture existed.
    /// </summary>
    public int? DurationMs { get; init; }

    public int? UpstreamDurationMs { get; init; }

    public int? PrepareDurationMs { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public PromptTokenBasis PromptTokenBasis { get; init; }
}

public static class ConversationMetricsMapper
{
    public static ConversationMetricsListItemDto ToListItem(ConversationMetricsSummary summary) =>
        ToListItem(summary, turns: null, PromptTokenBasis.Estimated);

    public static ConversationMetricsListItemDto ToListItem(
        ConversationMetricsSummary summary,
        IReadOnlyList<ConversationTurnMetric>? turns,
        PromptTokenBasis basis)
    {
        if (basis == PromptTokenBasis.Estimated || turns is null)
        {
            return new ConversationMetricsListItemDto
            {
                ConversationId = summary.ConversationId,
                TotalTurns = summary.TotalTurns,
                TotalRawInputTokensEstimated = summary.TotalRawInputTokensEstimated,
                TotalActualTokensEstimated = summary.TotalActualTokensEstimated,
                TotalNetTokensSaved = summary.TotalNetTokensSaved,
                TotalVirtualToolsTokensSaved = summary.TotalVirtualToolsTokensSaved,
                AverageTokenSavingsRatio = summary.AverageTokenSavingsRatio,
                TotalCompressionOverheadTokens = summary.TotalCompressionOverheadTokens,
                UpdatedAt = summary.UpdatedAt,
                PromptTokenBasis = PromptTokenBasis.Estimated
            };
        }

        var projected = ProjectSummary(summary, turns);
        return new ConversationMetricsListItemDto
        {
            ConversationId = summary.ConversationId,
            TotalTurns = summary.TotalTurns,
            TotalRawInputTokensEstimated = projected.TotalRawInputTokens,
            TotalActualTokensEstimated = projected.TotalActualTokens,
            TotalNetTokensSaved = projected.TotalNetTokensSaved,
            TotalVirtualToolsTokensSaved = projected.TotalVirtualToolsTokensSaved,
            AverageTokenSavingsRatio = projected.AverageTokenSavingsRatio,
            TotalCompressionOverheadTokens = summary.TotalCompressionOverheadTokens,
            UpdatedAt = summary.UpdatedAt,
            PromptTokenBasis = PromptTokenBasis.ProviderActual
        };
    }

    public static ConversationMetricsSummaryDto ToSummaryDto(ConversationMetricsSummary summary) =>
        ToSummaryDto(summary, turns: null, PromptTokenBasis.Estimated, effectiveSettingsJson: null);

    public static ConversationMetricsSummaryDto ToSummaryDto(
        ConversationMetricsSummary summary,
        IReadOnlyList<ConversationTurnMetric>? turns,
        PromptTokenBasis basis,
        string? effectiveSettingsJson = null)
    {
        if (basis == PromptTokenBasis.Estimated || turns is null)
        {
            return new ConversationMetricsSummaryDto
            {
                ConversationId = summary.ConversationId,
                TotalTurns = summary.TotalTurns,
                TotalRawInputTokensEstimated = summary.TotalRawInputTokensEstimated,
                TotalCompressedPromptTokens = summary.TotalCompressedPromptTokens,
                TotalCompletionTokens = summary.TotalCompletionTokens,
                TotalCompressionOverheadTokens = summary.TotalCompressionOverheadTokens,
                TotalBaselineTokensEstimated = summary.TotalBaselineTokensEstimated,
                TotalActualTokensEstimated = summary.TotalActualTokensEstimated,
                TotalNetTokensSaved = summary.TotalNetTokensSaved,
                TotalVirtualToolsTokensSaved = summary.TotalVirtualToolsTokensSaved,
                AverageTokenSavingsRatio = summary.AverageTokenSavingsRatio,
                CompressionEventCount = summary.CompressionEventCount,
                CreatedAt = summary.CreatedAt,
                UpdatedAt = summary.UpdatedAt,
                PromptTokenBasis = PromptTokenBasis.Estimated,
                EffectiveSettingsJson = effectiveSettingsJson
            };
        }

        var projected = ProjectSummary(summary, turns);
        return new ConversationMetricsSummaryDto
        {
            ConversationId = summary.ConversationId,
            TotalTurns = summary.TotalTurns,
            TotalRawInputTokensEstimated = projected.TotalRawInputTokens,
            TotalCompressedPromptTokens = projected.TotalCompressedPromptTokens,
            TotalCompletionTokens = projected.TotalCompletionTokens,
            TotalCompressionOverheadTokens = summary.TotalCompressionOverheadTokens,
            TotalBaselineTokensEstimated = projected.TotalBaselineTokens,
            TotalActualTokensEstimated = projected.TotalActualTokens,
            TotalNetTokensSaved = projected.TotalNetTokensSaved,
            TotalVirtualToolsTokensSaved = projected.TotalVirtualToolsTokensSaved,
            AverageTokenSavingsRatio = projected.AverageTokenSavingsRatio,
            CompressionEventCount = summary.CompressionEventCount,
            CreatedAt = summary.CreatedAt,
            UpdatedAt = summary.UpdatedAt,
            PromptTokenBasis = PromptTokenBasis.ProviderActual,
            EffectiveSettingsJson = effectiveSettingsJson
        };
    }

    public static ConversationTurnMetricDto ToTurnDto(
        ConversationTurnMetric turn,
        ConversationTurnContextBreakdown? breakdown = null,
        PromptTokenBasis basis = PromptTokenBasis.Estimated)
    {
        var projected = PromptTokenBasisProjector.Project(turn, basis);
        var compressedInput = projected.CompressedInputTokens;
        return new ConversationTurnMetricDto
        {
            Id = turn.Id,
            TurnIndex = turn.TurnIndex,
            RequestStartedAt = turn.RequestStartedAt,
            Model = turn.Model,
            RawInputTokensEstimated = projected.RawInputTokens,
            IrFullInputTokensEstimated = projected.IrFullInputTokens,
            CompressedInputTokensEstimated = compressedInput,
            SystemPromptTokensEstimated = breakdown?.SystemPromptTokensEstimated ?? 0,
            WorkingMemoryTokensEstimated = breakdown?.WorkingMemoryTokensEstimated ?? 0,
            PreparedVirtualToolSchemaTokensEstimated =
                breakdown?.PreparedVirtualToolSchemaTokensEstimated ?? 0,
            PreparedClientToolSchemaTokensEstimated =
                breakdown?.PreparedClientToolSchemaTokensEstimated ?? 0,
            PreparedRulesTokensEstimated = breakdown?.PreparedRulesTokensEstimated ?? 0,
            HistoryTokensEstimated = breakdown?.HistoryTokensEstimated ?? compressedInput,
            ActualPromptTokens = turn.ActualPromptTokens,
            ActualCompletionTokens = turn.ActualCompletionTokens,
            BaselineTotalTokensEstimated = projected.BaselineTotalTokens,
            CompressedTotalTokensEstimated = projected.CompressedTotalTokens,
            NetTokensSaved = projected.NetTokensSaved,
            NetTokenSavingsRatio = projected.NetTokenSavingsRatio,
            VirtualToolsTokensSaved = projected.VirtualToolsTokensSaved,
            IsLegacyMixedAxis = projected.IrFullInputTokens is null,
            SoftBudgetExceeded = turn.SoftBudgetExceeded,
            HardBudgetExceeded = turn.HardBudgetExceeded,
            TrimTriggered = turn.TrimTriggered,
            WorkingMemoryVersionUsed = turn.WorkingMemoryVersionUsed,
            RawMessageCount = turn.RawMessageCount,
            SentMessageCount = turn.SentMessageCount,
            DurationMs = turn.DurationMs,
            UpstreamDurationMs = turn.UpstreamDurationMs,
            PrepareDurationMs = turn.PrepareDurationMs,
            CreatedAt = turn.CreatedAt,
            PromptTokenBasis = basis
        };
    }

    private static (
        long TotalRawInputTokens,
        long TotalCompressedPromptTokens,
        long TotalCompletionTokens,
        long TotalBaselineTokens,
        long TotalActualTokens,
        long TotalNetTokensSaved,
        long TotalVirtualToolsTokensSaved,
        double AverageTokenSavingsRatio) ProjectSummary(
        ConversationMetricsSummary summary,
        IReadOnlyList<ConversationTurnMetric> turns)
    {
        long raw = 0;
        long compressedPrompt = 0;
        long completion = 0;
        long baseline = 0;
        long compressedTotals = 0;
        long virtualTools = 0;
        foreach (var turn in turns)
        {
            var p = PromptTokenBasisProjector.Project(turn, PromptTokenBasis.ProviderActual);
            raw += p.RawInputTokens;
            compressedPrompt += p.CompressedInputTokens;
            completion += p.ActualCompletionTokens;
            baseline += p.BaselineTotalTokens;
            compressedTotals += p.CompressedTotalTokens;
            virtualTools += p.VirtualToolsTokensSaved ?? 0;
        }

        var totalActual = compressedTotals + summary.TotalCompressionOverheadTokens;
        var totalNet = baseline - totalActual;
        var average = baseline > 0
            ? Math.Round((double)totalNet / baseline, 6)
            : 0d;

        return (raw, compressedPrompt, completion, baseline, totalActual, totalNet, virtualTools, average);
    }
}
