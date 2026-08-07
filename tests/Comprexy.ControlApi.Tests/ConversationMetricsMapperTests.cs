using System.Text.Json;
using Comprexy.Application.Models.Telemetry;
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

    [Fact]
    public void ToTurnDto_SerializesPreparedCatalogSegmentsWithCamelCaseJsonNames()
    {
        var turn = CreateTurn(
            Guid.NewGuid(),
            turnIndex: 1,
            raw: 80_000,
            prepared: 20_000,
            irFull: 60_000,
            actualPrompt: 30_000,
            completion: 1_000);
        var breakdown = new ConversationTurnContextBreakdown
        {
            TurnIndex = 1,
            SystemPromptTokensEstimated = 100,
            WorkingMemoryTokensEstimated = 200,
            PreparedVirtualToolSchemaTokensEstimated = 300,
            PreparedClientToolSchemaTokensEstimated = 400,
            PreparedRulesTokensEstimated = 50,
            HistoryTokensEstimated = 950
        };

        var dto = ConversationMetricsMapper.ToTurnDto(turn, breakdown, PromptTokenBasis.Estimated);
        using var document = JsonDocument.Parse(
            JsonSerializer.Serialize(dto, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var root = document.RootElement;

        Assert.Equal(300, root.GetProperty("preparedVirtualToolSchemaTokensEstimated").GetInt32());
        Assert.Equal(400, root.GetProperty("preparedClientToolSchemaTokensEstimated").GetInt32());
        Assert.Equal(50, root.GetProperty("preparedRulesTokensEstimated").GetInt32());
        Assert.Equal(950, root.GetProperty("historyTokensEstimated").GetInt32());
        Assert.False(root.TryGetProperty("historyAndToolsTokensEstimated", out _));
        Assert.NotEqual(
            dto.VirtualToolsTokensSaved,
            dto.PreparedVirtualToolSchemaTokensEstimated);
    }

    [Fact]
    public void ToTurnDto_NullBreakdown_HistoryFallsBackToCompressedInput_CatalogZero()
    {
        var turn = CreateTurn(
            Guid.NewGuid(),
            turnIndex: 1,
            raw: 10_000,
            prepared: 4_000,
            irFull: 8_000,
            actualPrompt: 4_500,
            completion: 100);

        var dto = ConversationMetricsMapper.ToTurnDto(turn, breakdown: null, PromptTokenBasis.Estimated);

        Assert.Equal(0, dto.SystemPromptTokensEstimated);
        Assert.Equal(0, dto.WorkingMemoryTokensEstimated);
        Assert.Equal(0, dto.PreparedVirtualToolSchemaTokensEstimated);
        Assert.Equal(0, dto.PreparedClientToolSchemaTokensEstimated);
        Assert.Equal(0, dto.PreparedRulesTokensEstimated);
        Assert.Equal(4_000, dto.HistoryTokensEstimated);
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
