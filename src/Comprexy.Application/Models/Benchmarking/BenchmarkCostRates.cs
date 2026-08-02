namespace Comprexy.Application.Models.Benchmarking;

/// <summary>
/// Operator-chosen cost assumptions stamped into bench manifests and used for presentation math.
/// </summary>
public sealed record BenchmarkCostRates
{
    public decimal InputUsdPer1M { get; init; }

    public decimal OutputUsdPer1M { get; init; }

    public decimal CompressionInputUsdPer1M { get; init; }

    public decimal CompressionOutputUsdPer1M { get; init; }

    public decimal DeveloperUsdPerHour { get; init; }

    public decimal MachineUsdPerHour { get; init; }

    public BenchmarkModelKind ModelKind { get; init; } = BenchmarkModelKind.Local;

    public BenchmarkCostRates WithCompressionDefaultsFromMain()
    {
        return this with
        {
            CompressionInputUsdPer1M = CompressionInputUsdPer1M > 0
                ? CompressionInputUsdPer1M
                : InputUsdPer1M,
            CompressionOutputUsdPer1M = CompressionOutputUsdPer1M > 0
                ? CompressionOutputUsdPer1M
                : OutputUsdPer1M
        };
    }

    public static BenchmarkCostRates LocalDefaults() => new()
    {
        InputUsdPer1M = 0m,
        OutputUsdPer1M = 0m,
        CompressionInputUsdPer1M = 0m,
        CompressionOutputUsdPer1M = 0m,
        DeveloperUsdPerHour = 0m,
        MachineUsdPerHour = 0m,
        ModelKind = BenchmarkModelKind.Local
    };
}
