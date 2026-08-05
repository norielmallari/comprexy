using Comprexy.Application.Abstractions;
using Comprexy.Application.Configuration;
using Comprexy.Application.Models.Telemetry;
using Comprexy.Application.Services;
using Comprexy.Domain.Entities;
using Comprexy.Domain.Enums;
using Microsoft.Extensions.Options;
using Moq;

namespace Comprexy.Application.Tests.Services;

public class TelemetryQueryLimitsTests
{
    [Theory]
    [InlineData(null, 100)]
    [InlineData(-1, 100)]
    [InlineData(0, 100)]
    [InlineData(1, 1)]
    [InlineData(1001, 1000)]
    public void ClampTake_EnforcesDefaultAndMaximum(int? requested, int expected)
    {
        Assert.Equal(expected, TelemetryQueryLimits.ClampTake(requested));
    }
}

public class RegressionDetectorTests
{
    [Fact]
    public void DetectSavingsRegressions_OnlyReportsDropsGreaterThanTenPercent()
    {
        var turns = new[]
        {
            Projection(1, ratio: 0),
            Projection(2, ratio: 0.50),
            Projection(3, ratio: 0.45),
            Projection(4, ratio: 0.449),
            Projection(5, ratio: -0.20),
            Projection(6, ratio: -0.50)
        };

        var regressions = new RegressionDetector().DetectSavingsRegressions(turns);

        var regression = Assert.Single(regressions);
        Assert.Equal(4, regression.FromTurnIndex);
        Assert.Equal(5, regression.ToTurnIndex);
        Assert.Equal(1.445434, regression.RelativeDrop);
    }

    private static ConversationTurnProjection Projection(int turn, double ratio) =>
        TelemetryTestData.Projection(turn, ratio: ratio);
}

public class EvidenceMarkdownServiceTests
{
    [Fact]
    public void Build_ProducesSoftBudgetAndVirtualToolsWording()
    {
        var summary = new ConversationSummaryDto
        {
            TurnCount = 12,
            TotalBaselineTokensEstimated = 123_456,
            TotalCompressedTokensEstimated = 45_678,
            TotalNetTokensSaved = 77_778,
            TotalVirtualToolsTokensSaved = 12_345,
            WeightedSavingsRatio = 0.63
        };
        var final = new FinalTurnSnapshotDto
        {
            BaselineTotalTokensEstimated = 20_000,
            CompressedTotalTokensEstimated = 5_000,
            NetTokenSavingsRatio = 0.75,
            RawMessageCount = 40,
            SentMessageCount = 10
        };

        var markdown = new EvidenceMarkdownService().Build(summary, final);

        Assert.Equal(
            """
            ## Validation Metrics

            - Total turns analyzed: 12
            - Total SoftBudget baseline tokens estimated (IrFull + completion when IrFull present): 123,456
            - Total prepared/sent-equivalent tokens: 45,678
            - Total SoftBudget net tokens saved (IrFull − Prepared when IrFull present): 77,778
            - Total virtual-tools / native-wire channel tokens (NativeRaw − IrFull; not tools-only; may be negative): 12,345
            - Weighted average SoftBudget token savings: 63.00%
            - Final turn SoftBudget token savings: 75.00%
            - Final SoftBudget payload: 20,000 -> 5,000 tokens
            - Raw messages reduced: 40 -> 10
            """,
            markdown);
        Assert.DoesNotContain("compressed/sent-equivalent", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("tools-only", markdown.Replace("not tools-only", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
    }
}

public class ConversationMetricsTelemetryTests
{
    private readonly Mock<IConversationMetricsSummaryRepository> _summaries = new();
    private readonly Mock<IConversationTurnMetricRepository> _turns = new();
    private readonly Mock<IConversationRepository> _conversations = new();
    private readonly Mock<IWorkingMemoryRepository> _workingMemories = new();
    private readonly Mock<ITokenEstimator> _tokenEstimator = new();

    [Fact]
    public async Task GetPhaseBreakdownAsync_SplitsOnlyOnWorkingMemoryOrTrimTransitions()
    {
        var id = Guid.NewGuid();
        var projections = new[]
        {
            TelemetryTestData.Projection(1, workingMemory: null),
            TelemetryTestData.Projection(2, workingMemory: null),
            TelemetryTestData.Projection(3, workingMemory: 1),
            TelemetryTestData.Projection(4, workingMemory: 1),
            TelemetryTestData.Projection(5, workingMemory: 2),
            TelemetryTestData.Projection(6, workingMemory: 2, trim: true),
            TelemetryTestData.Projection(7, workingMemory: 2, trim: true)
        };
        SetupTurns(id, projections);

        var phases = await CreateService().GetPhaseBreakdownAsync(id, null, CancellationToken.None);

        Assert.Collection(
            phases,
            phase =>
            {
                Assert.Equal("early_pre_working_memory", phase.Phase);
                Assert.Equal((1, 2), (phase.TurnStart, phase.TurnEnd));
            },
            phase =>
            {
                Assert.Equal("working_memory_v1", phase.Phase);
                Assert.Equal((3, 4), (phase.TurnStart, phase.TurnEnd));
            },
            phase =>
            {
                Assert.Equal("working_memory_v2", phase.Phase);
                Assert.Equal((5, 5), (phase.TurnStart, phase.TurnEnd));
            },
            phase =>
            {
                Assert.Equal("trimmed_mature_state", phase.Phase);
                Assert.Equal((6, 7), (phase.TurnStart, phase.TurnEnd));
            });
    }

    [Fact]
    public async Task GetBudgetEventsAsync_MapsFirstEventsMaximumAndFirstPostTrimPrompt()
    {
        var id = Guid.NewGuid();
        SetupTurns(
            id,
            [
                TelemetryTestData.Projection(1, actualPrompt: 100),
                TelemetryTestData.Projection(2, actualPrompt: 500, soft: true),
                TelemetryTestData.Projection(3, actualPrompt: 200, hard: true, trim: true),
                TelemetryTestData.Projection(4, actualPrompt: 150, soft: true, hard: true)
            ]);

        var result = await CreateService().GetBudgetEventsAsync(id, null, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(2, result.SoftBudgetFirstExceededAtTurn);
        Assert.Equal(3, result.HardBudgetFirstExceededAtTurn);
        Assert.Equal(3, result.TrimFirstTriggeredAtTurn);
        Assert.Equal(500, result.MaxActualPromptTokens);
        Assert.Equal(2, result.MaxActualPromptTokensTurn);
        Assert.Equal(150, result.PostTrimActualPromptTokens);
    }

    [Fact]
    public async Task GetBudgetEventsAsync_PostTrimPromptIsNullWhenTrimIsLastTurn()
    {
        var id = Guid.NewGuid();
        SetupTurns(
            id,
            [
                TelemetryTestData.Projection(1, actualPrompt: 100),
                TelemetryTestData.Projection(2, actualPrompt: 200, hard: true, trim: true)
            ]);

        var result = await CreateService().GetBudgetEventsAsync(id, null, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(2, result.TrimFirstTriggeredAtTurn);
        Assert.Null(result.PostTrimActualPromptTokens);
    }

    [Theory]
    [MemberData(nameof(SummaryCases))]
    public async Task GetTelemetrySummaryAsync_ComputesWeightedMedianPeakFinalAndRegressions(
        double[] ratios,
        double expectedMedian)
    {
        var id = Guid.NewGuid();
        _summaries
            .Setup(x => x.GetRollupAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConversationSummaryRollup
            {
                ConversationId = id,
                TotalTurns = ratios.Length,
                TotalBaselineTokensEstimated = 1000,
                TotalCompressedPromptTokens = 300,
                TotalCompletionTokens = 100,
                TotalNetTokensSaved = 600,
                AverageTokenSavingsRatio = 0.123
            });
        var projections = ratios.Select((ratio, index) =>
            TelemetryTestData.Projection(index + 1, ratio: ratio, model: $"model-{index + 1}")).ToArray();
        SetupTurns(id, projections);
        SetupSummaryDependencies(
            id,
            projections[^1],
            new ConversationTurnSavingsAggregates
            {
                PeakNetTokenSavingsRatio = ratios.Max(),
                SimpleAverageNetTokenSavingsRatio = ratios.Average(),
                TurnCount = ratios.Length
            });

        var result = await CreateService().GetTelemetrySummaryAsync(id, 50, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(0.6, result.WeightedSavingsRatio);
        Assert.Equal(Math.Round(ratios.Average(), 6), result.SimpleAverageSavingsRatio);
        Assert.Equal(expectedMedian, result.MedianSavingsRatio);
        Assert.Equal(ratios.Max(), result.PeakSavingsRatio);
        Assert.Equal(ratios[^1], result.FinalTurnSavingsRatio);
        Assert.Equal($"model-{ratios.Length}", result.Model);
        Assert.Equal(ratios.Length, result.SampleTurnCount);
        Assert.Equal(1, result.SampleFirstTurnIndex);
        Assert.Equal(ratios.Length, result.SampleLastTurnIndex);
        Assert.False(result.IsPartialTurnSample);
        Assert.NotEmpty(result.SavingsRegressions);
        _turns.Verify(
            x => x.ListBoundedProjectionsAsync(id, 50, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    public static TheoryData<double[], double> SummaryCases =>
        new()
        {
            { [0.8, 0.4, 0.6], 0.6 },
            { [0.8, 0.4, 0.6, 0.2], 0.5 }
        };

    [Fact]
    public async Task GetTelemetrySummaryAsync_UsesWholeConversationValuesBeyondBoundedSample()
    {
        var id = Guid.NewGuid();
        var sample = new[]
        {
            TelemetryTestData.Projection(1, ratio: 0.8, model: "sample-1"),
            TelemetryTestData.Projection(2, ratio: 0.4, model: "sample-2"),
            TelemetryTestData.Projection(3, ratio: 0.6, model: "sample-3")
        };
        var final = TelemetryTestData.Projection(5, ratio: 0.9, model: "true-final-model");
        _summaries
            .Setup(x => x.GetRollupAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConversationSummaryRollup
            {
                ConversationId = id,
                TotalTurns = 5,
                TotalBaselineTokensEstimated = 1000,
                TotalCompressedPromptTokens = 300,
                TotalCompletionTokens = 100,
                TotalNetTokensSaved = 600
            });
        SetupTurns(id, sample);
        SetupSummaryDependencies(
            id,
            final,
            new ConversationTurnSavingsAggregates
            {
                PeakNetTokenSavingsRatio = 0.95,
                SimpleAverageNetTokenSavingsRatio = 0.65,
                TurnCount = 5
            });

        var result = await CreateService().GetTelemetrySummaryAsync(id, 3, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("true-final-model", result.Model);
        Assert.Equal(0.9, result.FinalTurnSavingsRatio);
        Assert.Equal(0.95, result.PeakSavingsRatio);
        Assert.Equal(0.65, result.SimpleAverageSavingsRatio);
        Assert.Equal(0.6, result.MedianSavingsRatio);
        var regression = Assert.Single(result.SavingsRegressions);
        Assert.Equal((1, 2), (regression.FromTurnIndex, regression.ToTurnIndex));
        Assert.Equal(3, result.SampleTurnCount);
        Assert.Equal(1, result.SampleFirstTurnIndex);
        Assert.Equal(3, result.SampleLastTurnIndex);
        Assert.True(result.IsPartialTurnSample);
        _turns.Verify(
            x => x.GetFinalTurnProjectionAsync(id, It.IsAny<CancellationToken>()),
            Times.Once);
        _turns.Verify(
            x => x.GetSavingsAggregatesAsync(id, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetTelemetrySummaryAsync_ProviderActual_TotalVtEqualsSumOfProjectedTurnVt()
    {
        var id = Guid.NewGuid();
        var turn1 = TelemetryTestData.TurnMetric(
            id,
            turn: 1,
            compressedInput: 20_000,
            workingMemoryVersion: 1,
            rawInput: 80_000,
            irFull: 60_000,
            actualPrompt: 30_000,
            completion: 1_000);
        var turn2 = TelemetryTestData.TurnMetric(
            id,
            turn: 2,
            compressedInput: 10_000,
            workingMemoryVersion: 1,
            rawInput: 40_000,
            irFull: 25_000,
            actualPrompt: 12_000,
            completion: 500);
        var estimateVtRollup = (turn1.VirtualToolsTokensSaved ?? 0) + (turn2.VirtualToolsTokensSaved ?? 0);
        _summaries
            .Setup(x => x.GetRollupAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConversationSummaryRollup
            {
                ConversationId = id,
                TotalTurns = 2,
                TotalBaselineTokensEstimated = turn1.BaselineTotalTokensEstimated + turn2.BaselineTotalTokensEstimated,
                TotalCompressedPromptTokens = 30_000,
                TotalCompletionTokens = 1_500,
                TotalNetTokensSaved = 55_000,
                TotalVirtualToolsTokensSaved = estimateVtRollup,
                TotalCompressionOverheadTokens = 0
            });
        var projections = new[]
        {
            TelemetryTestData.ProjectionFromMetric(turn1),
            TelemetryTestData.ProjectionFromMetric(turn2)
        };
        SetupTurns(id, projections);
        _turns
            .Setup(x => x.GetFinalTurnProjectionAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(projections[^1]);
        _turns
            .Setup(x => x.ListByConversationIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([turn1, turn2]);

        var expectedVt = PromptTokenBasisProjector.Project(turn1, PromptTokenBasis.ProviderActual).VirtualToolsTokensSaved!.Value
            + PromptTokenBasisProjector.Project(turn2, PromptTokenBasis.ProviderActual).VirtualToolsTokensSaved!.Value;

        var result = await CreateService(PromptTokenBasis.ProviderActual)
            .GetTelemetrySummaryAsync(id, 50, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(expectedVt, result.TotalVirtualToolsTokensSaved);
        Assert.NotEqual(estimateVtRollup, expectedVt);
        _turns.Verify(
            x => x.ListByConversationIdAsync(id, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetTelemetryTurnsAsync_ExposesIrFullVtAndLegacyFlag()
    {
        var id = Guid.NewGuid();
        var withIrFull = TelemetryTestData.Projection(
            1,
            ratio: 0.5,
            baseline: 60_000,
            actualPrompt: 20_000,
            irFull: 60_000,
            virtualTools: 20_000,
            rawInput: 80_000);
        var legacy = TelemetryTestData.Projection(
            2,
            ratio: 0.4,
            baseline: 50_000,
            actualPrompt: 30_000,
            irFull: null,
            virtualTools: null,
            rawInput: 50_000);
        SetupTurns(id, [withIrFull, legacy]);

        var turns = await CreateService().GetTelemetryTurnsAsync(id, 50, CancellationToken.None);

        Assert.Collection(
            turns,
            first =>
            {
                Assert.Equal(60_000, first.IrFullInputTokensEstimated);
                Assert.Equal(20_000, first.VirtualToolsTokensSaved);
                Assert.False(first.IsLegacyMixedAxis);
            },
            second =>
            {
                Assert.Null(second.IrFullInputTokensEstimated);
                Assert.Null(second.VirtualToolsTokensSaved);
                Assert.True(second.IsLegacyMixedAxis);
            });
    }

    [Fact]
    public async Task GetTelemetrySummaryAsync_ReturnsNullWithoutRollup()
    {
        var id = Guid.NewGuid();
        _summaries
            .Setup(x => x.GetRollupAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ConversationSummaryRollup?)null);

        var result = await CreateService().GetTelemetrySummaryAsync(id, null, CancellationToken.None);

        Assert.Null(result);
        _turns.Verify(
            x => x.ListBoundedProjectionsAsync(
                It.IsAny<Guid>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CompareConversationsAsync_ReturnsBothSummariesOrNullWhenEitherMissing()
    {
        var left = Guid.NewGuid();
        var right = Guid.NewGuid();
        _summaries
            .Setup(x => x.GetRollupAsync(left, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TelemetryTestData.Rollup(left));
        _summaries
            .Setup(x => x.GetRollupAsync(right, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TelemetryTestData.Rollup(right));
        SetupTurns(left, [TelemetryTestData.Projection(1)]);
        SetupTurns(right, [TelemetryTestData.Projection(1)]);
        SetupSummaryDependencies(
            left,
            TelemetryTestData.Projection(1),
            new ConversationTurnSavingsAggregates
            {
                PeakNetTokenSavingsRatio = 0.5,
                SimpleAverageNetTokenSavingsRatio = 0.5,
                TurnCount = 1
            });
        SetupSummaryDependencies(
            right,
            TelemetryTestData.Projection(1),
            new ConversationTurnSavingsAggregates
            {
                PeakNetTokenSavingsRatio = 0.5,
                SimpleAverageNetTokenSavingsRatio = 0.5,
                TurnCount = 1
            });

        var comparison = await CreateService().CompareConversationsAsync(
            left,
            right,
            null,
            CancellationToken.None);

        Assert.NotNull(comparison);
        Assert.Equal(left, comparison.Left.ConversationId);
        Assert.Equal(right, comparison.Right.ConversationId);

        _summaries
            .Setup(x => x.GetRollupAsync(right, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ConversationSummaryRollup?)null);
        Assert.Null(await CreateService().CompareConversationsAsync(
            left,
            right,
            null,
            CancellationToken.None));
    }

    [Fact]
    public async Task GetTelemetryTurnsAsync_HandlesZeroDenominatorsAndExcludesHashes()
    {
        var id = Guid.NewGuid();
        SetupTurns(
            id,
            [
                TelemetryTestData.Projection(
                    1,
                    baseline: 0,
                    rawMessages: 0,
                    sentMessages: 0)
            ]);

        var dto = Assert.Single(await CreateService().GetTelemetryTurnsAsync(
            id,
            null,
            CancellationToken.None));

        Assert.Equal(0, dto.CompressionRatio);
        Assert.Null(dto.MessageReductionRatio);
        Assert.DoesNotContain(
            typeof(ConversationTurnDto).GetProperties(),
            property => property.Name.Contains("Hash", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            typeof(ConversationTurnProjection).GetProperties(),
            property => property.Name.Contains("Hash", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ListTurnContextBreakdownsAsync_HoldsSystemConstantAndKeepsWorkingMemoryZeroUntilFirstVersion()
    {
        var id = Guid.NewGuid();
        var conversation = Conversation.Create("key", DateTimeOffset.UnixEpoch);
        conversation.CaptureSystemPromptIfAbsent("system prompt");

        _conversations
            .Setup(x => x.FindByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(conversation);
        _tokenEstimator.Setup(x => x.CountTokens("system prompt")).Returns(300);
        _workingMemories
            .Setup(x => x.ListVersionTokenCountsAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new WorkingMemoryVersionTokens { Version = 1, TokenCount = 800 }]);
        _turns
            .Setup(x => x.ListByConversationIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                TelemetryTestData.TurnMetric(id, 1, compressedInput: 5_000, workingMemoryVersion: null),
                TelemetryTestData.TurnMetric(id, 2, compressedInput: 4_000, workingMemoryVersion: 1)
            ]);

        var breakdowns = await CreateService().ListTurnContextBreakdownsAsync(id, CancellationToken.None);

        Assert.Collection(
            breakdowns,
            first =>
            {
                Assert.Equal(300, first.SystemPromptTokensEstimated);
                Assert.Equal(0, first.WorkingMemoryTokensEstimated);
                Assert.Equal(4_700, first.HistoryAndToolsTokensEstimated);
            },
            second =>
            {
                Assert.Equal(300, second.SystemPromptTokensEstimated);
                Assert.Equal(800, second.WorkingMemoryTokensEstimated);
                Assert.Equal(2_900, second.HistoryAndToolsTokensEstimated);
            });
    }

    [Fact]
    public async Task ListTurnContextBreakdownsAsync_SegmentsSumToPreparedPrompt()
    {
        var id = Guid.NewGuid();
        _conversations
            .Setup(x => x.FindByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Conversation.Create("key", DateTimeOffset.UnixEpoch));
        _tokenEstimator.Setup(x => x.CountTokens(ContextBuilder.DefaultSystemPrompt)).Returns(7);
        _workingMemories
            .Setup(x => x.ListVersionTokenCountsAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new WorkingMemoryVersionTokens { Version = 2, TokenCount = 1_200 }]);
        _turns
            .Setup(x => x.ListByConversationIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([TelemetryTestData.TurnMetric(id, 9, compressedInput: 12_345, workingMemoryVersion: 2)]);

        var breakdown = Assert.Single(
            await CreateService().ListTurnContextBreakdownsAsync(id, CancellationToken.None));

        Assert.Equal(
            12_345,
            breakdown.SystemPromptTokensEstimated
                + breakdown.WorkingMemoryTokensEstimated
                + breakdown.HistoryAndToolsTokensEstimated);
    }

    private ConversationMetricsQueryService CreateService(
        PromptTokenBasis basis = PromptTokenBasis.Estimated)
    {
        var options = new Mock<IOptionsMonitor<MetricsOptions>>();
        options.Setup(o => o.CurrentValue).Returns(new MetricsOptions
        {
            // Telemetry rollup unit tests assert estimate-ledger MapSummary paths by default.
            PromptTokenBasis = basis
        });
        return new ConversationMetricsQueryService(
            _summaries.Object,
            _turns.Object,
            _conversations.Object,
            _workingMemories.Object,
            _tokenEstimator.Object,
            new EvidenceMarkdownService(),
            new RegressionDetector(),
            new PromptTokenBasisContext(options.Object));
    }

    private void SetupTurns(Guid id, IReadOnlyList<ConversationTurnProjection> turns) =>
        _turns
            .Setup(x => x.ListBoundedProjectionsAsync(
                id,
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(turns);

    private void SetupSummaryDependencies(
        Guid id,
        ConversationTurnProjection finalTurn,
        ConversationTurnSavingsAggregates aggregates)
    {
        _turns
            .Setup(x => x.GetFinalTurnProjectionAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(finalTurn);
        _turns
            .Setup(x => x.GetSavingsAggregatesAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(aggregates);
    }
}

internal static class TelemetryTestData
{
    public static ConversationTurnProjection Projection(
        int turn,
        double ratio = 0.5,
        int? workingMemory = null,
        bool trim = false,
        bool soft = false,
        bool hard = false,
        int? actualPrompt = 100,
        int baseline = 200,
        int rawMessages = 10,
        int sentMessages = 5,
        string model = "test-model",
        int? irFull = null,
        int? virtualTools = null,
        int? rawInput = null) =>
        new()
        {
            TurnIndex = turn,
            RequestStartedAt = DateTimeOffset.UnixEpoch.AddMinutes(turn),
            Model = model,
            RawInputTokensEstimated = rawInput ?? baseline,
            IrFullInputTokensEstimated = irFull,
            CompressedInputTokensEstimated = actualPrompt ?? 100,
            ActualPromptTokens = actualPrompt,
            ActualCompletionTokens = 0,
            BaselineTotalTokensEstimated = baseline,
            CompressedTotalTokensEstimated = actualPrompt ?? 100,
            NetTokensSaved = (int)Math.Round(baseline * ratio),
            NetTokenSavingsRatio = ratio,
            VirtualToolsTokensSaved = virtualTools,
            SoftBudgetExceeded = soft,
            HardBudgetExceeded = hard,
            TrimTriggered = trim,
            WorkingMemoryVersionUsed = workingMemory,
            RawMessageCount = rawMessages,
            SentMessageCount = sentMessages,
            CreatedAt = DateTimeOffset.UnixEpoch.AddMinutes(turn)
        };

    public static ConversationTurnProjection ProjectionFromMetric(ConversationTurnMetric turn) =>
        new()
        {
            TurnIndex = turn.TurnIndex,
            RequestStartedAt = turn.RequestStartedAt,
            Model = turn.Model,
            RawInputTokensEstimated = turn.RawInputTokensEstimated,
            IrFullInputTokensEstimated = turn.IrFullInputTokensEstimated,
            CompressedInputTokensEstimated = turn.CompressedInputTokensEstimated,
            ActualPromptTokens = turn.ActualPromptTokens,
            ActualCompletionTokens = turn.ActualCompletionTokens,
            BaselineTotalTokensEstimated = turn.BaselineTotalTokensEstimated,
            CompressedTotalTokensEstimated = turn.CompressedTotalTokensEstimated,
            NetTokensSaved = turn.NetTokensSaved,
            NetTokenSavingsRatio = turn.NetTokenSavingsRatio,
            VirtualToolsTokensSaved = turn.VirtualToolsTokensSaved,
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

    public static ConversationTurnMetric TurnMetric(
        Guid conversationId,
        int turn,
        int compressedInput,
        int? workingMemoryVersion,
        int? rawInput = null,
        int? irFull = null,
        int? actualPrompt = null,
        int completion = 0) =>
        ConversationTurnMetric.Create(
            conversationId,
            turn,
            DateTimeOffset.UnixEpoch.AddMinutes(turn),
            "test-model",
            rawInputTokensEstimated: rawInput ?? compressedInput * 2,
            compressedInputTokensEstimated: compressedInput,
            actualPromptTokens: actualPrompt ?? compressedInput,
            actualCompletionTokens: completion,
            softBudgetExceeded: false,
            hardBudgetExceeded: false,
            trimTriggered: false,
            workingMemoryVersionUsed: workingMemoryVersion,
            rawMessageCount: 10,
            sentMessageCount: 5,
            requestHash: string.Empty,
            sentPayloadHash: string.Empty,
            durationMs: null,
            upstreamDurationMs: null,
            prepareDurationMs: null,
            createdAt: DateTimeOffset.UnixEpoch.AddMinutes(turn),
            irFullInputTokensEstimated: irFull);

    public static ConversationSummaryRollup Rollup(Guid id) =>
        new()
        {
            ConversationId = id,
            TotalTurns = 1,
            TotalBaselineTokensEstimated = 200,
            TotalCompressedPromptTokens = 100,
            TotalNetTokensSaved = 100,
            AverageTokenSavingsRatio = 0.5
        };
}
