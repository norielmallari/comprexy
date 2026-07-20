using System.Text.Json;
using Comprexy.Application.Abstractions;
using Comprexy.Application.Configuration;
using Comprexy.Application.Models;
using Comprexy.Application.Services;
using Comprexy.Domain.Entities;
using Comprexy.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Comprexy.Application.Tests.Services;

public class ProxyChatCompletionServiceTests
{
    private readonly Mock<IConversationRepository> _conversationRepository = new();
    private readonly Mock<IConversationMessageRepository> _messageRepository = new();
    private readonly Mock<IWorkingMemoryRepository> _workingMemoryRepository = new();
    private readonly Mock<ITokenEstimator> _tokenEstimator = new();
    private readonly Mock<IChatCompletionClient> _chatCompletionClient = new();
    private readonly Mock<ICompressionQueue> _compressionQueue = new();
    private readonly Mock<ICompressionOrchestrator> _compressionOrchestrator = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IClock> _clock = new();

    private readonly ContextPolicyOptions _policy = new()
    {
        SoftLimitTokens = 100,
        HardLimitTokens = 200
    };

    private ProxyOptions _proxyOptions = new();

    private int _estimatedTokensToReturn = 10;

    private ProxyChatCompletionService CreateService()
    {
        _clock.Setup(c => c.UtcNow).Returns(DateTimeOffset.UtcNow);
        _tokenEstimator.Setup(t => t.CountTokens(It.IsAny<string>())).Returns(5);
        _tokenEstimator.Setup(t => t.CountTokens(It.IsAny<IEnumerable<ChatMessage>>())).Returns(() => _estimatedTokensToReturn);
        _tokenEstimator.Setup(t => t.CountPromptTokens(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<JsonElement?>()))
            .Returns(() => _estimatedTokensToReturn);

        return new ProxyChatCompletionService(
            new ConversationIdentityResolver(),
            new ConversationRequestGate(),
            _conversationRepository.Object,
            _messageRepository.Object,
            _workingMemoryRepository.Object,
            _tokenEstimator.Object,
            new ContextBuilder(),
            new ContextBudgetEvaluator(Options.Create(_policy)),
            new RecentContextSelector(Options.Create(_policy)),
            new ProviderEndpointResolver(
                Options.Create(new ProviderOptions { BaseUrl = "http://upstream", ApiKey = "k", Model = "target-model" }),
                Options.Create(new CompressionOptions())),
            _chatCompletionClient.Object,
            _compressionQueue.Object,
            _compressionOrchestrator.Object,
            _unitOfWork.Object,
            _clock.Object,
            Options.Create(_policy),
            Options.Create(_proxyOptions),
            Mock.Of<IPayloadTraceLogger>(),
            NullLogger<ProxyChatCompletionService>.Instance);
    }

    private static IncomingChatRequest BuildRequest(
        string conversationHeader = "conv-1",
        string userContent = "Hello",
        bool stream = false)
    {
        var payload = new
        {
            model = "client-model",
            stream,
            temperature = 0.2,
            tools = new object[]
            {
                new { type = "function", function = new { name = "lookup" } }
            },
            messages = new object[]
            {
                new { role = "system", content = "You are helpful." },
                new { role = "user", content = userContent }
            }
        };

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        return Comprexy.Api.Mapping.ChatCompletionRequestParser.Parse(document.RootElement.Clone(), conversationHeader);
    }

    [Fact]
    public async Task HandleAsync_NewConversation_PersistsMessagesAndForwardsToUpstream()
    {
        _estimatedTokensToReturn = 10;
        _conversationRepository.Setup(r => r.FindByKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Conversation?)null);
        _workingMemoryRepository.Setup(r => r.GetLatestAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkingMemory?)null);
        _chatCompletionClient
            .Setup(c => c.CompleteAsync(It.IsAny<ProviderEndpoint>(), It.IsAny<UpstreamRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpstreamChatResult("Hi there!", "stop", 42, 7, """{"id":"raw"}"""));

        var service = CreateService();
        var result = await service.HandleAsync(BuildRequest(), CancellationToken.None);

        Assert.Equal("Hi there!", result.AssistantContent);
        Assert.Equal("target-model", result.Model);
        Assert.Equal(7, result.CompletionTokens);
        Assert.Equal("""{"id":"raw"}""", result.RawResponseJson);

        _conversationRepository.Verify(r => r.Add(It.IsAny<Conversation>()), Times.Once);
        _messageRepository.Verify(r => r.Add(It.IsAny<ConversationMessage>()), Times.Exactly(2));
        _compressionQueue.Verify(q => q.Enqueue(It.IsAny<CompressionJob>()), Times.Never);
        _compressionOrchestrator.Verify(
            o => o.RunAsync(It.IsAny<Guid>(), It.IsAny<CompressionMode>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_SameConversationHeaderTwice_ReusesExistingConversation()
    {
        var conversation = Conversation.Create("header:conv-1", DateTimeOffset.UtcNow);
        _estimatedTokensToReturn = 10;

        _conversationRepository.Setup(r => r.FindByKeyAsync("header:conv-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(conversation);
        _messageRepository.Setup(r => r.GetByConversationIdAsync(conversation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _workingMemoryRepository.Setup(r => r.GetLatestAsync(conversation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkingMemory?)null);
        _chatCompletionClient
            .Setup(c => c.CompleteAsync(It.IsAny<ProviderEndpoint>(), It.IsAny<UpstreamRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpstreamChatResult("ack", "stop", 10, 2));

        var service = CreateService();
        await service.HandleAsync(BuildRequest(), CancellationToken.None);

        _conversationRepository.Verify(r => r.Add(It.IsAny<Conversation>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_EstimatedTokensAboveSoftLimit_EnqueuesHighPriorityCompression()
    {
        _estimatedTokensToReturn = 150;
        _conversationRepository.Setup(r => r.FindByKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Conversation?)null);
        _workingMemoryRepository.Setup(r => r.GetLatestAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkingMemory?)null);
        _chatCompletionClient
            .Setup(c => c.CompleteAsync(It.IsAny<ProviderEndpoint>(), It.IsAny<UpstreamRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpstreamChatResult("ack", "stop", 10, 2));

        var service = CreateService();
        await service.HandleAsync(BuildRequest(), CancellationToken.None);

        _compressionQueue.Verify(
            q => q.Enqueue(It.Is<CompressionJob>(j => j.Mode == CompressionMode.HighPriorityBackground)), Times.Once);
        _compressionOrchestrator.Verify(
            o => o.RunAsync(It.IsAny<Guid>(), It.IsAny<CompressionMode>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_AboveSoftLimit_WithOpenToolCalls_DoesNotEnqueueCompression()
    {
        _estimatedTokensToReturn = 150;
        _conversationRepository.Setup(r => r.FindByKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Conversation?)null);
        _workingMemoryRepository.Setup(r => r.GetLatestAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkingMemory?)null);

        const string assistantMessageJson = """
            {"role":"assistant","content":null,"tool_calls":[{"id":"call_1","type":"function","function":{"name":"read_file","arguments":"{\"path\":\"a.cs\"}"}}]}
            """;
        _chatCompletionClient
            .Setup(c => c.CompleteAsync(It.IsAny<ProviderEndpoint>(), It.IsAny<UpstreamRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpstreamChatResult(
                Content: string.Empty,
                FinishReason: "tool_calls",
                PromptTokens: 10,
                CompletionTokens: 5,
                RawResponseJson: """{"id":"x"}""",
                AssistantMessageJson: assistantMessageJson));

        var service = CreateService();
        await service.HandleAsync(BuildRequest(), CancellationToken.None);

        _compressionQueue.Verify(q => q.Enqueue(It.IsAny<CompressionJob>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_EstimatedTokensAboveHardLimit_EmergencyOffWithoutWorkingMemory_ThrowsWithoutSyncCompact()
    {
        _policy.EmergencyCompression = EmergencyCompressionMode.Off;
        _estimatedTokensToReturn = 250;
        _conversationRepository.Setup(r => r.FindByKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Conversation?)null);
        _workingMemoryRepository.Setup(r => r.GetLatestAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkingMemory?)null);
        _messageRepository.Setup(r => r.GetByConversationIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _chatCompletionClient
            .Setup(c => c.CompleteAsync(It.IsAny<ProviderEndpoint>(), It.IsAny<UpstreamRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpstreamChatResult("ack", "stop", 10, 2));

        var service = CreateService();
        await Assert.ThrowsAsync<Comprexy.Application.Exceptions.ContextBudgetExceededException>(
            () => service.HandleAsync(BuildRequest(), CancellationToken.None));

        _compressionOrchestrator.Verify(
            o => o.RunAsync(It.IsAny<Guid>(), CompressionMode.Emergency, It.IsAny<CancellationToken>()), Times.Never);
        _chatCompletionClient.Verify(
            c => c.CompleteAsync(It.IsAny<ProviderEndpoint>(), It.IsAny<UpstreamRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _compressionQueue.Verify(q => q.Enqueue(It.IsAny<CompressionJob>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_EstimatedTokensAboveHardLimit_EmergencySyncWithoutWorkingMemory_RunsThenThrowsIfStillNoMemory()
    {
        _policy.EmergencyCompression = EmergencyCompressionMode.Sync;
        _estimatedTokensToReturn = 250;
        _conversationRepository.Setup(r => r.FindByKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Conversation?)null);
        _workingMemoryRepository.Setup(r => r.GetLatestAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkingMemory?)null);
        _messageRepository.Setup(r => r.GetByConversationIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _chatCompletionClient
            .Setup(c => c.CompleteAsync(It.IsAny<ProviderEndpoint>(), It.IsAny<UpstreamRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpstreamChatResult("ack", "stop", 10, 2));

        var service = CreateService();
        await Assert.ThrowsAsync<Comprexy.Application.Exceptions.ContextBudgetExceededException>(
            () => service.HandleAsync(BuildRequest(), CancellationToken.None));

        _compressionOrchestrator.Verify(
            o => o.RunAsync(It.IsAny<Guid>(), CompressionMode.Emergency, It.IsAny<CancellationToken>()), Times.Once);
        _chatCompletionClient.Verify(
            c => c.CompleteAsync(It.IsAny<ProviderEndpoint>(), It.IsAny<UpstreamRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _compressionQueue.Verify(q => q.Enqueue(It.IsAny<CompressionJob>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_NoMessages_ThrowsArgumentException()
    {
        var service = CreateService();
        using var document = JsonDocument.Parse("""{"messages":[]}""");
        var request = new IncomingChatRequest([], null, false, document.RootElement.Clone(), new ChatCompletionCallOptions());

        await Assert.ThrowsAsync<ArgumentException>(() => service.HandleAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task HandleStreamingAsync_ForwardsRawChunksAndPersistsCompletedAssistantMessage()
    {
        _conversationRepository.Setup(r => r.FindByKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Conversation?)null);
        _workingMemoryRepository.Setup(r => r.GetLatestAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkingMemory?)null);
        _chatCompletionClient
            .Setup(c => c.StreamAsync(
                It.IsAny<ProviderEndpoint>(),
                It.IsAny<UpstreamRequest>(),
                It.IsAny<Func<string, CancellationToken, Task>>(),
                It.IsAny<CancellationToken>()))
            .Returns(
                async (
                    ProviderEndpoint _,
                    UpstreamRequest _,
                    Func<string, CancellationToken, Task> onRawSseData,
                    CancellationToken token) =>
                {
                    await onRawSseData("""{"choices":[{"delta":{"content":"Hello"}}]}""", token);
                    await onRawSseData("""{"choices":[{"delta":{"content":" world"},"finish_reason":"stop"}]}""", token);
                    await onRawSseData("[DONE]", token);
                    return new UpstreamChatResult("Hello world", "stop", 10, 2);
                });

        var chunks = new List<string>();
        ConversationMessage? assistantMessage = null;
        _messageRepository
            .Setup(r => r.Add(It.IsAny<ConversationMessage>()))
            .Callback<ConversationMessage>(message =>
            {
                if (message.Role == MessageRole.Assistant)
                {
                    assistantMessage = message;
                }
            });

        var service = CreateService();
        Guid? conversationId = null;
        var result = await service.HandleStreamingAsync(
            BuildRequest(stream: true),
            id => conversationId = id,
            (chunk, _) =>
            {
                chunks.Add(chunk);
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.Equal("Hello world", result.AssistantContent);
        Assert.NotNull(conversationId);
        Assert.Equal(3, chunks.Count);
        Assert.Contains("Hello", chunks[0]);
        Assert.Equal("[DONE]", chunks[2]);
        Assert.NotNull(assistantMessage);
        Assert.Equal("Hello world", assistantMessage!.Content);
        _compressionQueue.Verify(q => q.Enqueue(It.IsAny<CompressionJob>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_ToolCallAssistant_PersistsWireJsonEvenWhenContentEmpty()
    {
        _conversationRepository.Setup(r => r.FindByKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Conversation?)null);
        _workingMemoryRepository.Setup(r => r.GetLatestAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkingMemory?)null);

        const string assistantMessageJson = """
            {"role":"assistant","content":null,"tool_calls":[{"id":"call_1","type":"function","function":{"name":"read_file","arguments":"{\"path\":\"a.cs\"}"}}]}
            """;
        _chatCompletionClient
            .Setup(c => c.CompleteAsync(It.IsAny<ProviderEndpoint>(), It.IsAny<UpstreamRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpstreamChatResult(
                Content: string.Empty,
                FinishReason: "tool_calls",
                PromptTokens: 10,
                CompletionTokens: 5,
                RawResponseJson: """{"id":"x"}""",
                AssistantMessageJson: assistantMessageJson));

        ConversationMessage? assistantMessage = null;
        _messageRepository
            .Setup(r => r.Add(It.IsAny<ConversationMessage>()))
            .Callback<ConversationMessage>(message =>
            {
                if (message.Role == MessageRole.Assistant)
                {
                    assistantMessage = message;
                }
            });

        var service = CreateService();
        await service.HandleAsync(BuildRequest(), CancellationToken.None);

        Assert.NotNull(assistantMessage);
        Assert.Equal(assistantMessageJson, assistantMessage!.RawWireJson);
        Assert.Contains("read_file", assistantMessage.Content);
        Assert.Contains("tool_calls", assistantMessage.Content);
    }

    [Fact]
    public async Task HandleAsync_PassThrough_PreservesOriginalRequestAndSkipsCompression()
    {
        _proxyOptions = new ProxyOptions { PassThrough = true };
        _estimatedTokensToReturn = 250;

        UpstreamRequest? forwarded = null;
        _conversationRepository.Setup(r => r.FindByKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Conversation?)null);
        _chatCompletionClient
            .Setup(c => c.CompleteAsync(
                It.IsAny<ProviderEndpoint>(),
                It.IsAny<UpstreamRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<ProviderEndpoint, UpstreamRequest, CancellationToken>((_, request, _) => forwarded = request)
            .ReturnsAsync(new UpstreamChatResult("passthrough-ack", "stop", 10, 2, """{"ok":true}"""));

        var request = BuildRequest();
        var service = CreateService();
        var result = await service.HandleAsync(request, CancellationToken.None);

        Assert.Equal("passthrough-ack", result.AssistantContent);
        Assert.NotNull(forwarded);
        Assert.False(forwarded!.ReplaceMessages);
        Assert.Equal(UpstreamRequestPurpose.Chat, forwarded.Purpose);
        Assert.True(forwarded.OriginalClientRequest.HasValue);
        Assert.True(forwarded.OriginalClientRequest!.Value.TryGetProperty("tools", out _));
        Assert.True(forwarded.OriginalClientRequest.Value.TryGetProperty("temperature", out _));

        _workingMemoryRepository.Verify(r => r.GetLatestAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _compressionOrchestrator.Verify(
            o => o.RunAsync(It.IsAny<Guid>(), It.IsAny<CompressionMode>(), It.IsAny<CancellationToken>()), Times.Never);
        _compressionQueue.Verify(q => q.Enqueue(It.IsAny<CompressionJob>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_BeforeFirstCompression_ForwardsFullClientMessages()
    {
        UpstreamRequest? forwarded = null;
        _conversationRepository.Setup(r => r.FindByKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Conversation?)null);
        _workingMemoryRepository.Setup(r => r.GetLatestAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkingMemory?)null);
        _chatCompletionClient
            .Setup(c => c.CompleteAsync(It.IsAny<ProviderEndpoint>(), It.IsAny<UpstreamRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ProviderEndpoint, UpstreamRequest, CancellationToken>((_, request, _) => forwarded = request)
            .ReturnsAsync(new UpstreamChatResult("ack", "stop", 10, 2));

        var request = BuildRequest();
        var service = CreateService();
        await service.HandleAsync(request, CancellationToken.None);

        Assert.NotNull(forwarded);
        Assert.False(forwarded!.ReplaceMessages);
        Assert.Equal(request.Messages, forwarded.Messages);
        Assert.Equal(UpstreamRequestPurpose.Chat, forwarded.Purpose);
        Assert.True(forwarded.OriginalClientRequest.HasValue);
        Assert.True(forwarded.OriginalClientRequest!.Value.TryGetProperty("tools", out _));
    }

    [Fact]
    public async Task HandleAsync_AfterWorkingMemoryExists_IncludesAllUnfoldedMessagesNotJustRecentWindow()
    {
        UpstreamRequest? forwarded = null;
        var conversation = Conversation.Create("header:conv-wm", DateTimeOffset.UtcNow);
        conversation.CaptureSystemPromptIfAbsent("You are helpful.");
        var workingMemory = WorkingMemory.Create(
            conversation.Id,
            1,
            "# Working Memory\n## Current Goal\nShip it",
            20,
            DateTimeOffset.UtcNow);
        var now = DateTimeOffset.UtcNow;
        // More unfolded messages than CompressionRetain — all must be forwarded.
        var stored = Enumerable.Range(0, 12)
            .Select(i => ConversationMessage.Create(
                conversation.Id,
                i,
                i % 2 == 0 ? MessageRole.User : MessageRole.Assistant,
                $"msg-{i}",
                5,
                now))
            .ToList();

        _conversationRepository.Setup(r => r.FindByKeyAsync("header:conv-wm", It.IsAny<CancellationToken>()))
            .ReturnsAsync(conversation);
        _messageRepository.Setup(r => r.GetByConversationIdAsync(conversation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);
        _workingMemoryRepository.Setup(r => r.GetLatestAsync(conversation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(workingMemory);
        conversation.SetSyncedMessageCount(12, now);
        _chatCompletionClient
            .Setup(c => c.CompleteAsync(It.IsAny<ProviderEndpoint>(), It.IsAny<UpstreamRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ProviderEndpoint, UpstreamRequest, CancellationToken>((_, request, _) => forwarded = request)
            .ReturnsAsync(new UpstreamChatResult("ack", "stop", 10, 2));

        var service = CreateService();
        await service.HandleAsync(BuildRequest(conversationHeader: "conv-wm", userContent: "next tip"), CancellationToken.None);

        Assert.NotNull(forwarded);
        Assert.True(forwarded!.ReplaceMessages);
        Assert.Contains(forwarded.Messages, m => m.Role == MessageRole.System && m.Content.Contains("Working Memory"));
        // system + WM system + 12 unfolded prior + tip = at least 14 (tip may also persist as 13th)
        Assert.True(forwarded.Messages.Count >= 14, $"Expected all unfolded history; got {forwarded.Messages.Count}");
        Assert.Contains(forwarded.Messages, m => m.Content == "msg-0");
        Assert.Equal("next tip", forwarded.Messages[^1].Content);
    }

    [Fact]
    public async Task HandleAsync_AfterEmergencyStillOverHardLimit_AppliesSendTimeTrimThenForwards()
    {
        _policy.SoftLimitTokens = 50;
        _policy.HardLimitTokens = 100;
        _policy.EmergencyCompression = EmergencyCompressionMode.Sync;
        _policy.EmergencyRecentMessageCount = 2;

        UpstreamRequest? forwarded = null;
        var conversation = Conversation.Create("header:conv-trim", DateTimeOffset.UtcNow);
        conversation.CaptureSystemPromptIfAbsent("You are helpful.");
        var workingMemory = WorkingMemory.Create(
            conversation.Id,
            1,
            "# Working Memory\n## Current Goal\nShip it",
            20,
            DateTimeOffset.UtcNow);
        var now = DateTimeOffset.UtcNow;
        var stored = Enumerable.Range(0, 10)
            .Select(i => ConversationMessage.Create(
                conversation.Id,
                i,
                i % 2 == 0 ? MessageRole.User : MessageRole.Assistant,
                $"msg-{i}",
                5,
                now))
            .ToList();

        conversation.SetSyncedMessageCount(10, now);
        _conversationRepository.Setup(r => r.FindByKeyAsync("header:conv-trim", It.IsAny<CancellationToken>()))
            .ReturnsAsync(conversation);
        _messageRepository.Setup(r => r.GetByConversationIdAsync(conversation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);
        _workingMemoryRepository.Setup(r => r.GetLatestAsync(conversation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(workingMemory);
        _compressionOrchestrator
            .Setup(o => o.RunAsync(conversation.Id, CompressionMode.Emergency, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CompressionEvent?)null);

        _chatCompletionClient
            .Setup(c => c.CompleteAsync(It.IsAny<ProviderEndpoint>(), It.IsAny<UpstreamRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ProviderEndpoint, UpstreamRequest, CancellationToken>((_, request, _) => forwarded = request)
            .ReturnsAsync(new UpstreamChatResult("ack", "stop", 10, 2));

        var service = CreateService();
        var estimates = new Queue<int>([150, 150, 80]);
        _tokenEstimator
            .Setup(t => t.CountPromptTokens(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<JsonElement?>()))
            .Returns(() => estimates.Dequeue());

        await service.HandleAsync(BuildRequest(conversationHeader: "conv-trim", userContent: "next tip"), CancellationToken.None);

        Assert.NotNull(forwarded);
        Assert.DoesNotContain(forwarded!.Messages, m => m.Content == "msg-0");
        Assert.Contains(forwarded.Messages, m => m.Content == "msg-9");
        Assert.Equal("next tip", forwarded.Messages[^1].Content);
        _compressionOrchestrator.Verify(
            o => o.RunAsync(conversation.Id, CompressionMode.Emergency, It.IsAny<CancellationToken>()),
            Times.Once);
        _chatCompletionClient.Verify(
            c => c.CompleteAsync(It.IsAny<ProviderEndpoint>(), It.IsAny<UpstreamRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_AfterEmergencyAndTrimStillOverHardLimit_ThrowsContextBudgetExceeded()
    {
        _policy.SoftLimitTokens = 50;
        _policy.HardLimitTokens = 100;
        _policy.EmergencyCompression = EmergencyCompressionMode.Sync;
        _policy.EmergencyRecentMessageCount = 2;

        var conversation = Conversation.Create("header:conv-over", DateTimeOffset.UtcNow);
        conversation.CaptureSystemPromptIfAbsent("You are helpful.");
        var workingMemory = WorkingMemory.Create(
            conversation.Id,
            1,
            "# Working Memory",
            20,
            DateTimeOffset.UtcNow);
        var now = DateTimeOffset.UtcNow;
        var stored = Enumerable.Range(0, 6)
            .Select(i => ConversationMessage.Create(
                conversation.Id,
                i,
                MessageRole.User,
                $"msg-{i}",
                5,
                now))
            .ToList();

        conversation.SetSyncedMessageCount(6, now);
        _conversationRepository.Setup(r => r.FindByKeyAsync("header:conv-over", It.IsAny<CancellationToken>()))
            .ReturnsAsync(conversation);
        _messageRepository.Setup(r => r.GetByConversationIdAsync(conversation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);
        _workingMemoryRepository.Setup(r => r.GetLatestAsync(conversation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(workingMemory);
        _compressionOrchestrator
            .Setup(o => o.RunAsync(conversation.Id, CompressionMode.Emergency, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CompressionEvent?)null);

        var service = CreateService();
        _tokenEstimator
            .Setup(t => t.CountPromptTokens(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<JsonElement?>()))
            .Returns(200);

        var ex = await Assert.ThrowsAsync<Comprexy.Application.Exceptions.ContextBudgetExceededException>(
            () => service.HandleAsync(
                BuildRequest(conversationHeader: "conv-over", userContent: "next tip"),
                CancellationToken.None));

        Assert.Equal(200, ex.EstimatedTokens);
        Assert.Equal(100, ex.HardLimitTokens);
        _chatCompletionClient.Verify(
            c => c.CompleteAsync(It.IsAny<ProviderEndpoint>(), It.IsAny<UpstreamRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_HardLimitWithEmergencyOff_SkipsSyncCompactAppliesTrim()
    {
        _policy.SoftLimitTokens = 50;
        _policy.HardLimitTokens = 100;
        _policy.EmergencyCompression = EmergencyCompressionMode.Off;
        _policy.EmergencyRecentMessageCount = 2;

        UpstreamRequest? forwarded = null;
        var conversation = Conversation.Create("header:conv-off", DateTimeOffset.UtcNow);
        conversation.CaptureSystemPromptIfAbsent("You are helpful.");
        var workingMemory = WorkingMemory.Create(
            conversation.Id,
            1,
            "# Working Memory\n## Current Goal\nShip it",
            20,
            DateTimeOffset.UtcNow);
        var now = DateTimeOffset.UtcNow;
        var stored = Enumerable.Range(0, 10)
            .Select(i => ConversationMessage.Create(
                conversation.Id,
                i,
                i % 2 == 0 ? MessageRole.User : MessageRole.Assistant,
                $"msg-{i}",
                5,
                now))
            .ToList();

        conversation.SetSyncedMessageCount(10, now);
        _conversationRepository.Setup(r => r.FindByKeyAsync("header:conv-off", It.IsAny<CancellationToken>()))
            .ReturnsAsync(conversation);
        _messageRepository.Setup(r => r.GetByConversationIdAsync(conversation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);
        _workingMemoryRepository.Setup(r => r.GetLatestAsync(conversation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(workingMemory);

        _chatCompletionClient
            .Setup(c => c.CompleteAsync(It.IsAny<ProviderEndpoint>(), It.IsAny<UpstreamRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ProviderEndpoint, UpstreamRequest, CancellationToken>((_, request, _) => forwarded = request)
            .ReturnsAsync(new UpstreamChatResult("ack", "stop", 10, 2));

        var service = CreateService();
        var estimates = new Queue<int>([150, 80]);
        _tokenEstimator
            .Setup(t => t.CountPromptTokens(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<JsonElement?>()))
            .Returns(() => estimates.Dequeue());

        await service.HandleAsync(BuildRequest(conversationHeader: "conv-off", userContent: "next tip"), CancellationToken.None);

        Assert.NotNull(forwarded);
        _compressionOrchestrator.Verify(
            o => o.RunAsync(It.IsAny<Guid>(), CompressionMode.Emergency, It.IsAny<CancellationToken>()),
            Times.Never);
        Assert.DoesNotContain(forwarded!.Messages, m => m.Content == "msg-0");
        Assert.Equal("next tip", forwarded.Messages[^1].Content);
    }
}
