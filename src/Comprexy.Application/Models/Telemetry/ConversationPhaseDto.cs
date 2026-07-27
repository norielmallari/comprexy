namespace Comprexy.Application.Models.Telemetry;

public sealed class ConversationPhaseDto
{
    public string Phase { get; init; } = string.Empty;

    public int TurnStart { get; init; }

    public int TurnEnd { get; init; }

    public int? WorkingMemoryVersionUsed { get; init; }

    public bool TrimTriggered { get; init; }

    public long TotalBaselineTokensEstimated { get; init; }

    public long TotalNetTokensSaved { get; init; }

    public double WeightedSavingsRatio { get; init; }
}
