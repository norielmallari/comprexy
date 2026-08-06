using System.Text.Json;
using Comprexy.Application.Models;
using Comprexy.Application.Services.ChatTurn;
using Comprexy.Domain.Entities;
using Comprexy.Domain.Enums;
using Moq;

namespace Comprexy.Application.Tests.Services.Settings;

public class ChatTurnCompleterMetricsGateTests
{
    [Fact]
    public async Task CompleteAsync_MetricsPrepareSet_SkipCompressionTrue_StillRecords()
    {
        var h = new SliceCTestHarness
        {
            Metrics = new() { Enabled = true }
        };
        var now = DateTimeOffset.UtcNow;
        var conversation = Conversation.Create("header:completer-gate", now);
        h.SeedExistingConversation(conversation);

        // Ensure accessor is set the way Completer SoftLimit log expects on sticky path.
        h.Accessor.Set(new EffectiveSettingsV1
        {
            SoftLimitTokens = 100,
            MetricsEnabled = true,
            OptimizationMode = OptimizationMode.MonitorOnly
        });

        var metricsPrepare = new TurnMetricsPrepareData(
            RequestStartedAt: now,
            RawInputTokensEstimated: 33,
            RequestHash: "hash-fixture",
            RawMessageCount: 2,
            WorkingMemoryVersionUsed: null,
            TrimTriggered: false,
            IrFullInputTokensEstimated: 33);

        var prepared = new PreparedRequest(
            conversation,
            NextSequence: 1,
            EstimatedTokens: 33,
            Decision: ContextBudgetDecision.ForwardImmediate,
            Endpoint: new ProviderEndpoint("http://upstream.example.test", "k", "target-model", 30),
            UpstreamRequest: new UpstreamRequest(
                [new ChatMessage(MessageRole.User, "hi")],
                Stream: false,
                OriginalClientRequest: JsonDocument.Parse("""{"model":"client-model","messages":[]}""").RootElement.Clone(),
                CallOptions: null,
                ReplaceMessages: false),
            SkipCompression: true,
            IncomingMessageCount: 1,
            WindowStartSequence: null,
            WindowEndSequence: null,
            RecentRawCount: 0,
            MetricsPrepare: metricsPrepare);

        var completer = h.CreateCompleter();
        await completer.CompleteAsync(
            prepared,
            new UpstreamChatResult("ok", "stop", 33, 4),
            new TurnPhaseTiming(0, TimeSpan.Zero, TimeSpan.Zero),
            _ => Task.CompletedTask,
            CancellationToken.None);

        h.MetricsRecorder.Verify(
            m => m.RecordSuccessfulTurnAsync(
                It.Is<SuccessfulTurnMetricInput>(i =>
                    i.RawInputTokensEstimated == 33
                    && i.CompressedInputTokensEstimated == 33
                    && i.IrFullInputTokensEstimated == 33),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
