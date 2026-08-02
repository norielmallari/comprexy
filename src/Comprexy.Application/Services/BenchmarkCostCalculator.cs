using Comprexy.Application.Abstractions;
using Comprexy.Application.Models.Benchmarking;

namespace Comprexy.Application.Services;

public sealed class BenchmarkCostCalculator : IBenchmarkCostCalculator
{
    private const string LocalDisclaimer =
        "Local cost-equivalent model: rates are illustrative, not billing. Proxy duration sums are diagnostic only.";

    private const string UsdDisclaimer =
        "USD estimates use operator-supplied rates; not provider invoices. Time-value uses wall-clock delta only.";

    public BenchmarkCostBreakdown ComputeTelemetryCost(
        ConversationTokenTotals totals,
        BenchmarkCostRates rates)
    {
        var normalized = rates.WithCompressionDefaultsFromMain();
        if (normalized.ModelKind == BenchmarkModelKind.Local)
        {
            return new BenchmarkCostBreakdown
            {
                ModelKind = BenchmarkModelKind.Local,
                Disclaimer = LocalDisclaimer
            };
        }

        var input = TokenCostUsd(totals.InputTokens, normalized.InputUsdPer1M);
        var output = TokenCostUsd(totals.OutputTokens, normalized.OutputUsdPer1M);
        var overhead = TokenCostUsd(
            totals.OverheadTokens,
            normalized.CompressionInputUsdPer1M + normalized.CompressionOutputUsdPer1M);
        var total = input + output + overhead;

        return new BenchmarkCostBreakdown
        {
            ModelKind = BenchmarkModelKind.Usd,
            CompareInputCostUsd = input,
            CompareOutputCostUsd = output,
            CompareOverheadCostUsd = overhead,
            CompareTotalCostUsd = total,
            Disclaimer = UsdDisclaimer
        };
    }

    public BenchmarkCostBreakdown ComputeComparisonCost(
        BenchmarkComparisonTotals totals,
        BenchmarkCostRates rates)
    {
        var normalized = rates.WithCompressionDefaultsFromMain();
        if (normalized.ModelKind == BenchmarkModelKind.Local)
        {
            return new BenchmarkCostBreakdown
            {
                ModelKind = BenchmarkModelKind.Local,
                Disclaimer = LocalDisclaimer
            };
        }

        var baselineInput = TokenCostUsd(totals.Baseline.InputTokens, normalized.InputUsdPer1M);
        var baselineOutput = TokenCostUsd(totals.Baseline.OutputTokens, normalized.OutputUsdPer1M);
        var baselineOverhead = TokenCostUsd(
            totals.Baseline.OverheadTokens,
            normalized.CompressionInputUsdPer1M + normalized.CompressionOutputUsdPer1M);

        var compareInput = TokenCostUsd(totals.Compare.InputTokens, normalized.InputUsdPer1M);
        var compareOutput = TokenCostUsd(totals.Compare.OutputTokens, normalized.OutputUsdPer1M);
        var compareOverhead = TokenCostUsd(
            totals.Compare.OverheadTokens,
            normalized.CompressionInputUsdPer1M + normalized.CompressionOutputUsdPer1M);

        var baselineTotal = baselineInput + baselineOutput + baselineOverhead;
        var compareTotal = compareInput + compareOutput + compareOverhead;

        decimal? timeValue = null;
        if (totals.WallClockMs is { } wallDelta)
        {
            var baselineMs = totals.Baseline.WallClockMs ?? 0;
            var compareMs = totals.Compare.WallClockMs ?? 0;
            var deltaHours = (compareMs - baselineMs) / 3_600_000m;
            timeValue = deltaHours * (normalized.DeveloperUsdPerHour + normalized.MachineUsdPerHour);
        }

        return new BenchmarkCostBreakdown
        {
            ModelKind = BenchmarkModelKind.Usd,
            BaselineInputCostUsd = baselineInput,
            BaselineOutputCostUsd = baselineOutput,
            BaselineOverheadCostUsd = baselineOverhead,
            CompareInputCostUsd = compareInput,
            CompareOutputCostUsd = compareOutput,
            CompareOverheadCostUsd = compareOverhead,
            BaselineTotalCostUsd = baselineTotal,
            CompareTotalCostUsd = compareTotal,
            CostDeltaUsd = compareTotal - baselineTotal,
            TimeValueDeltaUsd = timeValue,
            Disclaimer = UsdDisclaimer
        };
    }

    private static decimal TokenCostUsd(long tokens, decimal usdPer1M) =>
        tokens / 1_000_000m * usdPer1M;
}
