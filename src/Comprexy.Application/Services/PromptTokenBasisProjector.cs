using Comprexy.Application.Models.Telemetry;
using Comprexy.Domain.Entities;
using Comprexy.Domain.Enums;

namespace Comprexy.Application.Services;

/// <summary>
/// Read-side projection of turn token proof onto <see cref="PromptTokenBasis"/>.
/// Does not mutate persisted rows. SoftBudget arms use IrFull vs Prepared when IrFull is present;
/// Virtual Tools uses NativeRaw vs IrFull.
/// </summary>
public static class PromptTokenBasisProjector
{
    public readonly record struct ProjectedTurn(
        int RawInputTokens,
        int CompressedInputTokens,
        int BaselineTotalTokens,
        int CompressedTotalTokens,
        int NetTokensSaved,
        double NetTokenSavingsRatio,
        int ActualCompletionTokens,
        int? ActualPromptTokens,
        int CompressedInputTokensEstimated,
        int? IrFullInputTokens,
        int? VirtualToolsTokensSaved);

    public static ProjectedTurn Project(ConversationTurnMetric turn, PromptTokenBasis basis) =>
        Project(
            turn.RawInputTokensEstimated,
            turn.CompressedInputTokensEstimated,
            turn.ActualPromptTokens,
            turn.ActualCompletionTokens,
            turn.BaselineTotalTokensEstimated,
            turn.CompressedTotalTokensEstimated,
            turn.NetTokensSaved,
            turn.NetTokenSavingsRatio,
            basis,
            turn.IrFullInputTokensEstimated,
            turn.VirtualToolsTokensSaved);

    public static ProjectedTurn Project(ConversationTurnProjection turn, PromptTokenBasis basis) =>
        Project(
            turn.RawInputTokensEstimated,
            turn.CompressedInputTokensEstimated,
            turn.ActualPromptTokens,
            turn.ActualCompletionTokens,
            turn.BaselineTotalTokensEstimated,
            turn.CompressedTotalTokensEstimated,
            turn.NetTokensSaved,
            turn.NetTokenSavingsRatio,
            basis,
            turn.IrFullInputTokensEstimated,
            turn.VirtualToolsTokensSaved);

    public static ProjectedTurn Project(
        int rawInputEstimated,
        int compressedInputEstimated,
        int? actualPromptTokens,
        int actualCompletionTokens,
        int baselineTotalEstimated,
        int compressedTotalEstimated,
        int netTokensSaved,
        double netTokenSavingsRatio,
        PromptTokenBasis basis,
        int? irFullInputEstimated = null,
        int? virtualToolsTokensSaved = null)
    {
        if (basis != PromptTokenBasis.ProviderActual
            || actualPromptTokens is not int actual
            || actual <= 0)
        {
            return new ProjectedTurn(
                rawInputEstimated,
                compressedInputEstimated,
                baselineTotalEstimated,
                compressedTotalEstimated,
                netTokensSaved,
                netTokenSavingsRatio,
                actualCompletionTokens,
                actualPromptTokens,
                compressedInputEstimated,
                irFullInputEstimated,
                virtualToolsTokensSaved);
        }

        var compressedInput = actual;
        var rawInput = compressedInputEstimated > 0
            ? (int)Math.Round(rawInputEstimated * ((double)actual / compressedInputEstimated))
            : rawInputEstimated;

        int? scaledIrFull = null;
        int? scaledVt = null;
        int softBudgetBaselineInput;
        if (irFullInputEstimated is int irFull)
        {
            if (compressedInputEstimated > 0)
            {
                scaledIrFull = (int)Math.Round(irFull * ((double)actual / compressedInputEstimated));
            }
            else
            {
                scaledIrFull = irFull;
            }

            softBudgetBaselineInput = scaledIrFull.Value;
            scaledVt = rawInput - scaledIrFull.Value;
        }
        else
        {
            softBudgetBaselineInput = rawInput;
        }

        var baselineTotal = softBudgetBaselineInput + actualCompletionTokens;
        var compressedTotal = compressedInput + actualCompletionTokens;
        var netSaved = baselineTotal - compressedTotal;
        var ratio = baselineTotal > 0
            ? Math.Round((double)netSaved / baselineTotal, 6)
            : 0d;

        return new ProjectedTurn(
            rawInput,
            compressedInput,
            baselineTotal,
            compressedTotal,
            netSaved,
            ratio,
            actualCompletionTokens,
            actual,
            compressedInputEstimated,
            scaledIrFull,
            scaledVt);
    }

    /// <summary>
    /// Returns a projection with token proof fields rewritten for <paramref name="basis"/>.
    /// Identity when basis is Estimated or when provider usage is missing.
    /// </summary>
    public static ConversationTurnProjection ApplyBasis(
        ConversationTurnProjection turn,
        PromptTokenBasis basis)
    {
        if (basis == PromptTokenBasis.Estimated)
        {
            return turn;
        }

        var p = Project(turn, basis);
        if (p.CompressedInputTokens == turn.CompressedInputTokensEstimated
            && p.BaselineTotalTokens == turn.BaselineTotalTokensEstimated
            && p.IrFullInputTokens == turn.IrFullInputTokensEstimated
            && p.VirtualToolsTokensSaved == turn.VirtualToolsTokensSaved)
        {
            return turn;
        }

        return new ConversationTurnProjection
        {
            TurnIndex = turn.TurnIndex,
            RequestStartedAt = turn.RequestStartedAt,
            Model = turn.Model,
            RawInputTokensEstimated = p.RawInputTokens,
            IrFullInputTokensEstimated = p.IrFullInputTokens,
            CompressedInputTokensEstimated = p.CompressedInputTokens,
            ActualPromptTokens = turn.ActualPromptTokens,
            ActualCompletionTokens = turn.ActualCompletionTokens,
            BaselineTotalTokensEstimated = p.BaselineTotalTokens,
            CompressedTotalTokensEstimated = p.CompressedTotalTokens,
            NetTokensSaved = p.NetTokensSaved,
            NetTokenSavingsRatio = p.NetTokenSavingsRatio,
            VirtualToolsTokensSaved = p.VirtualToolsTokensSaved,
            SoftBudgetExceeded = turn.SoftBudgetExceeded,
            HardBudgetExceeded = turn.HardBudgetExceeded,
            TrimTriggered = turn.TrimTriggered,
            WorkingMemoryVersionUsed = turn.WorkingMemoryVersionUsed,
            RawMessageCount = turn.RawMessageCount,
            SentMessageCount = turn.SentMessageCount,
            DurationMs = turn.DurationMs,
            UpstreamDurationMs = turn.UpstreamDurationMs,
            PrepareDurationMs = turn.PrepareDurationMs,
            CreatedAt = turn.CreatedAt
        };
    }

    public static IReadOnlyList<ConversationTurnProjection> ApplyBasis(
        IReadOnlyList<ConversationTurnProjection> turns,
        PromptTokenBasis basis) =>
        basis == PromptTokenBasis.Estimated
            ? turns
            : turns.Select(t => ApplyBasis(t, basis)).ToList();
}
