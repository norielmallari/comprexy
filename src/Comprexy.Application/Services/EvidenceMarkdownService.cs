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
            - Total SoftBudget baseline tokens estimated (IrFull + completion when IrFull present): {summary.TotalBaselineTokensEstimated.ToString("N0", CultureInfo.InvariantCulture)}
            - Total prepared/sent-equivalent tokens: {summary.TotalCompressedTokensEstimated.ToString("N0", CultureInfo.InvariantCulture)}
            - Total SoftBudget net tokens saved (IrFull − Prepared when IrFull present): {summary.TotalNetTokensSaved.ToString("N0", CultureInfo.InvariantCulture)}
            - Total virtual-tools / native-wire channel tokens (NativeRaw − IrFull; not tools-only; may be negative): {summary.TotalVirtualToolsTokensSaved.ToString("N0", CultureInfo.InvariantCulture)}
            - Weighted average SoftBudget token savings: {weightedPct}%
            - Final turn SoftBudget token savings: {finalPct}%
            - Final SoftBudget payload: {finalTurn.BaselineTotalTokensEstimated.ToString("N0", CultureInfo.InvariantCulture)} -> {finalTurn.CompressedTotalTokensEstimated.ToString("N0", CultureInfo.InvariantCulture)} tokens
            - Raw messages reduced: {finalTurn.RawMessageCount.ToString("N0", CultureInfo.InvariantCulture)} -> {finalTurn.SentMessageCount.ToString("N0", CultureInfo.InvariantCulture)}
            """;
    }
}
