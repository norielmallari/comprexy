using Comprexy.Domain.Entities;

namespace Comprexy.Api.Contracts.Metrics;

public sealed class ConversationMetricsListItemDto
{
    public Guid ConversationId { get; init; }

    public int TotalTurns { get; init; }

    public long TotalRawInputTokensEstimated { get; init; }

    public long TotalActualTokensEstimated { get; init; }

    public long TotalNetTokensSaved { get; init; }

    public double AverageTokenSavingsRatio { get; init; }

    public long TotalCompressionOverheadTokens { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }
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

    public double AverageTokenSavingsRatio { get; init; }

    public int CompressionEventCount { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed class ConversationTurnMetricDto
{
    public Guid Id { get; init; }

    public int TurnIndex { get; init; }

    public DateTimeOffset RequestStartedAt { get; init; }

    public string Model { get; init; } = string.Empty;

    public int RawInputTokensEstimated { get; init; }

    public int CompressedInputTokensEstimated { get; init; }

    public int? ActualPromptTokens { get; init; }

    public int ActualCompletionTokens { get; init; }

    public int BaselineTotalTokensEstimated { get; init; }

    public int CompressedTotalTokensEstimated { get; init; }

    public int NetTokensSaved { get; init; }

    public double NetTokenSavingsRatio { get; init; }

    public bool SoftBudgetExceeded { get; init; }

    public bool HardBudgetExceeded { get; init; }

    public bool TrimTriggered { get; init; }

    public int? WorkingMemoryVersionUsed { get; init; }

    public int RawMessageCount { get; init; }

    public int SentMessageCount { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}

public static class ConversationMetricsMapper
{
    public static ConversationMetricsListItemDto ToListItem(ConversationMetricsSummary summary) =>
        new()
        {
            ConversationId = summary.ConversationId,
            TotalTurns = summary.TotalTurns,
            TotalRawInputTokensEstimated = summary.TotalRawInputTokensEstimated,
            TotalActualTokensEstimated = summary.TotalActualTokensEstimated,
            TotalNetTokensSaved = summary.TotalNetTokensSaved,
            AverageTokenSavingsRatio = summary.AverageTokenSavingsRatio,
            TotalCompressionOverheadTokens = summary.TotalCompressionOverheadTokens,
            UpdatedAt = summary.UpdatedAt
        };

    public static ConversationMetricsSummaryDto ToSummaryDto(ConversationMetricsSummary summary) =>
        new()
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
            AverageTokenSavingsRatio = summary.AverageTokenSavingsRatio,
            CompressionEventCount = summary.CompressionEventCount,
            CreatedAt = summary.CreatedAt,
            UpdatedAt = summary.UpdatedAt
        };

    public static ConversationTurnMetricDto ToTurnDto(ConversationTurnMetric turn) =>
        new()
        {
            Id = turn.Id,
            TurnIndex = turn.TurnIndex,
            RequestStartedAt = turn.RequestStartedAt,
            Model = turn.Model,
            RawInputTokensEstimated = turn.RawInputTokensEstimated,
            CompressedInputTokensEstimated = turn.CompressedInputTokensEstimated,
            ActualPromptTokens = turn.ActualPromptTokens,
            ActualCompletionTokens = turn.ActualCompletionTokens,
            BaselineTotalTokensEstimated = turn.BaselineTotalTokensEstimated,
            CompressedTotalTokensEstimated = turn.CompressedTotalTokensEstimated,
            NetTokensSaved = turn.NetTokensSaved,
            NetTokenSavingsRatio = turn.NetTokenSavingsRatio,
            SoftBudgetExceeded = turn.SoftBudgetExceeded,
            HardBudgetExceeded = turn.HardBudgetExceeded,
            TrimTriggered = turn.TrimTriggered,
            WorkingMemoryVersionUsed = turn.WorkingMemoryVersionUsed,
            RawMessageCount = turn.RawMessageCount,
            SentMessageCount = turn.SentMessageCount,
            CreatedAt = turn.CreatedAt
        };
}
