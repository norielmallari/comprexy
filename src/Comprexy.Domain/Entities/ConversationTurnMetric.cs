namespace Comprexy.Domain.Entities;

/// <summary>
/// Per-turn token accounting for a successful compressed-path chat completion.
/// Compares the original client prompt baseline against the prepared upstream prompt.
/// </summary>
public class ConversationTurnMetric : EntityBase
{
    public Guid ConversationId { get; private set; }

    public int TurnIndex { get; private set; }

    public DateTimeOffset RequestStartedAt { get; private set; }

    public string Model { get; private set; } = string.Empty;

    public int RawInputTokensEstimated { get; private set; }

    public int CompressedInputTokensEstimated { get; private set; }

    public int? ActualPromptTokens { get; private set; }

    public int ActualCompletionTokens { get; private set; }

    public int BaselineTotalTokensEstimated { get; private set; }

    public int CompressedTotalTokensEstimated { get; private set; }

    public int NetTokensSaved { get; private set; }

    public double NetTokenSavingsRatio { get; private set; }

    public bool SoftBudgetExceeded { get; private set; }

    public bool HardBudgetExceeded { get; private set; }

    public bool TrimTriggered { get; private set; }

    public int? WorkingMemoryVersionUsed { get; private set; }

    public int RawMessageCount { get; private set; }

    public int SentMessageCount { get; private set; }

    public string RequestHash { get; private set; } = string.Empty;

    public string SentPayloadHash { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    private ConversationTurnMetric()
    {
    }

    public static ConversationTurnMetric Create(
        Guid conversationId,
        int turnIndex,
        DateTimeOffset requestStartedAt,
        string model,
        int rawInputTokensEstimated,
        int compressedInputTokensEstimated,
        int? actualPromptTokens,
        int actualCompletionTokens,
        bool softBudgetExceeded,
        bool hardBudgetExceeded,
        bool trimTriggered,
        int? workingMemoryVersionUsed,
        int rawMessageCount,
        int sentMessageCount,
        string requestHash,
        string sentPayloadHash,
        DateTimeOffset createdAt)
    {
        var effectivePrompt = actualPromptTokens ?? compressedInputTokensEstimated;
        var baselineTotal = rawInputTokensEstimated + actualCompletionTokens;
        var compressedTotal = effectivePrompt + actualCompletionTokens;
        var netSaved = baselineTotal - compressedTotal;
        var ratio = baselineTotal > 0
            ? Math.Round((double)netSaved / baselineTotal, 6)
            : 0d;

        return new ConversationTurnMetric
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            TurnIndex = turnIndex,
            RequestStartedAt = requestStartedAt,
            Model = model,
            RawInputTokensEstimated = rawInputTokensEstimated,
            CompressedInputTokensEstimated = compressedInputTokensEstimated,
            ActualPromptTokens = actualPromptTokens,
            ActualCompletionTokens = actualCompletionTokens,
            BaselineTotalTokensEstimated = baselineTotal,
            CompressedTotalTokensEstimated = compressedTotal,
            NetTokensSaved = netSaved,
            NetTokenSavingsRatio = ratio,
            SoftBudgetExceeded = softBudgetExceeded,
            HardBudgetExceeded = hardBudgetExceeded,
            TrimTriggered = trimTriggered,
            WorkingMemoryVersionUsed = workingMemoryVersionUsed,
            RawMessageCount = rawMessageCount,
            SentMessageCount = sentMessageCount,
            RequestHash = requestHash,
            SentPayloadHash = sentPayloadHash,
            CreatedAt = createdAt
        };
    }
}
