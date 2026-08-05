using Comprexy.Application.Models.Telemetry;
using Comprexy.Application.Services;
using Comprexy.Domain.Entities;
using Comprexy.Domain.Enums;

namespace Comprexy.Application.Tests.Services;

public class ConversationTurnMetricTests
{
    [Fact]
    public void Create_UsesCompressedEstimateForSavings_IgnoringActualPromptMismatch()
    {
        var turn = ConversationTurnMetric.Create(
            Guid.NewGuid(),
            turnIndex: 1,
            requestStartedAt: DateTimeOffset.UtcNow,
            model: "test-model",
            rawInputTokensEstimated: 80_000,
            compressedInputTokensEstimated: 18_000,
            // Provider usage higher than tiktoken estimate must not invent a savings loss.
            actualPromptTokens: 32_000,
            actualCompletionTokens: 2_000,
            softBudgetExceeded: true,
            hardBudgetExceeded: false,
            trimTriggered: false,
            workingMemoryVersionUsed: 3,
            rawMessageCount: 40,
            sentMessageCount: 12,
            requestHash: "abc",
            sentPayloadHash: "def",
            durationMs: null,
            upstreamDurationMs: null,
            prepareDurationMs: null,
            createdAt: DateTimeOffset.UtcNow);

        Assert.Equal(32_000, turn.ActualPromptTokens);
        Assert.Equal(82_000, turn.BaselineTotalTokensEstimated);
        Assert.Equal(20_000, turn.CompressedTotalTokensEstimated);
        Assert.Equal(62_000, turn.NetTokensSaved);
        Assert.Equal(0.756098, turn.NetTokenSavingsRatio, precision: 6);
        Assert.Null(turn.IrFullInputTokensEstimated);
        Assert.Null(turn.VirtualToolsTokensSaved);
    }

    [Fact]
    public void Create_IrFullEqualsPrepared_SoftBudgetNetZero_VtIsNativeRawMinusIrFull()
    {
        var turn = CreateTurn(
            raw: 50_000,
            prepared: 40_000,
            irFull: 40_000,
            actualPrompt: 55_000,
            completion: 1_000);

        Assert.Equal(0, turn.NetTokensSaved);
        Assert.Equal(41_000, turn.BaselineTotalTokensEstimated);
        Assert.Equal(41_000, turn.CompressedTotalTokensEstimated);
        Assert.Equal(10_000, turn.VirtualToolsTokensSaved);
        Assert.Equal(40_000, turn.IrFullInputTokensEstimated);
    }

    [Fact]
    public void Create_IrFullMuchGreaterThanPrepared_SoftBudgetNetPositive()
    {
        var turn = CreateTurn(
            raw: 80_000,
            prepared: 20_000,
            irFull: 60_000,
            actualPrompt: 90_000,
            completion: 500);

        Assert.Equal(40_000, turn.NetTokensSaved);
        Assert.Equal(60_500, turn.BaselineTotalTokensEstimated);
        Assert.Equal(20_500, turn.CompressedTotalTokensEstimated);
        Assert.Equal(20_000, turn.VirtualToolsTokensSaved);
    }

    [Fact]
    public void Create_NullIrFull_KeepsLegacyRawMinusPrepared_AndNullVt()
    {
        var turn = CreateTurn(
            raw: 10_000,
            prepared: 4_000,
            irFull: null,
            actualPrompt: null,
            completion: 500);

        Assert.Null(turn.IrFullInputTokensEstimated);
        Assert.Null(turn.VirtualToolsTokensSaved);
        Assert.Equal(6_000, turn.NetTokensSaved);
        Assert.Equal(10_500, turn.BaselineTotalTokensEstimated);
    }

    [Fact]
    public void Create_WithIrFull_IgnoresActualPromptTokensInPersistedSoftBudgetNet()
    {
        var turn = CreateTurn(
            raw: 80_000,
            prepared: 18_000,
            irFull: 50_000,
            actualPrompt: 99_999,
            completion: 2_000);

        Assert.Equal(99_999, turn.ActualPromptTokens);
        Assert.Equal(32_000, turn.NetTokensSaved);
        Assert.Equal(52_000, turn.BaselineTotalTokensEstimated);
        Assert.Equal(20_000, turn.CompressedTotalTokensEstimated);
        Assert.Equal(30_000, turn.VirtualToolsTokensSaved);
    }

    private static ConversationTurnMetric CreateTurn(
        int raw,
        int prepared,
        int? irFull,
        int? actualPrompt,
        int completion) =>
        ConversationTurnMetric.Create(
            Guid.NewGuid(),
            turnIndex: 1,
            requestStartedAt: DateTimeOffset.UtcNow,
            model: "test-model",
            rawInputTokensEstimated: raw,
            compressedInputTokensEstimated: prepared,
            actualPromptTokens: actualPrompt,
            actualCompletionTokens: completion,
            softBudgetExceeded: false,
            hardBudgetExceeded: false,
            trimTriggered: false,
            workingMemoryVersionUsed: null,
            rawMessageCount: 5,
            sentMessageCount: 5,
            requestHash: "r",
            sentPayloadHash: "s",
            durationMs: null,
            upstreamDurationMs: null,
            prepareDurationMs: null,
            createdAt: DateTimeOffset.UtcNow,
            irFullInputTokensEstimated: irFull);

    [Fact]
    public void Create_Turn1ToolSchemaSavings_RemainPositiveWhenActualPromptExceedsRawEstimate()
    {
        // Mirrors findings-2026-07-27-01 turn 1: ToolSchema reduced estimate, provider usage did not.
        var turn = ConversationTurnMetric.Create(
            Guid.NewGuid(),
            turnIndex: 1,
            requestStartedAt: DateTimeOffset.UtcNow,
            model: "Qwen-35B",
            rawInputTokensEstimated: 22_370,
            compressedInputTokensEstimated: 20_462,
            actualPromptTokens: 32_566,
            actualCompletionTokens: 200,
            softBudgetExceeded: false,
            hardBudgetExceeded: false,
            trimTriggered: false,
            workingMemoryVersionUsed: null,
            rawMessageCount: 3,
            sentMessageCount: 5,
            requestHash: "r",
            sentPayloadHash: "s",
            durationMs: null,
            upstreamDurationMs: null,
            prepareDurationMs: null,
            createdAt: DateTimeOffset.UtcNow);

        Assert.Equal(1_908, turn.NetTokensSaved);
        Assert.Equal(22_570, turn.BaselineTotalTokensEstimated);
        Assert.Equal(20_662, turn.CompressedTotalTokensEstimated);
    }

    [Fact]
    public void Create_FallsBackToCompressedEstimate_WhenUsageMissing()
    {
        var turn = ConversationTurnMetric.Create(
            Guid.NewGuid(),
            turnIndex: 1,
            requestStartedAt: DateTimeOffset.UtcNow,
            model: "test-model",
            rawInputTokensEstimated: 10_000,
            compressedInputTokensEstimated: 4_000,
            actualPromptTokens: null,
            actualCompletionTokens: 500,
            softBudgetExceeded: false,
            hardBudgetExceeded: false,
            trimTriggered: false,
            workingMemoryVersionUsed: null,
            rawMessageCount: 5,
            sentMessageCount: 5,
            requestHash: "a",
            sentPayloadHash: "b",
            durationMs: null,
            upstreamDurationMs: null,
            prepareDurationMs: null,
            createdAt: DateTimeOffset.UtcNow);

        Assert.Equal(10_500, turn.BaselineTotalTokensEstimated);
        Assert.Equal(4_500, turn.CompressedTotalTokensEstimated);
        Assert.Equal(6_000, turn.NetTokensSaved);
    }
}

public class ConversationMetricsSummaryTests
{
    [Fact]
    public void ApplyTurnAndCompressionOverhead_MatchesRecomputedTotals()
    {
        var conversationId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var summary = ConversationMetricsSummary.Create(conversationId, now);

        var turn1 = ConversationTurnMetric.Create(
            conversationId,
            1,
            now,
            "m",
            rawInputTokensEstimated: 80_000,
            compressedInputTokensEstimated: 18_000,
            actualPromptTokens: null,
            actualCompletionTokens: 2_000,
            softBudgetExceeded: true,
            hardBudgetExceeded: false,
            trimTriggered: false,
            workingMemoryVersionUsed: 1,
            rawMessageCount: 10,
            sentMessageCount: 4,
            requestHash: "r1",
            sentPayloadHash: "s1",
            durationMs: null,
            upstreamDurationMs: null,
            prepareDurationMs: null,
            createdAt: now);

        var turn2 = ConversationTurnMetric.Create(
            conversationId,
            2,
            now,
            "m",
            rawInputTokensEstimated: 20_000,
            compressedInputTokensEstimated: 10_000,
            actualPromptTokens: 9_500,
            actualCompletionTokens: 1_000,
            softBudgetExceeded: false,
            hardBudgetExceeded: false,
            trimTriggered: false,
            workingMemoryVersionUsed: 1,
            rawMessageCount: 12,
            sentMessageCount: 5,
            requestHash: "r2",
            sentPayloadHash: "s2",
            durationMs: null,
            upstreamDurationMs: null,
            prepareDurationMs: null,
            createdAt: now);

        summary.ApplyTurn(turn1, now);
        summary.ApplyTurn(turn2, now);
        summary.ApplyCompressionOverhead(4_000, now);

        Assert.Equal(2, summary.TotalTurns);
        Assert.Equal(100_000, summary.TotalRawInputTokensEstimated);
        // Rollup uses compressed estimates only (18k + 10k), not actual prompt tokens.
        Assert.Equal(28_000, summary.TotalCompressedPromptTokens);
        Assert.Equal(3_000, summary.TotalCompletionTokens);
        Assert.Equal(4_000, summary.TotalCompressionOverheadTokens);
        Assert.Equal(103_000, summary.TotalBaselineTokensEstimated);
        Assert.Equal(35_000, summary.TotalActualTokensEstimated);
        Assert.Equal(68_000, summary.TotalNetTokensSaved);
        Assert.Equal(1, summary.CompressionEventCount);
        Assert.Equal(
            Math.Round(68_000d / 103_000d, 6),
            summary.AverageTokenSavingsRatio);
        Assert.Equal(0, summary.TotalVirtualToolsTokensSaved);
    }

    [Fact]
    public void ApplyTurn_AccumulatesVirtualToolsAndSoftBudgetFromIrFullBaseline()
    {
        var conversationId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var summary = ConversationMetricsSummary.Create(conversationId, now);

        var turn1 = ConversationTurnMetric.Create(
            conversationId,
            1,
            now,
            "m",
            rawInputTokensEstimated: 80_000,
            compressedInputTokensEstimated: 20_000,
            actualPromptTokens: null,
            actualCompletionTokens: 1_000,
            softBudgetExceeded: false,
            hardBudgetExceeded: false,
            trimTriggered: false,
            workingMemoryVersionUsed: 1,
            rawMessageCount: 10,
            sentMessageCount: 4,
            requestHash: "r1",
            sentPayloadHash: "s1",
            durationMs: null,
            upstreamDurationMs: null,
            prepareDurationMs: null,
            createdAt: now,
            irFullInputTokensEstimated: 55_000);

        var turn2 = ConversationTurnMetric.Create(
            conversationId,
            2,
            now,
            "m",
            rawInputTokensEstimated: 40_000,
            compressedInputTokensEstimated: 30_000,
            actualPromptTokens: null,
            actualCompletionTokens: 500,
            softBudgetExceeded: false,
            hardBudgetExceeded: false,
            trimTriggered: false,
            workingMemoryVersionUsed: 1,
            rawMessageCount: 12,
            sentMessageCount: 5,
            requestHash: "r2",
            sentPayloadHash: "s2",
            durationMs: null,
            upstreamDurationMs: null,
            prepareDurationMs: null,
            createdAt: now,
            irFullInputTokensEstimated: 30_000);

        summary.ApplyTurn(turn1, now);
        summary.ApplyTurn(turn2, now);

        // SoftBudget baselines: (55k+1k) + (30k+0.5k) = 86_500
        Assert.Equal(86_500, summary.TotalBaselineTokensEstimated);
        // SoftBudget nets: 35_000 + 0 = 35_000 (no overhead)
        Assert.Equal(35_000, summary.TotalNetTokensSaved);
        // VT: (80k−55k) + (40k−30k) = 35_000
        Assert.Equal(35_000, summary.TotalVirtualToolsTokensSaved);
        Assert.Equal(2, summary.TotalTurns);
    }
}

public class CompressionEventUsageTests
{
    [Fact]
    public void Succeed_StoresProviderUsage()
    {
        var started = DateTimeOffset.UtcNow;
        var evt = CompressionEvent.Start(
            Guid.NewGuid(),
            CompressionMode.Inline,
            originalTokens: 50_000,
            workingMemoryVersionBefore: 1,
            foldedMessageCount: 10,
            now: started);

        evt.Succeed(
            compressedTokens: 8_000,
            workingMemoryVersionAfter: 2,
            completedAt: started.AddSeconds(3),
            promptTokens: 12_000,
            completionTokens: 3_000,
            tokensAreEstimated: false);

        Assert.Equal(12_000, evt.PromptTokens);
        Assert.Equal(3_000, evt.CompletionTokens);
        Assert.Equal(15_000, evt.TotalTokens);
        Assert.False(evt.TokensAreEstimated);
    }

    [Fact]
    public void Succeed_MarksEstimatedUsage()
    {
        var started = DateTimeOffset.UtcNow;
        var evt = CompressionEvent.Start(
            Guid.NewGuid(),
            CompressionMode.Inline,
            originalTokens: 50_000,
            workingMemoryVersionBefore: null,
            foldedMessageCount: 10,
            now: started);

        evt.Succeed(
            compressedTokens: 8_000,
            workingMemoryVersionAfter: 1,
            completedAt: started.AddSeconds(1),
            promptTokens: 40_000,
            completionTokens: 2_000,
            tokensAreEstimated: true);

        Assert.True(evt.TokensAreEstimated);
        Assert.Equal(42_000, evt.TotalTokens);
    }

    [Fact]
    public void InlineSuccess_StoresModeVersionsAndUsage()
    {
        var started = DateTimeOffset.UtcNow;
        var evt = CompressionEvent.Start(
            Guid.NewGuid(),
            CompressionMode.Inline,
            originalTokens: 120,
            workingMemoryVersionBefore: 2,
            foldedMessageCount: 3,
            now: started);

        evt.Succeed(
            compressedTokens: 25,
            workingMemoryVersionAfter: 3,
            completedAt: started.AddSeconds(2),
            promptTokens: 90,
            completionTokens: 30,
            tokensAreEstimated: true);

        Assert.Equal(CompressionMode.Inline, evt.Mode);
        Assert.Equal(CompressionStatus.Succeeded, evt.Status);
        Assert.Equal(2, evt.WorkingMemoryVersionBefore);
        Assert.Equal(3, evt.WorkingMemoryVersionAfter);
        Assert.Equal(3, evt.FoldedMessageCount);
        Assert.Equal(120, evt.TotalTokens);
        Assert.True(evt.TokensAreEstimated);
    }
}

public class PromptTokenBasisProjectorTests
{
    [Fact]
    public void ProviderActual_ScalesRawBaseline_AndUsesActualCompressedInput()
    {
        // Legacy null IrFull: SoftBudget still scales NativeRaw vs Prepared.
        var turn = ConversationTurnMetric.Create(
            Guid.NewGuid(),
            turnIndex: 1,
            requestStartedAt: DateTimeOffset.UtcNow,
            model: "test-model",
            rawInputTokensEstimated: 80_000,
            compressedInputTokensEstimated: 20_000,
            actualPromptTokens: 30_000,
            actualCompletionTokens: 1_000,
            softBudgetExceeded: false,
            hardBudgetExceeded: false,
            trimTriggered: false,
            workingMemoryVersionUsed: null,
            rawMessageCount: 10,
            sentMessageCount: 4,
            requestHash: "r",
            sentPayloadHash: "s",
            durationMs: null,
            upstreamDurationMs: null,
            prepareDurationMs: null,
            createdAt: DateTimeOffset.UtcNow);

        var projected = PromptTokenBasisProjector.Project(turn, PromptTokenBasis.ProviderActual);

        Assert.Equal(120_000, projected.RawInputTokens);
        Assert.Equal(30_000, projected.CompressedInputTokens);
        Assert.Equal(121_000, projected.BaselineTotalTokens);
        Assert.Equal(31_000, projected.CompressedTotalTokens);
        Assert.Equal(90_000, projected.NetTokensSaved);
        Assert.Null(projected.IrFullInputTokens);
        Assert.Null(projected.VirtualToolsTokensSaved);
    }

    [Fact]
    public void Estimated_LeavesStoredProofUnchanged()
    {
        var turn = ConversationTurnMetric.Create(
            Guid.NewGuid(),
            turnIndex: 1,
            requestStartedAt: DateTimeOffset.UtcNow,
            model: "test-model",
            rawInputTokensEstimated: 80_000,
            compressedInputTokensEstimated: 20_000,
            actualPromptTokens: 30_000,
            actualCompletionTokens: 1_000,
            softBudgetExceeded: false,
            hardBudgetExceeded: false,
            trimTriggered: false,
            workingMemoryVersionUsed: null,
            rawMessageCount: 10,
            sentMessageCount: 4,
            requestHash: "r",
            sentPayloadHash: "s",
            durationMs: null,
            upstreamDurationMs: null,
            prepareDurationMs: null,
            createdAt: DateTimeOffset.UtcNow,
            irFullInputTokensEstimated: 55_000);

        var projected = PromptTokenBasisProjector.Project(turn, PromptTokenBasis.Estimated);

        Assert.Equal(turn.RawInputTokensEstimated, projected.RawInputTokens);
        Assert.Equal(turn.CompressedInputTokensEstimated, projected.CompressedInputTokens);
        Assert.Equal(turn.BaselineTotalTokensEstimated, projected.BaselineTotalTokens);
        Assert.Equal(turn.CompressedTotalTokensEstimated, projected.CompressedTotalTokens);
        Assert.Equal(turn.NetTokensSaved, projected.NetTokensSaved);
        Assert.Equal(turn.IrFullInputTokensEstimated, projected.IrFullInputTokens);
        Assert.Equal(turn.VirtualToolsTokensSaved, projected.VirtualToolsTokensSaved);
    }

    [Fact]
    public void ProviderActual_FallsBackToEstimate_WhenUsageMissing()
    {
        var turn = ConversationTurnMetric.Create(
            Guid.NewGuid(),
            turnIndex: 1,
            requestStartedAt: DateTimeOffset.UtcNow,
            model: "test-model",
            rawInputTokensEstimated: 10_000,
            compressedInputTokensEstimated: 8_000,
            actualPromptTokens: null,
            actualCompletionTokens: 500,
            softBudgetExceeded: false,
            hardBudgetExceeded: false,
            trimTriggered: false,
            workingMemoryVersionUsed: null,
            rawMessageCount: 3,
            sentMessageCount: 3,
            requestHash: "r",
            sentPayloadHash: "s",
            durationMs: null,
            upstreamDurationMs: null,
            prepareDurationMs: null,
            createdAt: DateTimeOffset.UtcNow,
            irFullInputTokensEstimated: 9_000);

        var projected = PromptTokenBasisProjector.Project(turn, PromptTokenBasis.ProviderActual);

        Assert.Equal(turn.CompressedTotalTokensEstimated, projected.CompressedTotalTokens);
        Assert.Equal(turn.NetTokensSaved, projected.NetTokensSaved);
        Assert.Equal(9_000, projected.IrFullInputTokens);
        Assert.Equal(1_000, projected.VirtualToolsTokensSaved);
    }

    [Theory]
    [InlineData(60_000, 20_000, 80_000)] // SoftBudget +, VT +
    [InlineData(15_000, 20_000, 80_000)] // SoftBudget −, VT +
    [InlineData(20_000, 20_000, 80_000)] // SoftBudget 0, VT +
    [InlineData(90_000, 20_000, 80_000)] // SoftBudget +, VT −
    public void ProviderActual_WithIrFull_PreservesSoftBudgetAndVtSigns(
        int irFull,
        int prepared,
        int nativeRaw)
    {
        const int actual = 30_000;
        const int completion = 1_000;
        var turn = ConversationTurnMetric.Create(
            Guid.NewGuid(),
            1,
            DateTimeOffset.UtcNow,
            "test-model",
            nativeRaw,
            prepared,
            actual,
            completion,
            softBudgetExceeded: false,
            hardBudgetExceeded: false,
            trimTriggered: false,
            workingMemoryVersionUsed: null,
            rawMessageCount: 4,
            sentMessageCount: 4,
            requestHash: "r",
            sentPayloadHash: "s",
            durationMs: null,
            upstreamDurationMs: null,
            prepareDurationMs: null,
            createdAt: DateTimeOffset.UtcNow,
            irFullInputTokensEstimated: irFull);

        var projected = PromptTokenBasisProjector.Project(turn, PromptTokenBasis.ProviderActual);

        Assert.Equal(Math.Sign(irFull - prepared), Math.Sign(projected.NetTokensSaved));
        Assert.NotNull(projected.IrFullInputTokens);
        Assert.NotNull(projected.VirtualToolsTokensSaved);
        Assert.Equal(
            Math.Sign(nativeRaw - irFull),
            Math.Sign(projected.VirtualToolsTokensSaved.Value));
        Assert.Equal(
            projected.IrFullInputTokens.Value - projected.CompressedInputTokens,
            projected.NetTokensSaved);
        Assert.Equal(
            projected.RawInputTokens - projected.IrFullInputTokens.Value,
            projected.VirtualToolsTokensSaved.Value);
    }

    [Fact]
    public void ApplyBasis_ProviderActual_PreservesScaledIrFullAndVt()
    {
        var source = new ConversationTurnProjection
        {
            TurnIndex = 2,
            RequestStartedAt = DateTimeOffset.UnixEpoch.AddMinutes(2),
            Model = "test-model",
            RawInputTokensEstimated = 80_000,
            IrFullInputTokensEstimated = 60_000,
            CompressedInputTokensEstimated = 20_000,
            ActualPromptTokens = 30_000,
            ActualCompletionTokens = 1_000,
            BaselineTotalTokensEstimated = 61_000,
            CompressedTotalTokensEstimated = 21_000,
            NetTokensSaved = 40_000,
            NetTokenSavingsRatio = Math.Round(40_000d / 61_000d, 6),
            VirtualToolsTokensSaved = 20_000,
            SoftBudgetExceeded = false,
            HardBudgetExceeded = false,
            TrimTriggered = false,
            WorkingMemoryVersionUsed = 1,
            RawMessageCount = 8,
            SentMessageCount = 4,
            DurationMs = 100,
            UpstreamDurationMs = 70,
            PrepareDurationMs = 20,
            CreatedAt = DateTimeOffset.UnixEpoch.AddMinutes(2)
        };

        var applied = PromptTokenBasisProjector.ApplyBasis(source, PromptTokenBasis.ProviderActual);

        Assert.Equal(120_000, applied.RawInputTokensEstimated);
        Assert.Equal(90_000, applied.IrFullInputTokensEstimated);
        Assert.Equal(30_000, applied.CompressedInputTokensEstimated);
        Assert.Equal(30_000, applied.VirtualToolsTokensSaved);
        Assert.Equal(91_000, applied.BaselineTotalTokensEstimated);
        Assert.Equal(31_000, applied.CompressedTotalTokensEstimated);
        Assert.Equal(60_000, applied.NetTokensSaved);
        Assert.Equal(
            applied.IrFullInputTokensEstimated!.Value - applied.CompressedInputTokensEstimated,
            applied.NetTokensSaved);
        Assert.Equal(2, applied.TurnIndex);
        Assert.Equal(1, applied.WorkingMemoryVersionUsed);
        Assert.Equal(100, applied.DurationMs);
    }

    [Fact]
    public void ApplyBasis_Estimated_PreservesStoredIrFullAndVt()
    {
        var source = new ConversationTurnProjection
        {
            TurnIndex = 1,
            RequestStartedAt = DateTimeOffset.UnixEpoch,
            Model = "test-model",
            RawInputTokensEstimated = 80_000,
            IrFullInputTokensEstimated = 60_000,
            CompressedInputTokensEstimated = 20_000,
            ActualPromptTokens = 30_000,
            ActualCompletionTokens = 1_000,
            BaselineTotalTokensEstimated = 61_000,
            CompressedTotalTokensEstimated = 21_000,
            NetTokensSaved = 40_000,
            NetTokenSavingsRatio = 0.655738,
            VirtualToolsTokensSaved = 20_000,
            SoftBudgetExceeded = true,
            HardBudgetExceeded = false,
            TrimTriggered = false,
            WorkingMemoryVersionUsed = 2,
            RawMessageCount = 10,
            SentMessageCount = 5,
            CreatedAt = DateTimeOffset.UnixEpoch
        };

        var applied = PromptTokenBasisProjector.ApplyBasis(source, PromptTokenBasis.Estimated);

        Assert.Same(source, applied);
        Assert.Equal(60_000, applied.IrFullInputTokensEstimated);
        Assert.Equal(20_000, applied.VirtualToolsTokensSaved);
    }
}
