using Comprexy.Application.Models.Benchmarking;
using Comprexy.Application.Models.Cost;

namespace Comprexy.Application.Services.Benchmarking;

/// <summary>
/// Maps catalog presentation rates into <see cref="BenchmarkCostRates"/>.
/// Uses input + output only; cached catalog columns are ignored.
/// </summary>
public static class ModelPricingCostRatesMapper
{
    public static BenchmarkCostRates ToBenchmarkCostRates(
        ModelPricingCatalogItem item,
        decimal developerUsdPerHour = 0m,
        decimal machineUsdPerHour = 0m)
    {
        ArgumentNullException.ThrowIfNull(item);
        return ToBenchmarkCostRates(
            item.InputUsdPer1M,
            item.OutputUsdPer1M,
            developerUsdPerHour,
            machineUsdPerHour);
    }

    public static BenchmarkCostRates ToBenchmarkCostRates(
        decimal inputUsdPer1M,
        decimal outputUsdPer1M,
        decimal developerUsdPerHour = 0m,
        decimal machineUsdPerHour = 0m)
    {
        var modelKind = inputUsdPer1M == 0m && outputUsdPer1M == 0m
            ? BenchmarkModelKind.Local
            : BenchmarkModelKind.Usd;

        return new BenchmarkCostRates
        {
            InputUsdPer1M = inputUsdPer1M,
            OutputUsdPer1M = outputUsdPer1M,
            CompressionInputUsdPer1M = 0m,
            CompressionOutputUsdPer1M = 0m,
            DeveloperUsdPerHour = developerUsdPerHour,
            MachineUsdPerHour = machineUsdPerHour,
            ModelKind = modelKind
        }.WithCompressionDefaultsFromMain();
    }
}
