using Comprexy.Application.Services;
using Comprexy.ControlApi.Contracts.Metrics;
using Comprexy.Domain.Entities;
using Comprexy.Domain.Enums;

namespace Comprexy.ControlApi.Tests;

public class ConversationMetricsMapperTests
{
    [Fact]
    public void ToSummaryDto_ProviderActual_TotalVtEqualsSumOfProjectedTurnVt()
    {
        var conversationId = Guid.NewGuid();
        var now = DateTimeOffset.UnixEpoch;
        var summary = ConversationMetricsSummary.Create(conversationId, now);

        var turn1 = CreateTurn(
            conversationId,
            turnIndex: 1,
            raw: 80_000,
            prepared: 20_000,
            irFull: 60_000,
            actualPrompt: 30_000,
            completion: 1_000);
        var turn2 = CreateTurn(
            conversationId,
            turnIndex: 2,
            raw: 40_000,
            prepared: 10_000,
            irFull: 25_000,
            actualPrompt: 12_000,
            completion: 500);

        summary.ApplyTurn(turn1, now);
        summary.ApplyTurn(turn2, now);

        var estimateVt = summary.TotalVirtualToolsTokensSaved;
        var expectedVt =
            PromptTokenBasisProjector.Project(turn1, PromptTokenBasis.ProviderActual).VirtualToolsTokensSaved!.Value
            + PromptTokenBasisProjector.Project(turn2, PromptTokenBasis.ProviderActual).VirtualToolsTokensSaved!.Value;

        var dto = ConversationMetricsMapper.ToSummaryDto(
            summary,
            [turn1, turn2],
            PromptTokenBasis.ProviderActual);

        Assert.Equal(expectedVt, dto.TotalVirtualToolsTokensSaved);
        Assert.NotEqual(estimateVt, expectedVt);
        Assert.Equal(PromptTokenBasis.ProviderActual, dto.PromptTokenBasis);
    }

    [Fact]
    public void ToListItem_ProviderActual_TotalVtEqualsSumOfProjectedTurnVt()
    {
        var conversationId = Guid.NewGuid();
        var now = DateTimeOffset.UnixEpoch;
        var summary = ConversationMetricsSummary.Create(conversationId, now);
        var turn = CreateTurn(
            conversationId,
            turnIndex: 1,
            raw: 80_000,
            prepared: 20_000,
            irFull: 60_000,
            actualPrompt: 30_000,
            completion: 1_000);
        summary.ApplyTurn(turn, now);

        var expectedVt = PromptTokenBasisProjector
            .Project(turn, PromptTokenBasis.ProviderActual)
            .VirtualToolsTokensSaved!.Value;

        var item = ConversationMetricsMapper.ToListItem(
            summary,
            [turn],
            PromptTokenBasis.ProviderActual);

        Assert.Equal(expectedVt, item.TotalVirtualToolsTokensSaved);
        Assert.NotEqual(summary.TotalVirtualToolsTokensSaved, expectedVt);
    }

    [Fact]
    public void ToTurnDto_ExposesProjectedIrFullVtAndLegacyFlag()
    {
        var conversationId = Guid.NewGuid();
        var withIrFull = CreateTurn(
            conversationId,
            turnIndex: 1,
            raw: 80_000,
            prepared: 20_000,
            irFull: 60_000,
            actualPrompt: 30_000,
            completion: 1_000);
        var legacy = CreateTurn(
            conversationId,
            turnIndex: 2,
            raw: 50_000,
            prepared: 30_000,
            irFull: null,
            actualPrompt: 40_000,
            completion: 500);

        var projected = ConversationMetricsMapper.ToTurnDto(withIrFull, basis: PromptTokenBasis.ProviderActual);
        var legacyDto = ConversationMetricsMapper.ToTurnDto(legacy, basis: PromptTokenBasis.ProviderActual);

        Assert.Equal(90_000, projected.IrFullInputTokensEstimated);
        Assert.Equal(30_000, projected.VirtualToolsTokensSaved);
        Assert.False(projected.IsLegacyMixedAxis);
        Assert.Equal(60_000, projected.NetTokensSaved);
        Assert.Null(legacyDto.IrFullInputTokensEstimated);
        Assert.Null(legacyDto.VirtualToolsTokensSaved);
        Assert.True(legacyDto.IsLegacyMixedAxis);
    }

    private static ConversationTurnMetric CreateTurn(
        Guid conversationId,
        int turnIndex,
        int raw,
        int prepared,
        int? irFull,
        int? actualPrompt,
        int completion) =>
        ConversationTurnMetric.Create(
            conversationId,
            turnIndex,
            DateTimeOffset.UnixEpoch.AddMinutes(turnIndex),
            "test-model",
            rawInputTokensEstimated: raw,
            compressedInputTokensEstimated: prepared,
            actualPromptTokens: actualPrompt,
            actualCompletionTokens: completion,
            softBudgetExceeded: false,
            hardBudgetExceeded: false,
            trimTriggered: false,
            workingMemoryVersionUsed: 1,
            rawMessageCount: 10,
            sentMessageCount: 5,
            requestHash: $"r{turnIndex}",
            sentPayloadHash: $"s{turnIndex}",
            durationMs: 100,
            upstreamDurationMs: 70,
            prepareDurationMs: 20,
            createdAt: DateTimeOffset.UnixEpoch.AddMinutes(turnIndex),
            irFullInputTokensEstimated: irFull);
}
