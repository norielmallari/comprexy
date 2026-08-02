namespace Comprexy.Application.Models.Benchmarking;

public sealed record BenchmarkChannelDelta
{
    public required long Baseline { get; init; }

    public required long Compare { get; init; }

    public required long Delta { get; init; }

    public double? DeltaPercent { get; init; }
}
