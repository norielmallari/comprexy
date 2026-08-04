using Comprexy.Application.Configuration;
using Comprexy.Application.Services;
using Comprexy.Application.Services.ToolIr;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Comprexy.Application.Tests.Services;

public class ToolIrResultShapeProbeTests
{
    [Theory]
    [InlineData("<path>docs/a.md</path><type>file</type><content>hello</content>", ToolIrEnvelopeKind.TaggedContent)]
    [InlineData("{\"content\":\"hello\"}", ToolIrEnvelopeKind.JsonField)]
    [InlineData("plain body text", ToolIrEnvelopeKind.Plain)]
    public void Classify_UnambiguousEnvelopes(string payload, ToolIrEnvelopeKind expected)
    {
        var (descriptor, confidence) = ToolIrResultShapeProbe.Classify(payload);
        Assert.Equal(ToolIrShapeConfidence.Unambiguous, confidence);
        Assert.Equal(expected, descriptor.Envelope);
    }

    [Fact]
    public void Classify_MultiFieldJson_IsAmbiguous()
    {
        var (_, confidence) = ToolIrResultShapeProbe.Classify("{\"content\":\"a\",\"text\":\"b\"}");
        Assert.Equal(ToolIrShapeConfidence.Ambiguous, confidence);
    }

    [Fact]
    public void Classify_ContentTagWithBadPrelude_IsAmbiguous()
    {
        var (_, confidence) = ToolIrResultShapeProbe.Classify("code: var x = \"<content>y</content>\";");
        Assert.Equal(ToolIrShapeConfidence.Ambiguous, confidence);
    }

    [Fact]
    public void EnvelopeGate_DoesNotUnwrapEmbeddedContentTags()
    {
        Assert.False(ToolIrResultDistiller.TryExtractTaggedContent(
            "void M() { var s = \"<content>nope</content>\"; }",
            out _));
    }

    [Fact]
    public void EnvelopeGate_RealEnvelope_LastCloseWins()
    {
        Assert.True(ToolIrResultDistiller.TryExtractTaggedContent(
            "<path>docs/a.md</path><content>a</content>more</content>",
            out var body));
        Assert.Contains("a</content>more", body, StringComparison.Ordinal);
    }

    [Fact]
    public void RecordSample_RequiresBothAnchorAndAmbiguous_ToEnqueue()
    {
        var options = new ToolSchemaOptions
        {
            ResultShape = new ResultShapeOptions
            {
                Learner = new ShapeLearnerOptions { Enabled = true },
                MinSamplesBeforeProposal = 2
            }
        };
        var store = new ToolIrResultShapeStore(Options.Create(options));
        var conversationId = Guid.NewGuid();

        var anchor = ToolIrShapeSanitizer.Build(
            "<path>docs/a.md</path><content>hello</content>",
            ToolIrShapeConfidence.Unambiguous,
            new ToolIrResultDistiller.ExtractedFileBody("hello", null, false),
            512)!;
        var ambiguous = ToolIrShapeSanitizer.Build(
            "{\"content\":\"a\",\"text\":\"b\"}",
            ToolIrShapeConfidence.Ambiguous,
            heuristicBody: null,
            512)!;

        var first = store.RecordSample(conversationId, "Read", anchor);
        Assert.False(first.ShouldEnqueue);

        var secondAnchor = store.RecordSample(conversationId, "Read", anchor);
        Assert.False(secondAnchor.ShouldEnqueue);

        var enqueue = store.RecordSample(conversationId, "Read", ambiguous);
        Assert.True(enqueue.ShouldEnqueue);
        Assert.Contains(enqueue.Snapshot, s => s.ObservedBody is not null);
        Assert.Contains(enqueue.Snapshot, s => s.ObservedBody is null);
    }

    [Fact]
    public void Sanitizer_DoesNotRetainPayloadTokens()
    {
        var payload =
            "<weird>SECRET_TOKEN_XYZ</weird><path>/workspace/repo/docs/a.md</path><content>body</content>";
        var features = ToolIrShapeSanitizer.Build(
            payload,
            ToolIrShapeConfidence.Unambiguous,
            new ToolIrResultDistiller.ExtractedFileBody("body", null, false),
            512);
        Assert.NotNull(features);
        var json = System.Text.Json.JsonSerializer.Serialize(features);
        Assert.DoesNotContain("SECRET_TOKEN_XYZ", json, StringComparison.Ordinal);
        Assert.DoesNotContain("/workspace/repo", json, StringComparison.Ordinal);
        Assert.DoesNotContain("weird", json, StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public void NullClientTool_SkipsProbe()
    {
        var options = Options.Create(new ToolSchemaOptions());
        using var cache = new ToolIrFileBodyCache(options);
        var store = ToolIrTestFactory.CreateShapeStore();
        var queue = ToolIrTestFactory.CreateLearnQueue();
        var distiller = new ToolIrResultDistiller(options, cache, store, queue);
        var conversationId = Guid.NewGuid();

        _ = distiller.ExtractFileBody(conversationId, null, "<path>docs/a.md</path><content>hi</content>");
        Assert.False(store.TryGet(conversationId, "Read", out _));
    }
    [Fact]
    public void Classify_TaggedPlusJson_IsAmbiguous()
    {
        var (_, confidence) = ToolIrResultShapeProbe.Classify(
            "{\"content\":\"x\"}<path>docs/a.md</path><content>y</content>");
        Assert.Equal(ToolIrShapeConfidence.Ambiguous, confidence);
    }

    [Fact]
    public void Classify_BorderlinePrefixMajority_IsAmbiguous()
    {
        // 2 prefixed, 3 non-empty → |4-3|=1 borderline with prefixes present
        var payload = "1: a\n2: b\nplain\n";
        var (_, confidence) = ToolIrResultShapeProbe.Classify(payload);
        Assert.Equal(ToolIrShapeConfidence.Ambiguous, confidence);
    }

    [Fact]
    public void RecordProbe_Unambiguous_StoresProbeSource()
    {
        var store = ToolIrTestFactory.CreateShapeStore();
        var conversationId = Guid.NewGuid();
        var (descriptor, confidence) = ToolIrResultShapeProbe.Classify(
            "<path>docs/a.md</path><type>file</type><content>hello</content>");
        Assert.Equal(ToolIrShapeConfidence.Unambiguous, confidence);
        store.RecordProbe(conversationId, "Read", descriptor);
        Assert.True(store.TryGet(conversationId, "Read", out var shape));
        Assert.Equal(ToolIrShapeSource.Probe, shape!.Source);
    }

    [Fact]
    public void RecordSample_NoWastedJobs_WithoutBothClasses()
    {
        var options = new ToolSchemaOptions
        {
            ResultShape = new ResultShapeOptions
            {
                Learner = new ShapeLearnerOptions { Enabled = true },
                MinSamplesBeforeProposal = 2
            }
        };
        var store = new ToolIrResultShapeStore(Options.Create(options));
        var conversationId = Guid.NewGuid();
        var anchor = ToolIrShapeSanitizer.Build(
            "plain ascii body here\nand more",
            ToolIrShapeConfidence.Unambiguous,
            new ToolIrResultDistiller.ExtractedFileBody("plain ascii body here\nand more", null, false),
            512)!;
        var amb = ToolIrShapeSanitizer.Build(
            "{\"content\":\"a\",\"text\":\"b\"}",
            ToolIrShapeConfidence.Ambiguous,
            null,
            512)!;

        Assert.False(store.RecordSample(conversationId, "Read", anchor).ShouldEnqueue);
        Assert.False(store.RecordSample(conversationId, "Read", anchor).ShouldEnqueue);
        Assert.False(store.RecordSample(conversationId, "Grep", amb).ShouldEnqueue);
        Assert.False(store.RecordSample(conversationId, "Grep", amb).ShouldEnqueue);
    }

    [Fact]
    public void SamplingStops_AfterPromoteAndAttemptExhaustion()
    {
        var options = new ToolSchemaOptions
        {
            ResultShape = new ResultShapeOptions
            {
                MaxProposalAttemptsPerKey = 1,
                MinSamplesBeforeProposal = 2,
                Learner = new ShapeLearnerOptions { Enabled = true, MaxPromotionsPerConversation = 8 }
            }
        };
        var store = new ToolIrResultShapeStore(Options.Create(options));
        var conversationId = Guid.NewGuid();
        store.Promote((conversationId, "Read"), new ToolIrResultShape
        {
            Envelope = ToolIrEnvelopeKind.Plain,
            LinePrefix = ToolIrLinePrefixStyle.None,
            Source = ToolIrShapeSource.Learner,
            ObservedAt = DateTimeOffset.UtcNow
        });
        Assert.False(store.ShouldSample(conversationId, "Read"));

        var amb = ToolIrShapeSanitizer.Build("{\"content\":\"a\",\"text\":\"b\"}", ToolIrShapeConfidence.Ambiguous, null, 512)!;
        var anchor = ToolIrShapeSanitizer.Build(
            "plain\ntext\nlines",
            ToolIrShapeConfidence.Unambiguous,
            new ToolIrResultDistiller.ExtractedFileBody("plain\ntext\nlines", null, false),
            512)!;
        store.RecordSample(conversationId, "Grep", anchor);
        var enq = store.RecordSample(conversationId, "Grep", amb);
        Assert.True(enq.ShouldEnqueue);
        store.CompleteJob((conversationId, "Grep"), promoted: false);
        Assert.False(store.ShouldSample(conversationId, "Grep"));
    }

    [Fact]
    public void StoredDescriptor_DrivesAmbiguous_NotUnambiguous_AndDemotesOnMismatch()
    {
        var options = Options.Create(new ToolSchemaOptions());
        using var cache = new ToolIrFileBodyCache(options);
        var store = ToolIrTestFactory.CreateShapeStore();
        var queue = ToolIrTestFactory.CreateLearnQueue();
        var distiller = new ToolIrResultDistiller(options, cache, store, queue);
        var conversationId = Guid.NewGuid();
        store.RecordProbe(conversationId, "Read", new ToolIrResultShape
        {
            Envelope = ToolIrEnvelopeKind.JsonField,
            JsonField = ToolIrJsonFieldToken.Text,
            LinePrefix = ToolIrLinePrefixStyle.None,
            Source = ToolIrShapeSource.Probe,
            ObservedAt = DateTimeOffset.UtcNow
        });

        var amb = distiller.ExtractFileBody(conversationId, "Read", "{\"content\":\"from-content\",\"text\":\"from-text\"}");
        Assert.Equal("from-text", amb.Body);

        var unambiguous = distiller.ExtractFileBody(conversationId, "Read", "{\"content\":\"only-content-field\"}");
        Assert.Equal("only-content-field", unambiguous.Body);

        store.Promote((conversationId, "Read"), new ToolIrResultShape
        {
            Envelope = ToolIrEnvelopeKind.TaggedContent,
            LinePrefix = ToolIrLinePrefixStyle.None,
            Source = ToolIrShapeSource.Learner,
            ObservedAt = DateTimeOffset.UtcNow
        });
        _ = distiller.ExtractFileBody(conversationId, "Read", "{\"content\":\"a\",\"text\":\"b\"}");
        Assert.False(store.TryGet(conversationId, "Read", out _));
    }

    [Fact]
    public void StoreBounds_LruAndRingAndLearnerOff()
    {
        var options = new ToolSchemaOptions
        {
            ResultShape = new ResultShapeOptions
            {
                MaxConversations = 2,
                MaxSamplesRetained = 2,
                Learner = new ShapeLearnerOptions { Enabled = true }
            }
        };
        var store = new ToolIrResultShapeStore(Options.Create(options));
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        store.RecordProbe(a, "Read", new ToolIrResultShape { Envelope = ToolIrEnvelopeKind.Plain, LinePrefix = ToolIrLinePrefixStyle.None, Source = ToolIrShapeSource.Probe, ObservedAt = DateTimeOffset.UtcNow });
        store.RecordProbe(b, "Read", new ToolIrResultShape { Envelope = ToolIrEnvelopeKind.Plain, LinePrefix = ToolIrLinePrefixStyle.None, Source = ToolIrShapeSource.Probe, ObservedAt = DateTimeOffset.UtcNow });
        store.RecordProbe(c, "Read", new ToolIrResultShape { Envelope = ToolIrEnvelopeKind.Plain, LinePrefix = ToolIrLinePrefixStyle.None, Source = ToolIrShapeSource.Probe, ObservedAt = DateTimeOffset.UtcNow });
        Assert.False(store.TryGet(a, "Read", out _));
        Assert.True(store.TryGet(c, "Read", out _));

        var off = new ToolIrResultShapeStore(Options.Create(new ToolSchemaOptions
        {
            ResultShape = new ResultShapeOptions
            {
                Learner = new ShapeLearnerOptions { Enabled = false }
            }
        }));
        Assert.False(off.ShouldSample(Guid.NewGuid(), "Read"));
    }
}

public class UpstreamActivityGateTests
{
    [Fact]
    public async Task WaitForIdle_CompletesOnFreshGate_AfterDebounce()
    {
        var time = new FakeTimeProvider();
        var gate = new UpstreamActivityGate(time);
        var wait = gate.WaitForIdleAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        Assert.False(wait.IsCompleted);
        await AdvanceUntilCompletedAsync(time, wait, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void BeginClientDrivenCall_SetsBusy_AndDoubleDisposeIsSafe()
    {
        var gate = new UpstreamActivityGate(TimeProvider.System);
        var lease = gate.BeginClientDrivenCall();
        Assert.True(gate.IsBusy);
        lease.Dispose();
        lease.Dispose();
        Assert.False(gate.IsBusy);
    }

    [Fact]
    public async Task Decorator_ReleaseOnThrow_CompleteAndStream()
    {
        var gate = new UpstreamActivityGate(TimeProvider.System);
        var inner = new ThrowingChatClient();
        var decorator = new UpstreamActivityTrackingChatCompletionClient(inner, gate);
        var endpoint = new Models.ProviderEndpoint("http://example.test", "k", "m", 30);
        var request = new Models.UpstreamRequest([], Stream: false);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            decorator.CompleteAsync(endpoint, request, CancellationToken.None));
        Assert.False(gate.IsBusy);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            decorator.StreamAsync(endpoint, request, (_, _) => Task.CompletedTask, CancellationToken.None));
        Assert.False(gate.IsBusy);
    }

    [Fact]
    public static async Task ShapeLearnerPurpose_DoesNotSetBusy()
    {
        var gate = new UpstreamActivityGate(TimeProvider.System);
        var inner = new NoopChatClient();
        var decorator = new UpstreamActivityTrackingChatCompletionClient(inner, gate);
        var endpoint = new Models.ProviderEndpoint("http://example.test", "k", "m", 30);
        var request = new Models.UpstreamRequest(
            [],
            Stream: false,
            Purpose: Models.UpstreamRequestPurpose.ShapeLearner);

        await decorator.CompleteAsync(endpoint, request, CancellationToken.None);
        Assert.False(gate.IsBusy);
    }

    private sealed class ThrowingChatClient : Abstractions.IChatCompletionClient
    {
        public Task<Models.UpstreamChatResult> CompleteAsync(
            Models.ProviderEndpoint endpoint,
            Models.UpstreamRequest request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("boom");

        public Task<Models.UpstreamChatResult> StreamAsync(
            Models.ProviderEndpoint endpoint,
            Models.UpstreamRequest request,
            Func<string, CancellationToken, Task> onRawSseData,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("boom");
    }

    private sealed class NoopChatClient : Abstractions.IChatCompletionClient
    {
        public Task<Models.UpstreamChatResult> CompleteAsync(
            Models.ProviderEndpoint endpoint,
            Models.UpstreamRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new Models.UpstreamChatResult("ok", "stop", 1, 1));

        public Task<Models.UpstreamChatResult> StreamAsync(
            Models.ProviderEndpoint endpoint,
            Models.UpstreamRequest request,
            Func<string, CancellationToken, Task> onRawSseData,
            CancellationToken cancellationToken) =>
            Task.FromResult(new Models.UpstreamChatResult("ok", "stop", 1, 1));
    }

    [Fact]
    public void PreemptToken_CancelledOnBusy_AndFreshAfterIdle()
    {
        var gate = new UpstreamActivityGate(TimeProvider.System);
        var token = gate.PreemptToken;
        Assert.False(token.IsCancellationRequested);
        using (gate.BeginClientDrivenCall())
        {
            Assert.True(token.IsCancellationRequested);
        }

        var fresh = gate.PreemptToken;
        Assert.False(fresh.IsCancellationRequested);
    }

    [Fact]
    public async Task ReadDuringSwap_NeverThrowsObjectDisposed()
    {
        var gate = new UpstreamActivityGate(TimeProvider.System);
        var errors = 0;
        using var stop = new CancellationTokenSource();
        var reader = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    _ = gate.PreemptToken.IsCancellationRequested;
                }
                catch (ObjectDisposedException)
                {
                    Interlocked.Increment(ref errors);
                }
            }
        });

        for (var i = 0; i < 200; i++)
        {
            using var lease = gate.BeginClientDrivenCall();
        }

        await stop.CancelAsync();
        await reader;
        Assert.Equal(0, errors);
    }

    [Fact]
    public async Task WaitForIdle_RestartsDebounce_WhenLeaseTakenMidWindow()
    {
        var time = new FakeTimeProvider();
        var gate = new UpstreamActivityGate(time);
        var wait = gate.WaitForIdleAsync(TimeSpan.FromSeconds(5), CancellationToken.None);

        await AdvanceFakeAsync(time, TimeSpan.FromSeconds(2));
        Assert.False(wait.IsCompleted);

        using (gate.BeginClientDrivenCall())
        {
            await AdvanceFakeAsync(time, TimeSpan.FromSeconds(10));
            Assert.False(wait.IsCompleted);
        }

        await AdvanceUntilCompletedAsync(time, wait, TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Yield so <c>Task.Delay(..., FakeTimeProvider)</c> can register, then advance once.
    /// No wall-clock sleep.
    /// </summary>
    private static async Task AdvanceFakeAsync(FakeTimeProvider time, TimeSpan step)
    {
        for (var i = 0; i < 16; i++)
        {
            await Task.Yield();
        }

        time.Advance(step);
    }

    private static async Task AdvanceUntilCompletedAsync(FakeTimeProvider time, Task wait, TimeSpan step)
    {
        for (var i = 0; i < 64; i++)
        {
            if (wait.IsCompleted)
            {
                await wait;
                return;
            }

            await AdvanceFakeAsync(time, step);
        }

        Assert.True(wait.IsCompleted, "WaitForIdleAsync did not complete after fake-time advances.");
        await wait;
    }
}

public class ToolIrShapeProposalValidatorTests
{

    [Fact]
    public void Validate_RejectsUnknownEnvelope()
    {
        var ok = ToolIrShapeProposalValidator.Validate(
            "{\"envelope\":\"regex_shaped\",\"line_prefix\":\"none\"}",
            [MinimalAnchor()],
            new ResultShapeOptions { MinSamplesBeforeProposal = 1 },
            out _,
            out var reason);
        Assert.False(ok);
        Assert.Equal("not_closed_set", reason);
    }

    [Fact]
    public void Validate_PositivePath_PromotesWhenAnchorAndAmbiguousAgree()
    {
        var tagged = "<path>docs/a.md</path><content>hello world</content>";
        var ambiguous = "<custom>x</custom><content>hello world</content>";
        var anchorFeatures = ToolIrShapeSanitizer.Build(
            tagged,
            ToolIrShapeConfidence.Unambiguous,
            new ToolIrResultDistiller.ExtractedFileBody("hello world", null, false),
            512)!;
        var ambiguousFeatures = ToolIrShapeSanitizer.Build(
            ambiguous,
            ToolIrShapeConfidence.Ambiguous,
            heuristicBody: null,
            512)!;

        var proposal =
            "{\"envelope\":\"tagged_content\",\"json_field\":null,\"line_prefix\":\"none\"}";
        var ok = ToolIrShapeProposalValidator.Validate(
            proposal,
            [anchorFeatures, ambiguousFeatures],
            new ResultShapeOptions { MinSamplesBeforeProposal = 2 },
            out var descriptor,
            out var reason);

        Assert.True(ok, reason);
        Assert.NotNull(descriptor);
        Assert.Equal(ToolIrEnvelopeKind.TaggedContent, descriptor!.Envelope);
        Assert.Equal(ToolIrShapeSource.Learner, descriptor.Source);
    }

    private static ToolIrShapeFeatures MinimalAnchor()
    {
        return ToolIrShapeSanitizer.Build(
            "plain",
            ToolIrShapeConfidence.Unambiguous,
            new ToolIrResultDistiller.ExtractedFileBody("plain", null, false),
            512)!;
    }

    [Fact]
    public void Validate_SampleFloor_AndNoAnchor()
    {
        var amb = ToolIrShapeSanitizer.Build("{\"content\":\"a\",\"text\":\"b\"}", ToolIrShapeConfidence.Ambiguous, null, 512)!;
        Assert.False(ToolIrShapeProposalValidator.Validate(
            "{\"envelope\":\"plain\",\"line_prefix\":\"none\"}",
            [amb],
            new ResultShapeOptions { MinSamplesBeforeProposal = 2 },
            out _,
            out var floor));
        Assert.Equal("sample_floor", floor);

        Assert.False(ToolIrShapeProposalValidator.Validate(
            "{\"envelope\":\"plain\",\"line_prefix\":\"none\"}",
            [amb, amb],
            new ResultShapeOptions { MinSamplesBeforeProposal = 2 },
            out _,
            out var noAnchor));
        Assert.Equal("no_anchor_sample", noAnchor);
    }

    [Fact]
    public void Validate_NoAmbiguityResolved()
    {
        var anchor = ToolIrShapeSanitizer.Build(
            "plain body only",
            ToolIrShapeConfidence.Unambiguous,
            new ToolIrResultDistiller.ExtractedFileBody("plain body only", null, false),
            512)!;
        // Ambiguous sample that cannot replay as plain with length>0 meaningfully — use empty-ish
        var amb = ToolIrShapeSanitizer.Build("{}", ToolIrShapeConfidence.Ambiguous, null, 512)!;
        // Proposal tagged_content will fail anchors; use plain which replays anchors but ambiguous {} may have length 2
        var ok = ToolIrShapeProposalValidator.Validate(
            "{\"envelope\":\"json_field\",\"json_field\":\"content\",\"line_prefix\":\"none\"}",
            [anchor, amb],
            new ResultShapeOptions { MinSamplesBeforeProposal = 2 },
            out _,
            out var reason);
        Assert.False(ok);
        Assert.Contains(reason, new[] { "replay_mismatch", "no_ambiguity_resolved", "not_attested", "not_closed_set" });
    }

    [Fact]
    public void TryExtractBody_NonePrefix_RejectsPrefixedPayload_LikeTryReplaySpan()
    {
        // Outer payload lines carry colon prefixes so live extract and feature replay agree.
        var payload = "1: alpha\n2: beta\n3: gamma";
        var noneDescriptor = new ToolIrResultShape
        {
            Envelope = ToolIrEnvelopeKind.Plain,
            LinePrefix = ToolIrLinePrefixStyle.None,
            Source = ToolIrShapeSource.Probe,
            ObservedAt = DateTimeOffset.UtcNow
        };
        var colonDescriptor = new ToolIrResultShape
        {
            Envelope = ToolIrEnvelopeKind.Plain,
            LinePrefix = ToolIrLinePrefixStyle.Colon,
            Source = ToolIrShapeSource.Probe,
            ObservedAt = DateTimeOffset.UtcNow
        };

        Assert.False(ToolIrResultShape.TryExtractBody(payload, noneDescriptor, out _, out _));
        Assert.True(ToolIrResultShape.TryExtractBody(payload, colonDescriptor, out var body, out var firstLine));
        Assert.Equal("alpha\nbeta\ngamma", body);
        Assert.Equal(1, firstLine);

        var features = ToolIrShapeSanitizer.Build(
            payload,
            ToolIrShapeConfidence.Unambiguous,
            new ToolIrResultDistiller.ExtractedFileBody("alpha\nbeta\ngamma", 1, true),
            512)!;
        Assert.False(ToolIrResultShape.TryReplaySpan(features, noneDescriptor, out _, out var reason));
        Assert.Equal("prefix_disagrees_with_features", reason);
        Assert.True(ToolIrResultShape.TryReplaySpan(features, colonDescriptor, out _, out _));
    }

    [Fact]
    public void TryReplaySpan_TaggedContent_UsesLastClose()
    {
        var payload = "<path>docs/a.md</path><content>a</content>more</content>";
        var features = ToolIrShapeSanitizer.Build(
            payload,
            ToolIrShapeConfidence.Unambiguous,
            new ToolIrResultDistiller.ExtractedFileBody("a</content>more", null, false),
            512)!;
        var descriptor = new ToolIrResultShape
        {
            Envelope = ToolIrEnvelopeKind.TaggedContent,
            LinePrefix = ToolIrLinePrefixStyle.None,
            Source = ToolIrShapeSource.Probe,
            ObservedAt = DateTimeOffset.UtcNow
        };
        Assert.True(ToolIrResultShape.TryReplaySpan(features, descriptor, out var span, out _));
        Assert.Equal(features.ObservedBody!.Value.Start, span.Start);
        Assert.Equal(features.ObservedBody!.Value.Length, span.Length);
    }

    [Fact]
    public void RoundTrip_ObservedBody_AsciiAndMultiByte()
    {
        RoundTrip("hello plain ascii text\nline2");
        RoundTrip("前置—🚀<body>", "<path>docs/a.md</path><content>前置—🚀body</content>", ToolIrEnvelopeKind.TaggedContent);
        RoundTrip("前置—🚀json", "{\"content\":\"前置—🚀json\"}", ToolIrEnvelopeKind.JsonField);
    }

    private static void RoundTrip(string plain) =>
        RoundTrip(plain, plain, ToolIrEnvelopeKind.Plain);

    private static void RoundTrip(string body, string payload, ToolIrEnvelopeKind envelope)
    {
        var (descriptor, confidence) = ToolIrResultShapeProbe.Classify(payload);
        if (confidence == ToolIrShapeConfidence.Ambiguous)
        {
            descriptor = new ToolIrResultShape
            {
                Envelope = envelope,
                JsonField = envelope == ToolIrEnvelopeKind.JsonField ? ToolIrJsonFieldToken.Content : null,
                LinePrefix = ToolIrLinePrefixStyle.None,
                Source = ToolIrShapeSource.Probe,
                ObservedAt = DateTimeOffset.UtcNow
            };
        }

        var features = ToolIrShapeSanitizer.Build(
            payload,
            ToolIrShapeConfidence.Unambiguous,
            new ToolIrResultDistiller.ExtractedFileBody(body, null, false),
            512);
        Assert.NotNull(features);
        Assert.True(ToolIrResultShape.TryReplaySpan(features!, descriptor, out var span, out var reason), reason);
        Assert.Equal(features!.ObservedBody!.Value, span);
    }
}
