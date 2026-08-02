using Comprexy.Application.Models.Benchmarking;
using Comprexy.Application.Services;

namespace Comprexy.Application.Tests.Services.Benchmarking;

public sealed class BenchmarkTotalsCalculatorTests
{
    private readonly BenchmarkTotalsCalculator _calculator = new();

    private static readonly Guid BaselineId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid CompareId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void Compare_ComputesPerChannelDeltasIndependently()
    {
        var baseline = Totals(BaselineId, input: 1_000, output: 200, overhead: 50, turns: 3, wallClockMs: 10_000);
        var compare = Totals(CompareId, input: 800, output: 250, overhead: 30, turns: 3, wallClockMs: 9_000);

        var result = _calculator.Compare(baseline, compare);

        Assert.Equal(-200, result.Input.Delta);
        Assert.Equal(50, result.Output.Delta);
        Assert.Equal(-20, result.Overhead.Delta);
        Assert.Equal(0, result.TurnCount.Delta);
        Assert.Equal(-0.2, result.Input.DeltaPercent);
        Assert.Equal(0.25, result.Output.DeltaPercent);
        Assert.Null(result.Overhead.DeltaPercent);
    }

    [Fact]
    public void Compare_PreservesNegativeDeltas()
    {
        var baseline = Totals(BaselineId, input: 400, output: 300, overhead: 10, turns: 2);
        var compare = Totals(CompareId, input: 500, output: 100, overhead: 25, turns: 2);

        var result = _calculator.Compare(baseline, compare);

        Assert.Equal(100, result.Input.Delta);
        Assert.Equal(-200, result.Output.Delta);
        Assert.Equal(15, result.Overhead.Delta);
    }

    [Fact]
    public void Compare_IncludesTurnCountCaveat_WhenHopCountsDiffer()
    {
        var baseline = Totals(BaselineId, input: 100, output: 50, overhead: 0, turns: 4);
        var compare = Totals(CompareId, input: 90, output: 45, overhead: 0, turns: 6);

        var result = _calculator.Compare(baseline, compare);

        Assert.Contains(result.Caveats, c => c.Contains("turn counts differ", StringComparison.Ordinal));
        Assert.Equal(2, result.TurnCount.Delta);
    }

    [Fact]
    public void Compare_ComputesWallClockDelta_WhenBothSidesPresent()
    {
        var baseline = Totals(BaselineId, input: 100, output: 50, overhead: 0, turns: 2, wallClockMs: 3_600_000);
        var compare = Totals(CompareId, input: 100, output: 50, overhead: 0, turns: 2, wallClockMs: 1_800_000);

        var result = _calculator.Compare(baseline, compare);

        Assert.NotNull(result.WallClockMs);
        Assert.Equal(-1_800_000, result.WallClockMs.Delta);
        Assert.Equal(-0.5, result.WallClockMs.DeltaPercent);
    }

    [Fact]
    public void FromSummary_MapsSeparatedIoFields()
    {
        var totals = _calculator.FromSummary(
            BaselineId,
            turnCount: 2,
            inputTokens: 1_200,
            outputTokens: 300,
            overheadTokens: 75,
            wallClockMs: null,
            totalProxyDurationMs: 5_000,
            totalUpstreamDurationMs: 4_000,
            totalPrepareDurationMs: 1_000);

        Assert.Equal(1_200, totals.InputTokens);
        Assert.Equal(300, totals.OutputTokens);
        Assert.Equal(75, totals.OverheadTokens);
        Assert.Equal(1_575, totals.TotalSentTokens);
        Assert.Equal(5_000, totals.TotalProxyDurationMs);
    }

    private static ConversationTokenTotals Totals(
        Guid conversationId,
        long input,
        long output,
        long overhead,
        int turns,
        long? wallClockMs = null,
        long? proxyMs = null) =>
        new()
        {
            ConversationId = conversationId,
            TurnCount = turns,
            InputTokens = input,
            OutputTokens = output,
            OverheadTokens = overhead,
            WallClockMs = wallClockMs,
            TotalProxyDurationMs = proxyMs
        };
}
