namespace Comprexy.Application.Models.Benchmarking;

/// <summary>
/// Separated token and timing totals for one conversation side.
/// </summary>
public sealed record ConversationTokenTotals
{
    public required Guid ConversationId { get; init; }

    public required int TurnCount { get; init; }

    public required long InputTokens { get; init; }

    public required long OutputTokens { get; init; }

    public required long OverheadTokens { get; init; }

    public long TotalSentTokens => InputTokens + OutputTokens + OverheadTokens;

    public long? WallClockMs { get; init; }

    public long? TotalProxyDurationMs { get; init; }

    public long? TotalUpstreamDurationMs { get; init; }

    public long? TotalPrepareDurationMs { get; init; }
}
