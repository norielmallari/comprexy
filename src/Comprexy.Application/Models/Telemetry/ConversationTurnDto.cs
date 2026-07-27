namespace Comprexy.Application.Models.Telemetry;

public sealed class ConversationTurnDto
{
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

    public double CompressionRatio { get; init; }

    public int? PromptEstimateError { get; init; }

    public double? MessageReductionRatio { get; init; }

    public bool SoftBudgetExceeded { get; init; }

    public bool HardBudgetExceeded { get; init; }

    public bool TrimTriggered { get; init; }

    public int? WorkingMemoryVersionUsed { get; init; }

    public int RawMessageCount { get; init; }

    public int SentMessageCount { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}
