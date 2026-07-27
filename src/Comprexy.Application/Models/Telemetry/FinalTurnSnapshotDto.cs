namespace Comprexy.Application.Models.Telemetry;

public sealed class FinalTurnSnapshotDto
{
    public Guid ConversationId { get; init; }

    public int TurnIndex { get; init; }

    public int BaselineTotalTokensEstimated { get; init; }

    public int CompressedTotalTokensEstimated { get; init; }

    public int NetTokensSaved { get; init; }

    public double NetTokenSavingsRatio { get; init; }

    public int RawMessageCount { get; init; }

    public int SentMessageCount { get; init; }

    public int? WorkingMemoryVersionUsed { get; init; }

    public bool TrimTriggered { get; init; }
}
