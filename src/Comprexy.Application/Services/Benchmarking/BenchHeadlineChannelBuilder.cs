namespace Comprexy.Application.Services.Benchmarking;

/// <summary>
/// Pure channel mapping for bench headlines and dashboard scoreboards.
/// Input, output, and overhead are kept separate; overhead is never folded into input via Actual totals.
/// </summary>
public static class BenchHeadlineChannelBuilder
{
    public sealed record ChannelTotals(
        long InputTokens,
        long OutputTokens,
        long OverheadTokens,
        int TurnCount)
    {
        public long TotalSentTokens => InputTokens + OutputTokens + OverheadTokens;
    }

    public sealed record ComparisonResult(
        ChannelTotals Baseline,
        ChannelTotals Compare,
        long InputDelta,
        long OutputDelta,
        long OverheadDelta,
        int TurnCountDelta,
        double? InputDeltaPercent,
        double? OutputDeltaPercent,
        IReadOnlyList<string> Caveats);

    public static ChannelTotals FromChannels(
        long inputTokens,
        long outputTokens,
        long overheadTokens,
        int turnCount) =>
        new(inputTokens, outputTokens, overheadTokens, turnCount);

    public static ComparisonResult Compare(ChannelTotals baseline, ChannelTotals compare)
    {
        var caveats = new List<string>();
        if (baseline.TurnCount != compare.TurnCount)
        {
            caveats.Add(
                $"turn counts differ ({baseline.TurnCount} vs {compare.TurnCount}); hop counts may not align across arms");
        }

        if (compare.OverheadTokens > 0)
        {
            caveats.Add(
                $"compare arm incurred {compare.OverheadTokens:N0} compression overhead tokens (shown separately, not in chart bars)");
        }

        return new ComparisonResult(
            baseline,
            compare,
            compare.InputTokens - baseline.InputTokens,
            compare.OutputTokens - baseline.OutputTokens,
            compare.OverheadTokens - baseline.OverheadTokens,
            compare.TurnCount - baseline.TurnCount,
            PercentDelta(baseline.InputTokens, compare.InputTokens),
            PercentDelta(baseline.OutputTokens, compare.OutputTokens),
            caveats);
    }

    public static long TreatmentCostIncludingOverhead(ChannelTotals treatment) =>
        treatment.TotalSentTokens;

    public static long TokensSavedVersusBaseline(ChannelTotals baseline, ChannelTotals treatment) =>
        baseline.TotalSentTokens - TreatmentCostIncludingOverhead(treatment);

    public static double TokenReductionRatio(ChannelTotals baseline, ChannelTotals treatment)
    {
        if (baseline.TotalSentTokens <= 0)
        {
            return 0d;
        }

        return Math.Round(
            (double)TokensSavedVersusBaseline(baseline, treatment) / baseline.TotalSentTokens,
            6);
    }

    private static double? PercentDelta(long baseline, long compare)
    {
        if (baseline == 0)
        {
            return compare == 0 ? 0d : null;
        }

        return Math.Round((double)(compare - baseline) / baseline, 6);
    }
}
