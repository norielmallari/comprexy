namespace Comprexy.Application.Models.Telemetry;

/// <summary>
/// Persisted conversation metrics summary columns for telemetry reads.
/// </summary>
public sealed class ConversationSummaryRollup
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
}
