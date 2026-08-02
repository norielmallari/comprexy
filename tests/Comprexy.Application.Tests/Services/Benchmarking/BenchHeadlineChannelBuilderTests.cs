using Comprexy.Application.Services.Benchmarking;

namespace Comprexy.Application.Tests.Services.Benchmarking;

public sealed class BenchHeadlineChannelBuilderTests
{
    [Fact]
    public void Compare_KeepsInputOutputAndOverheadChannelsSeparate()
    {
        var baseline = BenchHeadlineChannelBuilder.FromChannels(
            inputTokens: 1_000,
            outputTokens: 200,
            overheadTokens: 50,
            turnCount: 3);
        var compare = BenchHeadlineChannelBuilder.FromChannels(
            inputTokens: 800,
            outputTokens: 250,
            overheadTokens: 30,
            turnCount: 3);

        var result = BenchHeadlineChannelBuilder.Compare(baseline, compare);

        Assert.Equal(1_000, result.Baseline.InputTokens);
        Assert.Equal(200, result.Baseline.OutputTokens);
        Assert.Equal(50, result.Baseline.OverheadTokens);
        Assert.Equal(800, result.Compare.InputTokens);
        Assert.Equal(250, result.Compare.OutputTokens);
        Assert.Equal(30, result.Compare.OverheadTokens);
        Assert.Equal(-200, result.InputDelta);
        Assert.Equal(50, result.OutputDelta);
        Assert.Equal(-20, result.OverheadDelta);
    }

    [Fact]
    public void TotalSentTokens_CountsOverheadOnce_NotBlendedWithActualStyleTotals()
    {
        const long input = 1_000;
        const long output = 200;
        const long overhead = 100;
        var proper = BenchHeadlineChannelBuilder.FromChannels(input, output, overhead, turnCount: 2);

        // Anti-pattern: treating Actual (prompt+completion+overhead) as "input" then adding overhead again.
        var actualAsInput = input + output + overhead;
        var doubleCounted = actualAsInput + overhead;

        Assert.Equal(input + output + overhead, proper.TotalSentTokens);
        Assert.NotEqual(proper.TotalSentTokens, doubleCounted);
        Assert.Equal(1_300, proper.TotalSentTokens);
        Assert.Equal(1_400, doubleCounted);
    }

    [Fact]
    public void Compare_AddsTurnCountCaveat_WhenTurnCountsDiffer()
    {
        var baseline = BenchHeadlineChannelBuilder.FromChannels(100, 50, 0, turnCount: 4);
        var compare = BenchHeadlineChannelBuilder.FromChannels(90, 45, 0, turnCount: 6);

        var result = BenchHeadlineChannelBuilder.Compare(baseline, compare);

        Assert.Contains(result.Caveats, c => c.Contains("turn counts differ", StringComparison.Ordinal));
        Assert.Equal(2, result.TurnCountDelta);
    }

    [Fact]
    public void Compare_PreservesNegativeDeltasPerChannel()
    {
        var baseline = BenchHeadlineChannelBuilder.FromChannels(500, 300, 20, turnCount: 2);
        var compare = BenchHeadlineChannelBuilder.FromChannels(700, 150, 40, turnCount: 2);

        var result = BenchHeadlineChannelBuilder.Compare(baseline, compare);

        Assert.Equal(200, result.InputDelta);
        Assert.Equal(-150, result.OutputDelta);
        Assert.Equal(20, result.OverheadDelta);
        Assert.Equal(0.4, result.InputDeltaPercent);
        Assert.Equal(-0.5, result.OutputDeltaPercent);
    }

    [Fact]
    public void TokensSavedVersusBaseline_UsesSeparatedChannels_NotActualAsInput()
    {
        var baseline = BenchHeadlineChannelBuilder.FromChannels(1_000, 200, 50, turnCount: 2);
        var treatment = BenchHeadlineChannelBuilder.FromChannels(900, 180, 40, turnCount: 2);

        var saved = BenchHeadlineChannelBuilder.TokensSavedVersusBaseline(baseline, treatment);

        Assert.Equal(130, saved);
        Assert.NotEqual(
            baseline.TotalSentTokens - (treatment.InputTokens + treatment.OutputTokens + treatment.OverheadTokens + treatment.OverheadTokens),
            saved);
    }

    [Fact]
    public void Compare_AddsOverheadCaveat_WhenCompareArmHasOverhead()
    {
        var baseline = BenchHeadlineChannelBuilder.FromChannels(100, 50, 0, turnCount: 2);
        var compare = BenchHeadlineChannelBuilder.FromChannels(90, 45, 25, turnCount: 2);

        var result = BenchHeadlineChannelBuilder.Compare(baseline, compare);

        Assert.Contains(result.Caveats, c => c.Contains("compression overhead", StringComparison.OrdinalIgnoreCase));
    }
}
