namespace Comprexy.Application.Models.Benchmarking;

public sealed record BenchmarkCostBreakdown
{
    public required BenchmarkModelKind ModelKind { get; init; }

    public decimal? BaselineInputCostUsd { get; init; }

    public decimal? BaselineOutputCostUsd { get; init; }

    public decimal? BaselineOverheadCostUsd { get; init; }

    public decimal? CompareInputCostUsd { get; init; }

    public decimal? CompareOutputCostUsd { get; init; }

    public decimal? CompareOverheadCostUsd { get; init; }

    public decimal? BaselineTotalCostUsd { get; init; }

    public decimal? CompareTotalCostUsd { get; init; }

    public decimal? CostDeltaUsd { get; init; }

    /// <summary>Time-value from wall-clock delta only; null when wall clock is missing.</summary>
    public decimal? TimeValueDeltaUsd { get; init; }

    public required string Disclaimer { get; init; }
}
