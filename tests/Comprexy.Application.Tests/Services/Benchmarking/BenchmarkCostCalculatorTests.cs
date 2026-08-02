using Comprexy.Application.Models.Benchmarking;
using Comprexy.Application.Services;

namespace Comprexy.Application.Tests.Services.Benchmarking;

public sealed class BenchmarkCostCalculatorTests
{
    private readonly BenchmarkCostCalculator _calculator = new();

    private static readonly Guid BaselineId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid CompareId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public void ComputeTelemetryCost_LocalModel_ReturnsDisclaimerWithoutUsdAmounts()
    {
        var totals = SingleSideTotals(input: 1_000_000, output: 500_000, overhead: 10_000);
        var rates = BenchmarkCostRates.LocalDefaults();

        var cost = _calculator.ComputeTelemetryCost(totals, rates);

        Assert.Equal(BenchmarkModelKind.Local, cost.ModelKind);
        Assert.Null(cost.CompareTotalCostUsd);
        Assert.Contains("illustrative", cost.Disclaimer, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ComputeTelemetryCost_UsdModel_ComputesSingleSidePerChannelCosts()
    {
        var totals = SingleSideTotals(input: 1_000_000, output: 500_000, overhead: 100_000);
        var rates = UsdRates(inputPer1M: 3m, outputPer1M: 15m);

        var cost = _calculator.ComputeTelemetryCost(totals, rates);

        Assert.Equal(BenchmarkModelKind.Usd, cost.ModelKind);
        Assert.Equal(3m, cost.CompareInputCostUsd);
        Assert.Equal(7.5m, cost.CompareOutputCostUsd);
        Assert.Equal(1.8m, cost.CompareOverheadCostUsd);
        Assert.Equal(12.3m, cost.CompareTotalCostUsd);
        Assert.Null(cost.BaselineTotalCostUsd);
    }

    [Fact]
    public void ComputeComparisonCost_UsdModel_UsesCompressionRatesDefaultedFromMain()
    {
        var totals = ComparisonTotals(
            baselineInput: 1_000_000,
            baselineOutput: 0,
            baselineOverhead: 200_000,
            compareInput: 0,
            compareOutput: 1_000_000,
            compareOverhead: 0);
        var rates = new BenchmarkCostRates
        {
            InputUsdPer1M = 2m,
            OutputUsdPer1M = 4m,
            CompressionInputUsdPer1M = 0m,
            CompressionOutputUsdPer1M = 0m,
            ModelKind = BenchmarkModelKind.Usd
        };

        var cost = _calculator.ComputeComparisonCost(totals, rates);

        Assert.Equal(2m, cost.BaselineInputCostUsd);
        Assert.Equal(1.2m, cost.BaselineOverheadCostUsd);
        Assert.Equal(4m, cost.CompareOutputCostUsd);
        Assert.Equal(3.2m, cost.BaselineTotalCostUsd);
        Assert.Equal(4m, cost.CompareTotalCostUsd);
        Assert.Equal(0.8m, cost.CostDeltaUsd);
    }

    [Fact]
    public void ComputeComparisonCost_TimeValueUsesWallClockOnly_NotProxyDuration()
    {
        var totals = ComparisonTotals(
            baselineInput: 0,
            baselineOutput: 0,
            baselineOverhead: 0,
            compareInput: 0,
            compareOutput: 0,
            compareOverhead: 0,
            baselineWallClockMs: 3_600_000,
            compareWallClockMs: 7_200_000,
            baselineProxyMs: 100_000,
            compareProxyMs: 50_000);
        var rates = new BenchmarkCostRates
        {
            InputUsdPer1M = 1m,
            OutputUsdPer1M = 1m,
            DeveloperUsdPerHour = 100m,
            MachineUsdPerHour = 50m,
            ModelKind = BenchmarkModelKind.Usd
        };

        var cost = _calculator.ComputeComparisonCost(totals, rates);

        Assert.Equal(150m, cost.TimeValueDeltaUsd);
    }

    [Fact]
    public void ComputeComparisonCost_TimeValueNull_WhenWallClockMissing()
    {
        var totals = ComparisonTotals(
            baselineInput: 100,
            baselineOutput: 100,
            baselineOverhead: 0,
            compareInput: 100,
            compareOutput: 100,
            compareOverhead: 0,
            baselineProxyMs: 1_000,
            compareProxyMs: 2_000);
        var rates = UsdRates(1m, 1m) with
        {
            DeveloperUsdPerHour = 200m,
            MachineUsdPerHour = 0m
        };

        var cost = _calculator.ComputeComparisonCost(totals, rates);

        Assert.Null(cost.TimeValueDeltaUsd);
    }

    [Fact]
    public void ComputeComparisonCost_LocalModel_ReturnsDisclaimerOnly()
    {
        var totals = ComparisonTotals(100, 50, 0, 90, 45, 0);
        var rates = BenchmarkCostRates.LocalDefaults();

        var cost = _calculator.ComputeComparisonCost(totals, rates);

        Assert.Equal(BenchmarkModelKind.Local, cost.ModelKind);
        Assert.Null(cost.CostDeltaUsd);
        Assert.Contains("diagnostic", cost.Disclaimer, StringComparison.OrdinalIgnoreCase);
    }

    private static BenchmarkCostRates UsdRates(decimal inputPer1M, decimal outputPer1M) =>
        new()
        {
            InputUsdPer1M = inputPer1M,
            OutputUsdPer1M = outputPer1M,
            ModelKind = BenchmarkModelKind.Usd
        };

    private static ConversationTokenTotals SingleSideTotals(long input, long output, long overhead) =>
        new()
        {
            ConversationId = BaselineId,
            TurnCount = 1,
            InputTokens = input,
            OutputTokens = output,
            OverheadTokens = overhead
        };

    private static BenchmarkComparisonTotals ComparisonTotals(
        long baselineInput,
        long baselineOutput,
        long baselineOverhead,
        long compareInput,
        long compareOutput,
        long compareOverhead,
        long? baselineWallClockMs = null,
        long? compareWallClockMs = null,
        long? baselineProxyMs = null,
        long? compareProxyMs = null)
    {
        var calculator = new BenchmarkTotalsCalculator();
        var baseline = new ConversationTokenTotals
        {
            ConversationId = BaselineId,
            TurnCount = 2,
            InputTokens = baselineInput,
            OutputTokens = baselineOutput,
            OverheadTokens = baselineOverhead,
            WallClockMs = baselineWallClockMs,
            TotalProxyDurationMs = baselineProxyMs
        };
        var compare = new ConversationTokenTotals
        {
            ConversationId = CompareId,
            TurnCount = 2,
            InputTokens = compareInput,
            OutputTokens = compareOutput,
            OverheadTokens = compareOverhead,
            WallClockMs = compareWallClockMs,
            TotalProxyDurationMs = compareProxyMs
        };
        return calculator.Compare(baseline, compare);
    }
}
