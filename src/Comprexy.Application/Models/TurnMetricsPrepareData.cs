namespace Comprexy.Application.Models;

/// <summary>
/// Metrics fields captured during prepare for a compressed-path turn.
/// </summary>
public sealed record TurnMetricsPrepareData(
    DateTimeOffset RequestStartedAt,
    int RawInputTokensEstimated,
    string RequestHash,
    int RawMessageCount,
    int? WorkingMemoryVersionUsed,
    bool TrimTriggered);
