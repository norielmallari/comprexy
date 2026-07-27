namespace Comprexy.Application.Models.Telemetry;

public sealed class PromptGrowthTimelineDto
{
    public Guid ConversationId { get; init; }

    public IReadOnlyList<PromptGrowthPointDto> Points { get; init; } = [];
}

public sealed class PromptGrowthPointDto
{
    public int TurnIndex { get; init; }

    public int? ActualPromptTokens { get; init; }

    public int CompressedInputTokensEstimated { get; init; }

    public int EffectivePromptTokens { get; init; }
}
