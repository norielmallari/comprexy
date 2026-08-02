using System.Text.Json;
using Comprexy.Application.Abstractions;
using Comprexy.Application.Configuration;
using Comprexy.Application.Models;
using Comprexy.Application.Services;
using Comprexy.Application.Services.ToolIr;
using Comprexy.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Comprexy.Application.Tests.Services;

public class ToolIrShapeLearnerTests
{
    [Fact]
    public async Task Learner_WaitsForIdle_ThenCallsWithShapeLearnerPurpose()
    {
        var idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new Mock<IUpstreamActivityGate>(MockBehavior.Strict);
        gate.Setup(g => g.WaitForIdleAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns((TimeSpan _, CancellationToken ct) => idle.Task.WaitAsync(ct));
        gate.SetupGet(g => g.PreemptToken).Returns(CancellationToken.None);

        var options = CreateOptions();
        var store = new ToolIrResultShapeStore(options);
        var calls = 0;
        UpstreamRequestPurpose? seenPurpose = null;
        string? seenPrompt = null;
        var chat = new Mock<IChatCompletionClient>();
        chat.Setup(c => c.CompleteAsync(
                It.IsAny<ProviderEndpoint>(),
                It.IsAny<UpstreamRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProviderEndpoint _, UpstreamRequest req, CancellationToken _) =>
            {
                Interlocked.Increment(ref calls);
                seenPurpose = req.Purpose;
                seenPrompt = req.Messages.Last().Content;
                return new UpstreamChatResult(
                    """{"envelope":"tagged_content","json_field":null,"line_prefix":"none"}""",
                    "stop",
                    1,
                    1);
            });

        var scopeCalls = 0;
        var learner = CreateLearner(gate.Object, store, chat.Object, options, () => Interlocked.Increment(ref scopeCalls));
        var conversationId = Guid.NewGuid();
        var job = new ToolIrShapeLearnJob(
            conversationId, "Read", ToolSchemaConstants.FileRangeToolName, BuildPromotableSnapshot());

        var run = learner.ProcessJobAsync(job, CancellationToken.None);
        Assert.False(run.IsCompleted);
        Assert.Equal(0, calls);

        idle.SetResult();
        await run;

        Assert.Equal(1, calls);
        Assert.Equal(UpstreamRequestPurpose.ShapeLearner, seenPurpose);
        Assert.Equal(1, scopeCalls);
        Assert.DoesNotContain("SECRET_BODY_TOKEN", seenPrompt, StringComparison.Ordinal);
        Assert.True(store.TryGet(conversationId, "Read", out var shape));
        Assert.Equal(ToolIrShapeSource.Learner, shape!.Source);
    }

    [Fact]
    public async Task Learner_Preempt_DiscardsJob_CompleteJobNotPromoted()
    {
        var preemptCts = new CancellationTokenSource();
        var gate = new Mock<IUpstreamActivityGate>(MockBehavior.Strict);
        gate.Setup(g => g.WaitForIdleAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        gate.SetupGet(g => g.PreemptToken).Returns(() => preemptCts.Token);

        var options = CreateOptions();
        var store = new ToolIrResultShapeStore(options);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var hold = new TaskCompletionSource<UpstreamChatResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var chat = new Mock<IChatCompletionClient>();
        chat.Setup(c => c.CompleteAsync(
                It.IsAny<ProviderEndpoint>(),
                It.IsAny<UpstreamRequest>(),
                It.IsAny<CancellationToken>()))
            .Returns((ProviderEndpoint _, UpstreamRequest __, CancellationToken ct) =>
            {
                ct.Register(() => hold.TrySetCanceled(ct));
                started.TrySetResult();
                return hold.Task;
            });

        var learner = CreateLearner(gate.Object, store, chat.Object, options);
        var conversationId = Guid.NewGuid();
        var run = learner.ProcessJobAsync(
            new ToolIrShapeLearnJob(
                conversationId, "Read", ToolSchemaConstants.FileRangeToolName, BuildPromotableSnapshot()),
            CancellationToken.None);

        await started.Task;
        preemptCts.Cancel();
        await run;

        Assert.False(store.TryGet(conversationId, "Read", out _));
    }

    [Fact]
    public async Task Learner_InvalidProposal_NotPromoted()
    {
        var gate = IdleGate();
        var options = CreateOptions();
        var store = new ToolIrResultShapeStore(options);
        var chat = new Mock<IChatCompletionClient>();
        chat.Setup(c => c.CompleteAsync(It.IsAny<ProviderEndpoint>(), It.IsAny<UpstreamRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpstreamChatResult("""{"envelope":"not_a_real_kind","line_prefix":"none"}""", "stop", 1, 1));

        var learner = CreateLearner(gate.Object, store, chat.Object, options);
        var conversationId = Guid.NewGuid();
        await learner.ProcessJobAsync(
            new ToolIrShapeLearnJob(
                conversationId, "Read", ToolSchemaConstants.FileRangeToolName, BuildPromotableSnapshot()),
            CancellationToken.None);

        Assert.False(store.TryGet(conversationId, "Read", out _));
    }

    private static Mock<IUpstreamActivityGate> IdleGate()
    {
        var gate = new Mock<IUpstreamActivityGate>(MockBehavior.Strict);
        gate.Setup(g => g.WaitForIdleAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        gate.SetupGet(g => g.PreemptToken).Returns(CancellationToken.None);
        return gate;
    }

    private static IOptions<ToolSchemaOptions> CreateOptions() =>
        Options.Create(new ToolSchemaOptions
        {
            ResultShape = new ResultShapeOptions
            {
                MinSamplesBeforeProposal = 2,
                Learner = new ShapeLearnerOptions
                {
                    Enabled = true,
                    IdleDebounce = TimeSpan.FromSeconds(5),
                    MaxPromotionsPerConversation = 8
                }
            }
        });

    private static ToolIrShapeLearnerService CreateLearner(
        IUpstreamActivityGate gate,
        ToolIrResultShapeStore store,
        IChatCompletionClient client,
        IOptions<ToolSchemaOptions> options,
        Action? onScopeCreated = null)
    {
        var queue = new Mock<IToolIrShapeLearnQueue>(MockBehavior.Strict);
        var scopeFactory = new Mock<IServiceScopeFactory>();
        var scope = new Mock<IServiceScope>();
        var sp = new Mock<IServiceProvider>();
        sp.Setup(s => s.GetService(typeof(IChatCompletionClient))).Returns(client);
        scope.Setup(s => s.ServiceProvider).Returns(sp.Object);
        scopeFactory.Setup(f => f.CreateScope()).Returns(() =>
        {
            onScopeCreated?.Invoke();
            return scope.Object;
        });

        return new ToolIrShapeLearnerService(
            queue.Object,
            gate,
            store,
            scopeFactory.Object,
            new ProviderEndpointResolver(
                Options.Create(new ProviderOptions { BaseUrl = "http://example.test", ApiKey = "k", Model = "m" }),
                Options.Create(new CompressionOptions())),
            options,
            NullLogger<ToolIrShapeLearnerService>.Instance);
    }

    private static IReadOnlyList<ToolIrShapeFeatures> BuildPromotableSnapshot()
    {
        const string secretBodyToken = "SECRET_BODY_TOKEN";
        var tagged = $"<path>docs/a.md</path><content>hello world {secretBodyToken}</content>";
        var ambiguous = "<custom>x</custom><content>hello world</content>";
        var anchor = ToolIrShapeSanitizer.Build(
            tagged,
            ToolIrShapeConfidence.Unambiguous,
            new ToolIrResultDistiller.ExtractedFileBody("hello world", null, false),
            512)!;
        var amb = ToolIrShapeSanitizer.Build(
            ambiguous,
            ToolIrShapeConfidence.Ambiguous,
            heuristicBody: null,
            512)!;
        Assert.DoesNotContain(secretBodyToken, JsonSerializer.Serialize(anchor), StringComparison.Ordinal);
        return [anchor, amb];
    }
}
