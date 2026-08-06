using Comprexy.Application.Models;
using Comprexy.Application.Services;
using Comprexy.Application.Services.Rules;
using Comprexy.Application.Services.Settings;
using Comprexy.Domain.Entities;
using Comprexy.Domain.Enums;
using Moq;

namespace Comprexy.Application.Tests.Services.Settings;

public class EffectiveSettingsStickyAndMonitorOnlyTests
{
    [Fact]
    public async Task Sticky_OptimizationMode_A_to_B_SnapshotUnchanged()
    {
        var h = new SliceCTestHarness
        {
            Proxy = new() { OptimizationMode = OptimizationMode.MonitorOnly },
            Metrics = new() { Enabled = true }
        };
        var preparer = h.CreatePreparer();
        var request = SliceCTestHarness.BuildRequest("sticky-mode");

        var first = await preparer.PrepareAsync(request, "header:sticky-mode", _ => Task.CompletedTask, CancellationToken.None);
        var jsonAfterFirst = first.Conversation.EffectiveSettingsJson;
        Assert.NotNull(jsonAfterFirst);
        Assert.Equal(OptimizationMode.MonitorOnly, h.Accessor.Current.OptimizationMode);
        Assert.True(first.SkipCompression);
        Assert.NotNull(first.MetricsPrepare);

        h.Proxy.OptimizationMode = OptimizationMode.Full;
        var second = await preparer.PrepareAsync(
            SliceCTestHarness.BuildRequest("sticky-mode", userContent: "follow-up"),
            "header:sticky-mode",
            _ => Task.CompletedTask,
            CancellationToken.None);

        Assert.Equal(jsonAfterFirst, second.Conversation.EffectiveSettingsJson);
        Assert.Equal(OptimizationMode.MonitorOnly, h.Accessor.Current.OptimizationMode);
        Assert.True(second.SkipCompression);
        Assert.False(second.UpstreamRequest.ReplaceMessages);
    }

    [Fact]
    public async Task Sticky_SoftLimit_EvaluateUsesBoundS1()
    {
        var h = new SliceCTestHarness
        {
            Proxy = new() { OptimizationMode = OptimizationMode.Full },
            Policy = new() { SoftLimitTokens = 200, CompressionRetainMessageCount = 1 },
            ToolSchema = new() { Mode = ToolSchemaMode.Off },
            EstimatedTokens = 100
        };
        var preparer = h.CreatePreparer();

        var first = await preparer.PrepareAsync(
            SliceCTestHarness.BuildRequest("sticky-soft"),
            "header:sticky-soft",
            _ => Task.CompletedTask,
            CancellationToken.None);
        Assert.Equal(200, h.Accessor.Current.SoftLimitTokens);
        Assert.Equal(ContextBudgetDecision.ForwardImmediate, first.Decision);

        // Live soft limit drops below estimated tokens; sticky S1=200 must still win.
        h.Policy.SoftLimitTokens = 50;
        var second = await preparer.PrepareAsync(
            SliceCTestHarness.BuildRequest("sticky-soft", userContent: "again"),
            "header:sticky-soft",
            _ => Task.CompletedTask,
            CancellationToken.None);

        Assert.Equal(200, h.Accessor.Current.SoftLimitTokens);
        Assert.Contains("\"softLimitTokens\":200", second.Conversation.EffectiveSettingsJson!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ContextBudgetDecision.ForwardImmediate, second.Decision);
    }

    [Fact]
    public async Task Sticky_Retain_AccessorHoldsR1_AndSelectOverloadUsesR1()
    {
        var h = new SliceCTestHarness
        {
            Proxy = new() { OptimizationMode = OptimizationMode.Full },
            Policy = new() { SoftLimitTokens = 10_000, CompressionRetainMessageCount = 1 },
            ToolSchema = new() { Mode = ToolSchemaMode.Off }
        };
        var preparer = h.CreatePreparer();
        await preparer.PrepareAsync(
            SliceCTestHarness.BuildRequest("sticky-retain"),
            "header:sticky-retain",
            _ => Task.CompletedTask,
            CancellationToken.None);
        Assert.Equal(1, h.Accessor.Current.CompressionRetainMessageCount);

        h.Policy.CompressionRetainMessageCount = 50;
        await preparer.PrepareAsync(
            SliceCTestHarness.BuildRequest("sticky-retain", userContent: "tip-2"),
            "header:sticky-retain",
            _ => Task.CompletedTask,
            CancellationToken.None);
        Assert.Equal(1, h.Accessor.Current.CompressionRetainMessageCount);

        var conversationId = h.TrackedConversation!.Id;
        var messages = Enumerable.Range(0, 5)
            .Select(i => ConversationMessage.Create(
                conversationId, i, MessageRole.User, $"m{i}", 5, DateTimeOffset.UtcNow))
            .ToList();
        var selector = new RecentContextSelector(
            Microsoft.Extensions.Options.Options.Create(h.Policy));
        var selected = selector.Select(messages, h.Accessor.Current.CompressionRetainMessageCount);
        Assert.Single(selected);
        Assert.Equal(4, selected[0].Sequence);
    }

    [Fact]
    public async Task Sticky_ToolSchemaModeOff_StaysOffUnderGlobalOn()
    {
        var h = new SliceCTestHarness
        {
            Proxy = new() { OptimizationMode = OptimizationMode.Full },
            ToolSchema = new() { Mode = ToolSchemaMode.Off }
        };
        var orchestrator = h.CreateOrchestrator();
        var preparer = h.CreatePreparer(orchestrator);

        await preparer.PrepareAsync(
            SliceCTestHarness.BuildRequest("sticky-vt"),
            "header:sticky-vt",
            _ => Task.CompletedTask,
            CancellationToken.None);
        Assert.Equal(ToolSchemaMode.Off, h.Accessor.Current.ToolSchemaMode);
        Assert.False(orchestrator.ShouldAttemptActivation(h.Accessor.Current.SkipsPromptOptimizations));

        h.ToolSchema.Mode = ToolSchemaMode.Virtual;
        await preparer.PrepareAsync(
            SliceCTestHarness.BuildRequest("sticky-vt", userContent: "next"),
            "header:sticky-vt",
            _ => Task.CompletedTask,
            CancellationToken.None);

        Assert.Equal(ToolSchemaMode.Off, h.Accessor.Current.ToolSchemaMode);
        Assert.False(orchestrator.ShouldAttemptActivation(false));
    }

    [Fact]
    public async Task Sticky_StripReasoning_FollowsSnapshot()
    {
        var h = new SliceCTestHarness
        {
            Proxy = new() { OptimizationMode = OptimizationMode.Full, StripReasoningContent = true }
        };
        var preparer = h.CreatePreparer();
        await preparer.PrepareAsync(
            SliceCTestHarness.BuildRequest("sticky-strip"),
            "header:sticky-strip",
            _ => Task.CompletedTask,
            CancellationToken.None);
        Assert.True(h.Accessor.Current.StripReasoningContent);

        h.Proxy.StripReasoningContent = false;
        await preparer.PrepareAsync(
            SliceCTestHarness.BuildRequest("sticky-strip", userContent: "next"),
            "header:sticky-strip",
            _ => Task.CompletedTask,
            CancellationToken.None);
        Assert.True(h.Accessor.Current.StripReasoningContent);
    }

    [Fact]
    public async Task LegacyNull_NoBackfill_UsesLive()
    {
        var now = DateTimeOffset.UtcNow;
        var conversation = Conversation.Create("header:legacy-null", now);
        Assert.Null(conversation.EffectiveSettingsJson);

        var h = new SliceCTestHarness
        {
            Proxy = new() { OptimizationMode = OptimizationMode.MonitorOnly },
            Metrics = new() { Enabled = true }
        };
        h.SeedExistingConversation(conversation);
        var preparer = h.CreatePreparer();

        var prepared = await preparer.PrepareAsync(
            SliceCTestHarness.BuildRequest("legacy-null"),
            "header:legacy-null",
            _ => Task.CompletedTask,
            CancellationToken.None);

        Assert.Null(prepared.Conversation.EffectiveSettingsJson);
        Assert.Equal(OptimizationMode.MonitorOnly, h.Accessor.Current.OptimizationMode);
        Assert.True(prepared.SkipCompression);
        Assert.NotNull(prepared.MetricsPrepare);

        h.Proxy.OptimizationMode = OptimizationMode.Full;
        h.Metrics.Enabled = false;
        var second = await preparer.PrepareAsync(
            SliceCTestHarness.BuildRequest("legacy-null", userContent: "live-flip"),
            "header:legacy-null",
            _ => Task.CompletedTask,
            CancellationToken.None);

        Assert.Null(second.Conversation.EffectiveSettingsJson);
        Assert.Equal(OptimizationMode.Full, h.Accessor.Current.OptimizationMode);
        Assert.False(second.SkipCompression);
    }

    [Fact]
    public async Task MonitorOnly_CapturesBaseSystem_NoVt_WireIsClientBody_InvalidatesPrefix()
    {
        var h = new SliceCTestHarness
        {
            Proxy = new() { OptimizationMode = OptimizationMode.MonitorOnly },
            ToolSchema = new() { Mode = ToolSchemaMode.Virtual },
            Metrics = new() { Enabled = true }
        };
        var orchestrator = h.CreateOrchestrator();
        var preparer = h.CreatePreparer(orchestrator);
        const string systemContent = """
            Agent persona for observation.

            Instructions from: /workspace/repo/.kilo/rules/fixture.md
            Fixture rule body only.
            """;
        var expectedBaseSystem = new SystemRulesDetector().Detect(systemContent).BaseSystem;
        var request = SliceCTestHarness.BuildRequest("mon-opt", systemContent: systemContent);

        var prepared = await preparer.PrepareAsync(
            request,
            "header:mon-opt",
            _ => Task.CompletedTask,
            CancellationToken.None);

        Assert.Equal(expectedBaseSystem, prepared.Conversation.SystemPrompt);
        Assert.DoesNotContain("Fixture rule body", prepared.Conversation.SystemPrompt, StringComparison.Ordinal);
        // Janitor only: MonitorOnly never materializes Prefix, but drops any leftover Full Prefix
        // so an unbound live mode flip cannot warm-reuse stale BaseSystem bytes.
        h.CacheAlignment.Verify(
            c => c.Invalidate(prepared.Conversation.Id),
            Times.Once);
        Assert.False(prepared.UpstreamRequest.ReplaceMessages);
        Assert.Same(request.Messages, prepared.UpstreamRequest.Messages);
        Assert.True(prepared.SkipCompression);
        Assert.NotNull(prepared.MetricsPrepare);
        Assert.Equal(prepared.MetricsPrepare!.RawInputTokensEstimated, prepared.MetricsPrepare.IrFullInputTokensEstimated);
    }

    [Fact]
    public async Task MonitorOnly_MetricsOn_CompleterRecordsBaseline()
    {
        var h = new SliceCTestHarness
        {
            Proxy = new() { OptimizationMode = OptimizationMode.MonitorOnly },
            Metrics = new() { Enabled = true },
            EstimatedTokens = 42
        };
        var service = h.CreateService();
        await service.HandleAsync(SliceCTestHarness.BuildRequest("mon-metrics-on"), CancellationToken.None);

        h.MetricsRecorder.Verify(
            m => m.RecordSuccessfulTurnAsync(
                It.Is<SuccessfulTurnMetricInput>(i =>
                    i.RawInputTokensEstimated == 42
                    && i.CompressedInputTokensEstimated == 42
                    && i.IrFullInputTokensEstimated == 42),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task MonitorOnly_MetricsOff_NoRecord()
    {
        var h = new SliceCTestHarness
        {
            Proxy = new() { OptimizationMode = OptimizationMode.MonitorOnly },
            Metrics = new() { Enabled = false }
        };
        var preparer = h.CreatePreparer();
        var prepared = await preparer.PrepareAsync(
            SliceCTestHarness.BuildRequest("mon-metrics-off"),
            "header:mon-metrics-off",
            _ => Task.CompletedTask,
            CancellationToken.None);

        Assert.Null(prepared.MetricsPrepare);

        var service = h.CreateService();
        await service.HandleAsync(SliceCTestHarness.BuildRequest("mon-metrics-off-svc"), CancellationToken.None);
        h.MetricsRecorder.Verify(
            m => m.RecordSuccessfulTurnAsync(It.IsAny<SuccessfulTurnMetricInput>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task PassThrough_NeverMetrics_EvenWhenMetricsEnabled()
    {
        var h = new SliceCTestHarness
        {
            Proxy = new() { PassThrough = true, OptimizationMode = OptimizationMode.Full },
            Metrics = new() { Enabled = true }
        };
        var preparer = h.CreatePreparer();
        var prepared = await preparer.PrepareAsync(
            SliceCTestHarness.BuildRequest("pt-metrics", systemContent: "PassThrough system text."),
            "header:pt-metrics",
            _ => Task.CompletedTask,
            CancellationToken.None);

        Assert.Null(prepared.MetricsPrepare);
        Assert.Null(prepared.Conversation.SystemPrompt);
        Assert.True(prepared.SkipCompression);
        Assert.False(prepared.UpstreamRequest.ReplaceMessages);
        h.CacheAlignment.Verify(c => c.Invalidate(It.IsAny<Guid>()), Times.Never);

        var service = h.CreateService();
        await service.HandleAsync(SliceCTestHarness.BuildRequest("pt-metrics-svc"), CancellationToken.None);
        h.MetricsRecorder.Verify(
            m => m.RecordSuccessfulTurnAsync(It.IsAny<SuccessfulTurnMetricInput>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task PassThrough_WinsOverMonitorOnly()
    {
        var h = new SliceCTestHarness
        {
            Proxy = new() { PassThrough = true, OptimizationMode = OptimizationMode.MonitorOnly },
            Metrics = new() { Enabled = true },
            ToolSchema = new() { Mode = ToolSchemaMode.Virtual }
        };
        var preparer = h.CreatePreparer();
        var request = SliceCTestHarness.BuildRequest("pt-wins", systemContent: "Sticky MonitorOnly system text.");
        var prepared = await preparer.PrepareAsync(
            request,
            "header:pt-wins",
            _ => Task.CompletedTask,
            CancellationToken.None);

        Assert.Null(prepared.MetricsPrepare);
        Assert.True(prepared.SkipCompression);
        Assert.False(prepared.UpstreamRequest.ReplaceMessages);
        Assert.Null(prepared.Conversation.SystemPrompt);
        h.CacheAlignment.Verify(c => c.Invalidate(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task Full_BaseSystemRefresh_CacheAlignmentEnabled_Invalidates()
    {
        var now = DateTimeOffset.UtcNow;
        var conversation = Conversation.Create("header:full-bs-refresh", now);
        Assert.True(conversation.SetBaseSystem("Original base system."));

        var h = new SliceCTestHarness
        {
            Proxy = new() { OptimizationMode = OptimizationMode.Full },
            ToolSchema = new() { Mode = ToolSchemaMode.Off },
            Policy = new() { SoftLimitTokens = 10_000, CompressionRetainMessageCount = 1 },
            CacheAlignmentOptions = new() { Enabled = true, MaxConversations = 1024 }
        };
        h.SeedExistingConversation(conversation);
        var preparer = h.CreatePreparer();

        var prepared = await preparer.PrepareAsync(
            SliceCTestHarness.BuildRequest("full-bs-refresh", systemContent: "Updated base system."),
            "header:full-bs-refresh",
            _ => Task.CompletedTask,
            CancellationToken.None);

        Assert.Equal("Updated base system.", prepared.Conversation.SystemPrompt);
        h.CacheAlignment.Verify(c => c.Invalidate(conversation.Id), Times.Once);
    }

    [Fact]
    public async Task Full_BaseSystemRefresh_CacheAlignmentDisabled_StillInvalidates()
    {
        var now = DateTimeOffset.UtcNow;
        var conversation = Conversation.Create("header:full-bs-no-inv", now);
        Assert.True(conversation.SetBaseSystem("Original base system."));

        var h = new SliceCTestHarness
        {
            Proxy = new() { OptimizationMode = OptimizationMode.Full },
            ToolSchema = new() { Mode = ToolSchemaMode.Off },
            Policy = new() { SoftLimitTokens = 10_000, CompressionRetainMessageCount = 1 },
            CacheAlignmentOptions = new() { Enabled = false, MaxConversations = 1024 }
        };
        h.SeedExistingConversation(conversation);
        var preparer = h.CreatePreparer();

        var prepared = await preparer.PrepareAsync(
            SliceCTestHarness.BuildRequest("full-bs-no-inv", systemContent: "Updated base system."),
            "header:full-bs-no-inv",
            _ => Task.CompletedTask,
            CancellationToken.None);

        Assert.Equal("Updated base system.", prepared.Conversation.SystemPrompt);
        // Flag off skips materialize, but Invalidate still drops any leftover Prefix.
        h.CacheAlignment.Verify(c => c.Invalidate(conversation.Id), Times.Once);
    }
}
