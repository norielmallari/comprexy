namespace Comprexy.Domain.Entities;

/// <summary>
/// Conversation-level rollup of token metrics for operator proof and reporting.
/// </summary>
public class ConversationMetricsSummary : EntityBase
{
    public Guid ConversationId { get; private set; }

    public int TotalTurns { get; private set; }

    public long TotalRawInputTokensEstimated { get; private set; }

    public long TotalCompressedPromptTokens { get; private set; }

    public long TotalCompletionTokens { get; private set; }

    public long TotalCompressionOverheadTokens { get; private set; }

    public long TotalBaselineTokensEstimated { get; private set; }

    public long TotalActualTokensEstimated { get; private set; }

    public long TotalNetTokensSaved { get; private set; }

    public double AverageTokenSavingsRatio { get; private set; }

    public int CompressionEventCount { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    private ConversationMetricsSummary()
    {
    }

    public static ConversationMetricsSummary Create(Guid conversationId, DateTimeOffset now)
    {
        return new ConversationMetricsSummary
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public void ApplyTurn(ConversationTurnMetric turn, DateTimeOffset now)
    {
        TotalTurns += 1;
        TotalRawInputTokensEstimated += turn.RawInputTokensEstimated;
        TotalCompressedPromptTokens += turn.ActualPromptTokens ?? turn.CompressedInputTokensEstimated;
        TotalCompletionTokens += turn.ActualCompletionTokens;
        TotalBaselineTokensEstimated += turn.BaselineTotalTokensEstimated;
        RecalculateActualAndSavings(now);
    }

    public void ApplyCompressionOverhead(int overheadTokens, DateTimeOffset now)
    {
        if (overheadTokens < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(overheadTokens));
        }

        TotalCompressionOverheadTokens += overheadTokens;
        CompressionEventCount += 1;
        RecalculateActualAndSavings(now);
    }

    private void RecalculateActualAndSavings(DateTimeOffset now)
    {
        TotalActualTokensEstimated =
            TotalCompressedPromptTokens + TotalCompletionTokens + TotalCompressionOverheadTokens;
        TotalNetTokensSaved = TotalBaselineTokensEstimated - TotalActualTokensEstimated;
        AverageTokenSavingsRatio = TotalBaselineTokensEstimated > 0
            ? Math.Round((double)TotalNetTokensSaved / TotalBaselineTokensEstimated, 6)
            : 0d;
        UpdatedAt = now;
    }
}
