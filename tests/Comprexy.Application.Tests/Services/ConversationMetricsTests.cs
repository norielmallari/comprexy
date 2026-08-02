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
    }

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
