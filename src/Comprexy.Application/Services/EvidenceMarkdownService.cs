using System.Globalization;
using Comprexy.Application.Abstractions;
using Comprexy.Application.Models.Telemetry;

namespace Comprexy.Application.Services;

public sealed class EvidenceMarkdownService : IEvidenceMarkdownService
{
    public string Build(ConversationSummaryDto summary, FinalTurnSnapshotDto finalTurn)
    {
        var weightedPct = (summary.WeightedSavingsRatio * 100d).ToString("0.00", CultureInfo.InvariantCulture);
        var finalPct = (finalTurn.NetTokenSavingsRatio * 100d).ToString("0.00", CultureInfo.InvariantCulture);

        return
            $"""
            ## Validation Metrics

            - Total turns analyzed: {summary.TurnCount.ToString("N0", CultureInfo.InvariantCulture)}
            - Total baseline tokens estimated: {summary.TotalBaselineTokensEstimated.ToString("N0", CultureInfo.InvariantCulture)}
            - Total compressed/sent-equivalent tokens: {summary.TotalCompressedTokensEstimated.ToString("N0", CultureInfo.InvariantCulture)}
            - Total net tokens saved: {summary.TotalNetTokensSaved.ToString("N0", CultureInfo.InvariantCulture)}
            - Weighted average token savings: {weightedPct}%
            - Final turn token savings: {finalPct}%
            - Final payload reduction: {finalTurn.BaselineTotalTokensEstimated.ToString("N0", CultureInfo.InvariantCulture)} -> {finalTurn.CompressedTotalTokensEstimated.ToString("N0", CultureInfo.InvariantCulture)} tokens
            - Raw messages reduced: {finalTurn.RawMessageCount.ToString("N0", CultureInfo.InvariantCulture)} -> {finalTurn.SentMessageCount.ToString("N0", CultureInfo.InvariantCulture)}
            """;
    }
}
