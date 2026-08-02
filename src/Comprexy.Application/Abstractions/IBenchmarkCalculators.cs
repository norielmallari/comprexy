using Comprexy.Application.Models.Benchmarking;

namespace Comprexy.Application.Abstractions;

public interface IBenchmarkTotalsCalculator
{
    ConversationTokenTotals FromSummary(
        Guid conversationId,
        int turnCount,
        long inputTokens,
        long outputTokens,
        long overheadTokens,
        long? wallClockMs,
        long? totalProxyDurationMs,
        long? totalUpstreamDurationMs,
        long? totalPrepareDurationMs);

    BenchmarkComparisonTotals Compare(
        ConversationTokenTotals baseline,
        ConversationTokenTotals compare);
}

public interface IBenchmarkCostCalculator
{
    BenchmarkCostBreakdown ComputeComparisonCost(
        BenchmarkComparisonTotals totals,
        BenchmarkCostRates rates);

    BenchmarkCostBreakdown ComputeTelemetryCost(
        ConversationTokenTotals totals,
        BenchmarkCostRates rates);
}
