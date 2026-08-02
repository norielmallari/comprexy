namespace Comprexy.Application.Models;

/// <summary>
/// Proxy-measured wall clocks for one chat turn.
/// <paramref name="DurationMs"/> covers prepare + upstream + persist up to the metric write;
/// Inline wrap-up is timed separately on <c>CompressionEvent.DurationMs</c>.
/// </summary>
public sealed record TurnTimings(
    int PrepareDurationMs,
    int UpstreamDurationMs,
    int DurationMs);
