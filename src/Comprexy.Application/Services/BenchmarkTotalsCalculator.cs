using Comprexy.Application.Abstractions;
using Comprexy.Application.Models.Benchmarking;
using Comprexy.Application.Services.Benchmarking;

namespace Comprexy.Application.Services;

public sealed class BenchmarkTotalsCalculator : IBenchmarkTotalsCalculator
{
    public ConversationTokenTotals FromSummary(
        Guid conversationId,
        int turnCount,
        long inputTokens,
        long outputTokens,
        long overheadTokens,
        long? wallClockMs,
        long? totalProxyDurationMs,
        long? totalUpstreamDurationMs,
        long? totalPrepareDurationMs) =>
        new()
        {
            ConversationId = conversationId,
            TurnCount = turnCount,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            OverheadTokens = overheadTokens,
            WallClockMs = wallClockMs,
            TotalProxyDurationMs = totalProxyDurationMs,
            TotalUpstreamDurationMs = totalUpstreamDurationMs,
            TotalPrepareDurationMs = totalPrepareDurationMs
        };

    public BenchmarkComparisonTotals Compare(
        ConversationTokenTotals baseline,
        ConversationTokenTotals compare)
    {
        var channels = BenchHeadlineChannelBuilder.Compare(
            new BenchHeadlineChannelBuilder.ChannelTotals(
                baseline.InputTokens,
                baseline.OutputTokens,
                baseline.OverheadTokens,
                baseline.TurnCount),
            new BenchHeadlineChannelBuilder.ChannelTotals(
                compare.InputTokens,
                compare.OutputTokens,
                compare.OverheadTokens,
                compare.TurnCount));

        return new BenchmarkComparisonTotals
        {
            Baseline = baseline,
            Compare = compare,
            Input = ToDelta(baseline.InputTokens, compare.InputTokens, channels.InputDeltaPercent),
            Output = ToDelta(baseline.OutputTokens, compare.OutputTokens, channels.OutputDeltaPercent),
            Overhead = ToDelta(baseline.OverheadTokens, compare.OverheadTokens, null),
            TurnCount = ToDelta(baseline.TurnCount, compare.TurnCount, null),
            WallClockMs = baseline.WallClockMs is { } bWall && compare.WallClockMs is { } cWall
                ? ToDelta(bWall, cWall, PercentDelta(bWall, cWall))
                : null,
            ProxyDurationMs = baseline.TotalProxyDurationMs is { } bProxy && compare.TotalProxyDurationMs is { } cProxy
                ? ToDelta(bProxy, cProxy, PercentDelta(bProxy, cProxy))
                : null,
            Caveats = channels.Caveats
        };
    }

    private static BenchmarkChannelDelta ToDelta(long baseline, long compare, double? percent) =>
        new()
        {
            Baseline = baseline,
            Compare = compare,
            Delta = compare - baseline,
            DeltaPercent = percent
        };

    private static double? PercentDelta(long baseline, long compare)
    {
        if (baseline == 0)
        {
            return compare == 0 ? 0d : null;
        }

        return Math.Round((double)(compare - baseline) / baseline, 6);
    }
}
