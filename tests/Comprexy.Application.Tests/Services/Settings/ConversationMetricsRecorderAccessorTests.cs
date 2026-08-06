using Comprexy.Application.Abstractions;
using Comprexy.Application.Configuration;
using Comprexy.Application.Models;
using Comprexy.Application.Services;
using Comprexy.Application.Services.Settings;
using Comprexy.Domain.Entities;
using Moq;

namespace Comprexy.Application.Tests.Services.Settings;

public class ConversationMetricsRecorderAccessorTests
{
    [Fact]
    public void IsEnabled_UnsetAccessor_ReadsMonitor()
    {
        var accessor = new EffectiveSettingsAccessor();
        var options = new MetricsOptions { Enabled = true };
        var recorder = new ConversationMetricsRecorder(
            Mock.Of<IConversationTurnMetricRepository>(),
            Mock.Of<IConversationMetricsSummaryRepository>(),
            Mock.Of<IClock>(),
            accessor,
            new FixedOptionsMonitor<MetricsOptions>(options));

        Assert.True(recorder.IsEnabled);

        options.Enabled = false;
        Assert.False(recorder.IsEnabled);
    }

    [Fact]
    public void IsEnabled_AccessorSet_IgnoresLiveMonitor()
    {
        var accessor = new EffectiveSettingsAccessor();
        accessor.Set(new EffectiveSettingsV1 { MetricsEnabled = false });
        var options = new MetricsOptions { Enabled = true };
        var recorder = new ConversationMetricsRecorder(
            Mock.Of<IConversationTurnMetricRepository>(),
            Mock.Of<IConversationMetricsSummaryRepository>(),
            Mock.Of<IClock>(),
            accessor,
            new FixedOptionsMonitor<MetricsOptions>(options));

        Assert.False(recorder.IsEnabled);
    }

    [Fact]
    public async Task RecordCompressionOverheadAsync_StickyMetricsOff_LiveOn_DoesNotWrite()
    {
        var accessor = new EffectiveSettingsAccessor();
        accessor.Set(new EffectiveSettingsV1 { MetricsEnabled = false });
        var options = new MetricsOptions { Enabled = true };

        var summaryRepo = new Mock<IConversationMetricsSummaryRepository>(MockBehavior.Strict);
        var recorder = CreateRecorder(accessor, options, summaryRepo.Object);

        await recorder.RecordCompressionOverheadAsync(Guid.NewGuid(), overheadTokens: 100, CancellationToken.None);

        summaryRepo.Verify(
            r => r.FindByConversationIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        summaryRepo.Verify(r => r.Add(It.IsAny<ConversationMetricsSummary>()), Times.Never);
    }

    [Fact]
    public async Task RecordCompressionOverheadAsync_StickyMetricsOn_LiveOff_WritesOverhead()
    {
        var accessor = new EffectiveSettingsAccessor();
        accessor.Set(new EffectiveSettingsV1 { MetricsEnabled = true });
        var options = new MetricsOptions { Enabled = false };

        ConversationMetricsSummary? added = null;
        var summaryRepo = new Mock<IConversationMetricsSummaryRepository>();
        summaryRepo
            .Setup(r => r.FindByConversationIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ConversationMetricsSummary?)null);
        summaryRepo
            .Setup(r => r.Add(It.IsAny<ConversationMetricsSummary>()))
            .Callback<ConversationMetricsSummary>(s => added = s);

        var conversationId = Guid.NewGuid();
        var recorder = CreateRecorder(accessor, options, summaryRepo.Object);

        await recorder.RecordCompressionOverheadAsync(conversationId, overheadTokens: 42, CancellationToken.None);

        Assert.NotNull(added);
        Assert.Equal(conversationId, added!.ConversationId);
        Assert.Equal(42, added.TotalCompressionOverheadTokens);
        Assert.Equal(1, added.CompressionEventCount);
    }

    [Fact]
    public async Task RecordSuccessfulTurnAsync_StickyMetricsOff_LiveOn_DoesNotWrite()
    {
        var accessor = new EffectiveSettingsAccessor();
        accessor.Set(new EffectiveSettingsV1 { MetricsEnabled = false });
        var options = new MetricsOptions { Enabled = true };

        var turnRepo = new Mock<IConversationTurnMetricRepository>(MockBehavior.Strict);
        var summaryRepo = new Mock<IConversationMetricsSummaryRepository>(MockBehavior.Strict);
        var recorder = new ConversationMetricsRecorder(
            turnRepo.Object,
            summaryRepo.Object,
            Mock.Of<IClock>(c => c.UtcNow == DateTimeOffset.Parse("2026-01-15T12:00:00Z")),
            accessor,
            new FixedOptionsMonitor<MetricsOptions>(options));

        await recorder.RecordSuccessfulTurnAsync(
            CreateMonitorOnlyShapedInput(Guid.NewGuid()),
            CancellationToken.None);

        turnRepo.Verify(
            r => r.GetMaxTurnIndexAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        turnRepo.Verify(r => r.Add(It.IsAny<ConversationTurnMetric>()), Times.Never);
        summaryRepo.Verify(r => r.Add(It.IsAny<ConversationMetricsSummary>()), Times.Never);
    }

    [Fact]
    public async Task RecordSuccessfulTurnAsync_MonitorOnlyShape_SoftBudgetNetZeroOnSummary()
    {
        var accessor = new EffectiveSettingsAccessor();
        accessor.Set(new EffectiveSettingsV1 { MetricsEnabled = true });
        var options = new MetricsOptions { Enabled = false };

        ConversationMetricsSummary? added = null;
        var turnRepo = new Mock<IConversationTurnMetricRepository>();
        turnRepo
            .Setup(r => r.GetMaxTurnIndexAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        turnRepo.Setup(r => r.Add(It.IsAny<ConversationTurnMetric>()));

        var summaryRepo = new Mock<IConversationMetricsSummaryRepository>();
        summaryRepo
            .Setup(r => r.FindByConversationIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ConversationMetricsSummary?)null);
        summaryRepo
            .Setup(r => r.Add(It.IsAny<ConversationMetricsSummary>()))
            .Callback<ConversationMetricsSummary>(s => added = s);

        var conversationId = Guid.NewGuid();
        var recorder = new ConversationMetricsRecorder(
            turnRepo.Object,
            summaryRepo.Object,
            Mock.Of<IClock>(c => c.UtcNow == DateTimeOffset.Parse("2026-01-15T12:00:00Z")),
            accessor,
            new FixedOptionsMonitor<MetricsOptions>(options));

        await recorder.RecordSuccessfulTurnAsync(
            CreateMonitorOnlyShapedInput(conversationId),
            CancellationToken.None);

        Assert.NotNull(added);
        Assert.Equal(0, added!.TotalNetTokensSaved);
        Assert.Equal(42, added.TotalRawInputTokensEstimated);
        Assert.Equal(42, added.TotalCompressedPromptTokens);
        Assert.Equal(0, added.TotalCompressionOverheadTokens);
    }

    private static ConversationMetricsRecorder CreateRecorder(
        IEffectiveSettingsAccessor accessor,
        MetricsOptions options,
        IConversationMetricsSummaryRepository summaryRepo) =>
        new(
            Mock.Of<IConversationTurnMetricRepository>(),
            summaryRepo,
            Mock.Of<IClock>(c => c.UtcNow == DateTimeOffset.Parse("2026-01-15T12:00:00Z")),
            accessor,
            new FixedOptionsMonitor<MetricsOptions>(options));

    private static SuccessfulTurnMetricInput CreateMonitorOnlyShapedInput(Guid conversationId) =>
        new(
            ConversationId: conversationId,
            Model: "test-model",
            RequestStartedAt: DateTimeOffset.Parse("2026-01-15T12:00:00Z"),
            RawInputTokensEstimated: 42,
            CompressedInputTokensEstimated: 42,
            ActualPromptTokens: null,
            ActualCompletionTokens: 2,
            EstimatedCompletionTokensFallback: 2,
            BudgetDecision: ContextBudgetDecision.ForwardImmediate,
            TrimTriggered: false,
            WorkingMemoryVersionUsed: null,
            RawMessageCount: 1,
            SentMessageCount: 1,
            RequestHash: "req-hash",
            SentPayloadHash: "sent-hash",
            Timings: new TurnTimings(1, 1, 2),
            IrFullInputTokensEstimated: 42);
}
