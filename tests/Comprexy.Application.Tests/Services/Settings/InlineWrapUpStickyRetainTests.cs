using Comprexy.Application.Abstractions;
using Comprexy.Application.Configuration;
using Comprexy.Application.Models;
using Comprexy.Application.Services;
using Comprexy.Application.Services.CacheAlignment;
using Comprexy.Application.Services.ChatTurn;
using Comprexy.Application.Services.Settings;
using Comprexy.Domain.Entities;
using Comprexy.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Comprexy.Application.Tests.Services.Settings;

public class InlineWrapUpStickyRetainTests
{
    [Fact]
    public async Task RunAsync_WhenAccessorSet_SelectUsesStickyRetainNotLivePolicy()
    {
        var now = DateTimeOffset.UtcNow;
        var conversation = Conversation.Create("header:retain-wrap", now);
        var messages = Enumerable.Range(0, 5)
            .Select(i => ConversationMessage.Create(
                conversation.Id, i, MessageRole.User, $"m{i}", 5, now))
            .ToList();
        var assistant = ConversationMessage.Create(
            conversation.Id, 5, MessageRole.Assistant, "answer", 5, now);

        var messageRepo = new Mock<IConversationMessageRepository>();
        messageRepo
            .Setup(r => r.GetByConversationIdAsync(conversation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(messages.Concat([assistant]).ToList());

        var wmRepo = new Mock<IWorkingMemoryRepository>();
        wmRepo.Setup(r => r.GetLatestAsync(conversation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkingMemory?)null);
        wmRepo.Setup(r => r.Add(It.IsAny<WorkingMemory>()));

        CompressionEvent? addedEvent = null;
        var compressionRepo = new Mock<ICompressionEventRepository>();
        compressionRepo.Setup(r => r.Add(It.IsAny<CompressionEvent>()))
            .Callback<CompressionEvent>(e => addedEvent = e);

        var chat = new Mock<IChatCompletionClient>();
        chat.Setup(c => c.CompleteAsync(It.IsAny<ProviderEndpoint>(), It.IsAny<UpstreamRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpstreamChatResult(
                "# Working Memory\n## Current Goal\nInline summary",
                "stop",
                10,
                5));

        var clock = new Mock<IClock>();
        clock.Setup(c => c.UtcNow).Returns(now);

        var tokens = new Mock<ITokenEstimator>();
        tokens.Setup(t => t.CountTokens(It.IsAny<string>())).Returns(5);
        tokens.Setup(t => t.CountTokens(It.IsAny<IEnumerable<ChatMessage>>())).Returns(5);

        var livePolicy = new ContextPolicyOptions { CompressionRetainMessageCount = 50 };
        var accessor = new EffectiveSettingsAccessor();
        accessor.Set(new EffectiveSettingsV1
        {
            CompressionRetainMessageCount = 1,
            CacheAlignmentEnabled = false,
            SoftLimitTokens = 100
        });

        var runner = new InlineWrapUpRunner(
            messageRepo.Object,
            wmRepo.Object,
            compressionRepo.Object,
            new CompressionPromptFactory("inline instruction", "# Working Memory\n## Current Goal\n..."),
            chat.Object,
            tokens.Object,
            new ContextBuilder(),
            Mock.Of<ICacheAlignmentService>(),
            new RecentContextSelector(Options.Create(livePolicy)),
            Mock.Of<IConversationMetricsRecorder>(m => m.IsEnabled == false),
            clock.Object,
            accessor,
            new FixedOptionsMonitor<CacheAlignmentOptions>(new CacheAlignmentOptions { Enabled = false }),
            NullLogger<InlineWrapUpRunner>.Instance);

        var prepared = new PreparedRequest(
            conversation,
            NextSequence: 6,
            EstimatedTokens: 200,
            Decision: ContextBudgetDecision.ForwardWithHighPriorityCompression,
            Endpoint: new ProviderEndpoint("http://upstream.example.test", "k", "m", 30),
            UpstreamRequest: new UpstreamRequest(
                [new ChatMessage(MessageRole.User, "hi")],
                false,
                System.Text.Json.JsonDocument.Parse("""{"model":"m"}""").RootElement.Clone(),
                null,
                ReplaceMessages: true),
            SkipCompression: false,
            IncomingMessageCount: 6,
            WindowStartSequence: 0,
            WindowEndSequence: 5,
            RecentRawCount: 6,
            PreFollowUpEstimatedTokens: 200);

        await runner.RunAsync(
            prepared,
            new UpstreamChatResult("answer", "stop", 20, 5),
            "answer",
            assistant,
            InlineWrapUpMode.StopTurn,
            CancellationToken.None);

        Assert.NotNull(addedEvent);
        // foldUniverse = 5 users + assistant tip = 6; sticky retain 1 keeps tip assistant group only → fold 5.
        // Live retain 50 would fold 0.
        Assert.Equal(5, addedEvent!.FoldedMessageCount);
    }
}
