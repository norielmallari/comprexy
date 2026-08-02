namespace Comprexy.Application.Models.Benchmarking;

public sealed record BenchmarkComparisonTotals
{
    public required ConversationTokenTotals Baseline { get; init; }

    public required ConversationTokenTotals Compare { get; init; }

    public required BenchmarkChannelDelta Input { get; init; }

    public required BenchmarkChannelDelta Output { get; init; }

    public required BenchmarkChannelDelta Overhead { get; init; }

    public required BenchmarkChannelDelta TurnCount { get; init; }

    public BenchmarkChannelDelta? WallClockMs { get; init; }

    public BenchmarkChannelDelta? ProxyDurationMs { get; init; }

    public required IReadOnlyList<string> Caveats { get; init; }
}
