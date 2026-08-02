using System.Text.Json;
using Comprexy.Application.Abstractions;
using Comprexy.Application.Configuration;
using Comprexy.Application.Models;
using Comprexy.Application.Services;
using Comprexy.Application.Services.CacheAlignment;
using Comprexy.Application.Services.ChatTurn;
using Comprexy.Application.Services.ToolIr;
using Comprexy.Domain.Entities;
using Comprexy.Domain.Enums;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Comprexy.Application.Tests.Services;

public class ProxyChatCompletionServiceTests
{
    private readonly Mock<IConversationRepository> _conversationRepository = new();
    private readonly Mock<IConversationMessageRepository> _messageRepository = new();
    private readonly Mock<IWorkingMemoryRepository> _workingMemoryRepository = new();
    private readonly Mock<ICompressionEventRepository> _compressionEventRepository = new();
    private readonly Mock<ITokenEstimator> _tokenEstimator = new();
    private readonly Mock<IChatCompletionClient> _chatCompletionClient = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IClock> _clock = new();
    private readonly Mock<IConversationToolCatalogRepository> _toolCatalogRepository = new();
    private readonly Mock<IConversationToolDefinitionRepository> _toolDefinitionRepository = new();

    private readonly ContextPolicyOptions _policy = new()
    {
        SoftLimitTokens = 100
    };

    private ProxyOptions _proxyOptions = new();

    private CacheAlignmentOptions _cacheAlignmentOptions = new() { Enabled = true, MaxConversations = 1024 };

    private readonly CompressionOptions _compressionOptions = new();

    private readonly Mock<IHostApplicationLifetime> _hostLifetime = new();

    private ToolSchemaOptions _toolSchemaOptions = new() { Mode = ToolSchemaMode.Off };

    private InMemoryConversationToolCallMapRepository _callIdMapRepo = new();
    private int _mapSaveChangesCount;

    private int _estimatedTokensToReturn = 10;

    private int _wrapUpTipTokens = 5;

    private string? _providerModel = "target-model";

    private static readonly CompressionPromptFactory PromptFactory = new(
        "inline instruction",
        """
        # Working Memory

        ## Current Goal
        ...
        """);

    private static string ValidWorkingMemory => "# Working Memory\n## Current Goal\nInline summary";

    private static bool IsWrapUpRequest(UpstreamRequest request) =>
        request.Messages.Count > 0
        && request.Messages[^1].Role == MessageRole.User
        && request.Messages[^1].Content == PromptFactory.BuildInlineWrapUpUserMessage().Content;

    private readonly Dictionary<Guid, ConversationToolCatalog> _toolCatalogs = new();

    private ProxyChatCompletionService CreateService(
        IConversationRequestGate? requestGate = null,
        IConversationMetricsRecorder? metricsRecorder = null,
        ICacheAlignmentService? cacheAlignment = null)
    {
        _clock.Setup(c => c.UtcNow).Returns(DateTimeOffset.UtcNow);
        _tokenEstimator.Setup(t => t.CountTokens(It.IsAny<string>())).Returns(5);
        _tokenEstimator.Setup(t => t.CountTokens(It.IsAny<IEnumerable<ChatMessage>>())).Returns(() => _estimatedTokensToReturn);
        _tokenEstimator.Setup(t => t.CountPromptTokens(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<JsonElement?>()))
            .Returns(() => _estimatedTokensToReturn);

        _toolCatalogs.Clear();
        _toolCatalogRepository
            .Setup(r => r.GetByConversationIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) =>
                _toolCatalogs.TryGetValue(id, out var catalog) ? catalog : null);
        _toolCatalogRepository
            .Setup(r => r.GetTrackedByConversationIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) =>
                _toolCatalogs.TryGetValue(id, out var catalog) ? catalog : null);
        _toolCatalogRepository
            .Setup(r => r.Add(It.IsAny<ConversationToolCatalog>()))
            .Callback<ConversationToolCatalog>(c => _toolCatalogs[c.ConversationId] = c);
        _toolDefinitionRepository
            .Setup(r => r.GetByConversationIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _compressionEventRepository
            .Setup(r => r.GetLatestSucceededAsync(It.IsAny<Guid>(), CompressionMode.Inline, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CompressionEvent?)null);
        _hostLifetime.Setup(h => h.ApplicationStopping).Returns(CancellationToken.None);
        SetupWrapUpTipTokenEstimate(_wrapUpTipTokens);

        var toolSchemaOptions = Options.Create(_toolSchemaOptions);
        var endpointResolver = new ProviderEndpointResolver(
            Options.Create(new ProviderOptions { BaseUrl = "http://upstream", ApiKey = "k", Model = _providerModel }),
            Options.Create(_compressionOptions));
        var fileCache = new ToolIrFileBodyCache(toolSchemaOptions);
        var callIdHotCache = new ToolIrCallIdMap(_clock.Object, toolSchemaOptions);
        _callIdMapRepo = new InMemoryConversationToolCallMapRepository();
        _mapSaveChangesCount = 0;
        var callIdMapService = new ToolIrCallIdMapService(
            callIdHotCache,
            new InMemoryToolIrCallIdMapUnitOfWorkFactory(
                _callIdMapRepo,
                () =>
                {
                    _mapSaveChangesCount++;
                    return Task.CompletedTask;
                }),
            _clock.Object,
            toolSchemaOptions);

        ICacheAlignmentService alignment = cacheAlignment
            ?? new CacheAlignmentService(Options.Create(_cacheAlignmentOptions));

        var contextBuilder = new ContextBuilder();
        var metrics = metricsRecorder ?? Mock.Of<IConversationMetricsRecorder>(m => m.IsEnabled == false);
        var toolSchemaOrchestrator = new ToolSchemaOrchestrator(
            toolSchemaOptions,
            new ToolCatalogParser(),
            new ToolArgumentValidator(),
            new ToolIrSchemaMapper(
                toolSchemaOptions,
                Options.Create(_compressionOptions),
                endpointResolver,
                _chatCompletionClient.Object,
                _tokenEstimator.Object,
                metrics,
                NullLogger<ToolIrSchemaMapper>.Instance),
            new ToolIrPlanner(toolSchemaOptions, fileCache),
            ToolIrTestFactory.CreateDistiller(toolSchemaOptions, fileCache),
            callIdMapService,
            _toolCatalogRepository.Object,
            _toolDefinitionRepository.Object,
            _chatCompletionClient.Object,
            _clock.Object,
            ToolIrTestFactory.CreateShapeStore(_toolSchemaOptions),
            NullLogger<ToolSchemaOrchestrator>.Instance);
        var messageHelper = new ChatTurnMessageHelper(_messageRepository.Object, _tokenEstimator.Object);
        var historySynchronizer = new ClientHistorySynchronizer(
            _messageRepository.Object,
            _workingMemoryRepository.Object,
            toolSchemaOrchestrator,
            alignment,
            _tokenEstimator.Object,
            Options.Create(_proxyOptions),
            Options.Create(_cacheAlignmentOptions),
            NullLogger<ClientHistorySynchronizer>.Instance);
        var contextMaterializer = new OutgoingContextMaterializer(
            contextBuilder,
            alignment,
            Options.Create(_policy),
            Options.Create(_cacheAlignmentOptions),
            NullLogger<OutgoingContextMaterializer>.Instance);
        var inlineWrapUpRunner = new InlineWrapUpRunner(
            _messageRepository.Object,
            _workingMemoryRepository.Object,
            _compressionEventRepository.Object,
            PromptFactory,
            _chatCompletionClient.Object,
            _tokenEstimator.Object,
            contextBuilder,
            alignment,
            new RecentContextSelector(Options.Create(_policy)),
            metrics,
            _clock.Object,
            Options.Create(_cacheAlignmentOptions),
            NullLogger<InlineWrapUpRunner>.Instance);
        var preparer = new ChatTurnPreparer(
            _conversationRepository.Object,
            _messageRepository.Object,
            _workingMemoryRepository.Object,
            _compressionEventRepository.Object,
            _tokenEstimator.Object,
            contextBuilder,
            alignment,
            new ContextBudgetEvaluator(Options.Create(_policy)),
            PromptFactory,
            toolSchemaOrchestrator,
            historySynchronizer,
            contextMaterializer,
            messageHelper,
            endpointResolver,
            metrics,
            _clock.Object,
            Options.Create(_policy),
            Options.Create(_proxyOptions),
            Options.Create(_cacheAlignmentOptions),
            Mock.Of<IPayloadTraceLogger>(),
            Mock.Of<IRequestTraceFileSession>(),
            NullLogger<ChatTurnPreparer>.Instance);
        var completer = new ChatTurnCompleter(
            _messageRepository.Object,
            toolSchemaOrchestrator,
            metrics,
            inlineWrapUpRunner,
            messageHelper,
            _tokenEstimator.Object,
            _clock.Object,
            Options.Create(_policy),
            NullLogger<ChatTurnCompleter>.Instance);

        return new ProxyChatCompletionService(
            new ConversationIdentityResolver(),
            requestGate ?? new ConversationRequestGate(),
            _chatCompletionClient.Object,
            toolSchemaOrchestrator,
            preparer,
            completer,
            _unitOfWork.Object,
            _hostLifetime.Object,
            NullLogger<ProxyChatCompletionService>.Instance);
    }

    private void SetupWrapUpTipTokenEstimate(int tokens = 5)
    {
        _tokenEstimator.Setup(t => t.CountTokens(It.Is<IEnumerable<ChatMessage>>(messages =>
                messages.Count() == 1 &&
                messages.First().Role == MessageRole.User &&
                messages.First().Content == PromptFactory.BuildInlineWrapUpUserMessage().Content)))
            .Returns(tokens);
    }

    private IncomingChatRequest BuildRequest(
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

    private const string MidChainContentFrame =
        """{"choices":[{"index":0,"delta":{"content":"Working on it"}}]}""";

    private const string MidChainToolCallFrame =
        """{"choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"id":"call_new","type":"function","function":{"name":"lookup","arguments":""}}]}}]}""";

    private const string MidChainToolArgumentsFrame =
        """{"choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"function":{"arguments":"{\"q\":\"x\"}"}}]}}]}""";

    private const string MidChainFinishFrame =
        """{"choices":[{"index":0,"delta":{},"finish_reason":"tool_calls"}],"usage":{"prompt_tokens":40,"completion_tokens":12}}""";

    private const string MidChainOpenAssistantWire =
        """{"role":"assistant","content":null,"tool_calls":[{"id":"call_new","type":"function","function":{"name":"lookup","arguments":"{\"q\":\"x\"}"}}]}""";

    private sealed record MidChainInlineFixture(
        Conversation Conversation,
        ConversationMessage OlderUser,
        ConversationMessage PriorAssistant,
        ConversationMessage PriorTool);

    /// <summary>
    /// Inline-eligible conversation whose stored history is a closed tool chain: soft pressure,
    /// existing working memory v1, no cooldown. Retain window keeps the prior tool group, so the
    /// fold set is the older user message alone.
    /// </summary>
    private MidChainInlineFixture SetupMidChainInlineConversation(string conversationHeader)
    {
        _policy.CompressionRetainMessageCount = 1;
        _estimatedTokensToReturn = 150;

        var now = DateTimeOffset.UtcNow;
        var conversationKey = "header:" + conversationHeader;
        var conversation = Conversation.Create(conversationKey, now);
        conversation.CaptureSystemPromptIfAbsent("You are helpful.");
        conversation.SetSyncedMessageCount(1, now);

        const string priorAssistantWire =
            """{"role":"assistant","tool_calls":[{"id":"call_prior","type":"function","function":{"name":"lookup","arguments":"{}"}}]}""";
        const string priorToolWire =
            """{"role":"tool","tool_call_id":"call_prior","content":"prior result"}""";
        var olderUser = ConversationMessage.Create(conversation.Id, 0, MessageRole.User, "older", 5, now);
        var priorAssistant = ConversationMessage.Create(
            conversation.Id, 1, MessageRole.Assistant, string.Empty, 8, now, priorAssistantWire);
        var priorTool = ConversationMessage.Create(
            conversation.Id, 2, MessageRole.Tool, "prior result", 4, now, priorToolWire);
        var stored = new List<ConversationMessage> { olderUser, priorAssistant, priorTool };
        var existingWorkingMemory = WorkingMemory.Create(
            conversation.Id,
            1,
            "# Working Memory\n## Current Goal\nExisting",
            8,
            now);

        _conversationRepository.Setup(r => r.FindByKeyAsync(conversationKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(conversation);
        _messageRepository.Setup(r => r.GetByConversationIdAsync(conversation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);
        _workingMemoryRepository.Setup(r => r.GetLatestAsync(conversation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingWorkingMemory);

        return new MidChainInlineFixture(conversation, olderUser, priorAssistant, priorTool);
    }

    private void SetupMidChainStream(Action? onStreamStarted = null)
    {
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
                    onStreamStarted?.Invoke();
                    await onRawSseData(MidChainContentFrame, token);
                    await onRawSseData(MidChainToolCallFrame, token);
                    await onRawSseData(MidChainToolArgumentsFrame, token);
                    await onRawSseData(MidChainFinishFrame, token);
                    await onRawSseData("[DONE]", token);
                    return new UpstreamChatResult(
                        Content: string.Empty,
                        FinishReason: "tool_calls",
                        PromptTokens: 40,
                        CompletionTokens: 12,
                        AssistantMessageJson: MidChainOpenAssistantWire);
                });
    }

    private static List<string> WrittenSse(IEnumerable<string> ledger) =>
        ledger
            .Where(entry => entry.StartsWith("sse:", StringComparison.Ordinal))
            .Select(entry => entry["sse:".Length..])
            .ToList();

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
    }

    [Fact]
    public async Task HandleAsync_InlineMode_UnderSoft_DoesNotInjectProtocolOrWrapUp()
    {
        _estimatedTokensToReturn = 10;
        var captured = new List<UpstreamRequest>();

        _conversationRepository.Setup(r => r.FindByKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Conversation?)null);
        _workingMemoryRepository.Setup(r => r.GetLatestAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkingMemory?)null);
        _chatCompletionClient
            .Setup(c => c.CompleteAsync(It.IsAny<ProviderEndpoint>(), It.IsAny<UpstreamRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ProviderEndpoint, UpstreamRequest, CancellationToken>((_, request, _) => captured.Add(request))
            .ReturnsAsync(new UpstreamChatResult("Hi there!", "stop", 42, 7));

        var service = CreateService();
        await service.HandleAsync(BuildRequest(conversationHeader: "inline-soft"), CancellationToken.None);

        Assert.Single(captured);
        Assert.DoesNotContain(
            captured[0].Messages,
            m => m.Role == MessageRole.User &&
                 m.Content == PromptFactory.BuildInlineWrapUpUserMessage().Content);
        Assert.DoesNotContain(
            captured[0].Messages,
            m => m.Content != null && m.Content.Contains("Comprexy Inline", StringComparison.Ordinal));
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
    }

    [Fact]
    public async Task HandleAsync_BeforeFirstCompression_RebuildsFromStoredMessages_NotClientWire()
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
        Assert.True(forwarded!.ReplaceMessages);
        Assert.Equal(UpstreamRequestPurpose.Chat, forwarded.Purpose);
        Assert.Equal(MessageRole.System, forwarded.Messages[0].Role);
        Assert.Equal(request.Messages[^1].Content, forwarded.Messages[^1].Content);
        Assert.DoesNotContain(forwarded.Messages, m => m.Content.Contains("ConversationId:", StringComparison.Ordinal));
        // No WM block before first compression.
        Assert.DoesNotContain(
            forwarded.Messages,
            m => m.Role == MessageRole.System && m.Content.Contains("compressed historical context", StringComparison.Ordinal));
    }

    [Fact]
    public async Task HandleAsync_PreWmVirtual_UsesStoredIrHistory_NotNativeClientToolCalls()
    {
        _toolSchemaOptions = new ToolSchemaOptions { Mode = ToolSchemaMode.Virtual };
        _estimatedTokensToReturn = 10;
        const string irCallId = "call_ir_range_1";
        const string clientCallId = "cur_bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        var now = DateTimeOffset.UtcNow;

        var conversation = Conversation.Create("header:pre-wm-ir", now);
        conversation.CaptureSystemPromptIfAbsent("You are helpful.");
        // Client previously synced: system + user + native assistant + native tool (4 messages).
        conversation.SetSyncedMessageCount(4, now);

        var irAssistantWire =
            "{\"role\":\"assistant\",\"content\":\"\\n\\n\",\"tool_calls\":[{\"id\":\"" + irCallId +
            "\",\"type\":\"function\",\"function\":{\"name\":\"" + ToolSchemaConstants.FileRangeToolName +
            "\",\"arguments\":\"{\\\"path\\\":\\\"docs/a.md\\\",\\\"start_line\\\":1,\\\"end_line\\\":20}\"}}]}";
        var irObservation =
            "{\"type\":\"file_range\",\"path\":\"docs/a.md\",\"start_line\":1,\"end_line\":20,\"truncated\":false,\"content\":\"# Doc\\nbody\"}";
        var irToolWire =
            "{\"role\":\"tool\",\"tool_call_id\":\"" + irCallId + "\",\"content\":" +
            JsonSerializer.Serialize(irObservation) + "}";

        var stored = new List<ConversationMessage>
        {
            ConversationMessage.Create(conversation.Id, 0, MessageRole.User, "load docs", 5, now),
            ConversationMessage.Create(
                conversation.Id, 1, MessageRole.Assistant, "[tool_calls: comprexy_read_file_range]", 5, now, irAssistantWire),
            ConversationMessage.Create(conversation.Id, 2, MessageRole.Tool, irObservation, 5, now, irToolWire)
        };

        _conversationRepository.Setup(r => r.FindByKeyAsync("header:pre-wm-ir", It.IsAny<CancellationToken>()))
            .ReturnsAsync(conversation);
        _messageRepository.Setup(r => r.GetByConversationIdAsync(conversation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);
        _workingMemoryRepository.Setup(r => r.GetLatestAsync(conversation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkingMemory?)null);

        UpstreamRequest? forwarded = null;
        SetupCompressionMapperForNonFileTools("read", "bash");
        _chatCompletionClient
            .Setup(c => c.CompleteAsync(
                It.IsAny<ProviderEndpoint>(),
                It.Is<UpstreamRequest>(r => r.Purpose == UpstreamRequestPurpose.Chat),
                It.IsAny<CancellationToken>()))
            .Callback<ProviderEndpoint, UpstreamRequest, CancellationToken>((_, request, _) => forwarded = request)
            .ReturnsAsync(new UpstreamChatResult("ack", "stop", 10, 2));

        var service = CreateService();

        // Client wire still carries native remapped tool_calls; only the tip is new.
        var payload = new
        {
            model = "client-model",
            tools = new object[]
            {
                new { type = "function", function = new { name = "read" } },
                new { type = "function", function = new { name = "bash" } }
            },
            messages = new object[]
            {
                new { role = "system", content = "You are helpful." },
                new { role = "user", content = "load docs" },
                new
                {
                    role = "assistant",
                    content = "\n\n",
                    tool_calls = new object[]
                    {
                        new
                        {
                            id = clientCallId,
                            type = "function",
                            function = new
                            {
                                name = "read",
                                arguments = "{\"filePath\":\"docs/a.md\",\"offset\":1,\"limit\":20}"
                            }
                        }
                    }
                },
                new
                {
                    role = "tool",
                    tool_call_id = clientCallId,
                    content = "<path>docs/a.md</path><content>\n1: # Doc\n</content>"
                },
                new { role = "user", content = "continue" }
            }
        };
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        var request = Comprexy.Api.Mapping.ChatCompletionRequestParser.Parse(
            document.RootElement.Clone(),
            "pre-wm-ir");

        await service.HandleAsync(request, CancellationToken.None);

        Assert.NotNull(forwarded);
        Assert.True(forwarded!.ReplaceMessages);

        var assistantWires = forwarded.Messages
            .Where(m => m.Role == MessageRole.Assistant)
            .Select(m => m.RawWireMessage?.GetRawText() ?? m.Content)
            .ToList();
        Assert.Contains(assistantWires, w => w != null && w.Contains(ToolSchemaConstants.FileRangeToolName, StringComparison.Ordinal));
        Assert.DoesNotContain(
            assistantWires,
            w => w != null &&
                 w.Contains("\"name\":\"read\"", StringComparison.Ordinal) &&
                 !w.Contains(ToolSchemaConstants.FileRangeToolName, StringComparison.Ordinal));

        var toolContents = forwarded.Messages
            .Where(m => m.Role == MessageRole.Tool)
            .Select(m => m.Content)
            .ToList();
        Assert.Contains(toolContents, c => c != null && c.Contains("\"type\":\"file_range\"", StringComparison.Ordinal));
        Assert.DoesNotContain(toolContents, c => c != null && c.Contains("<path>", StringComparison.Ordinal));
        Assert.Equal("continue", forwarded.Messages[^1].Content);
    }

    [Fact]
    public async Task HandleAsync_Virtual_FirstTurnClientNativeFileHistory_NotPersistedOrForwarded()
    {
        _toolSchemaOptions = new ToolSchemaOptions { Mode = ToolSchemaMode.Virtual };
        _estimatedTokensToReturn = 10;
        const string clientCallId = "cur_firstturnpoisonaaaaaaaaaaaaaaaa";

        var addedMessages = new List<ConversationMessage>();
        _messageRepository.Setup(r => r.Add(It.IsAny<ConversationMessage>()))
            .Callback<ConversationMessage>(m => addedMessages.Add(m));
        _conversationRepository
            .Setup(r => r.FindByKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Conversation?)null);
        _conversationRepository
            .Setup(r => r.Add(It.IsAny<Conversation>()));
        _messageRepository
            .Setup(r => r.GetByConversationIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => addedMessages.ToList());
        _workingMemoryRepository
            .Setup(r => r.GetLatestAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkingMemory?)null);

        SetupCompressionMapperForReadFileAndBash();
        UpstreamRequest? forwarded = null;
        _chatCompletionClient
            .Setup(c => c.CompleteAsync(
                It.IsAny<ProviderEndpoint>(),
                It.Is<UpstreamRequest>(r => r.Purpose == UpstreamRequestPurpose.Chat),
                It.IsAny<CancellationToken>()))
            .Callback<ProviderEndpoint, UpstreamRequest, CancellationToken>((_, request, _) => forwarded = request)
            .ReturnsAsync(new UpstreamChatResult("ok", "stop", 10, 2));

        var service = CreateService();

        var payload = new
        {
            model = "client-model",
            tools = new object[]
            {
                new
                {
                    type = "function",
                    function = new
                    {
                        name = "read",
                        description = "Read a file.",
                        parameters = new
                        {
                            type = "object",
                            properties = new { filePath = new { type = "string" } },
                            required = new[] { "filePath" }
                        }
                    }
                },
                new
                {
                    type = "function",
                    function = new
                    {
                        name = "bash",
                        description = "Run a shell command.",
                        parameters = new
                        {
                            type = "object",
                            properties = new { command = new { type = "string" } },
                            required = new[] { "command" }
                        }
                    }
                }
            },
            messages = new object[]
            {
                new { role = "system", content = "You are helpful." },
                new { role = "user", content = "load docs" },
                new
                {
                    role = "assistant",
                    content = "",
                    tool_calls = new object[]
                    {
                        new
                        {
                            id = clientCallId,
                            type = "function",
                            function = new
                            {
                                name = "read",
                                arguments = "{\"filePath\":\"docs/a.md\"}"
                            }
                        }
                    }
                },
                new
                {
                    role = "tool",
                    tool_call_id = clientCallId,
                    content = "<path>docs/a.md</path><content>\n1: # Doc\n</content>"
                },
                new { role = "user", content = "summarize" }
            }
        };
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        var request = Comprexy.Api.Mapping.ChatCompletionRequestParser.Parse(
            document.RootElement.Clone(),
            "first-turn-native");

        await service.HandleAsync(request, CancellationToken.None);

        Assert.DoesNotContain(
            addedMessages,
            m => m.Role == MessageRole.Assistant &&
                 (m.RawWireJson?.Contains("\"name\":\"read\"", StringComparison.Ordinal) ?? false));
        Assert.DoesNotContain(
            addedMessages,
            m => m.Role == MessageRole.Tool &&
                 (m.Content?.Contains("<path>", StringComparison.Ordinal) ?? false));
        Assert.Contains(addedMessages, m => m.Role == MessageRole.User && m.Content == "load docs");
        Assert.Contains(addedMessages, m => m.Role == MessageRole.User && m.Content == "summarize");

        Assert.NotNull(forwarded);
        Assert.DoesNotContain(
            forwarded!.Messages,
            m => m.Role == MessageRole.Assistant &&
                 (m.RawWireMessage?.GetRawText().Contains("\"name\":\"read\"", StringComparison.Ordinal) ?? false));
        Assert.DoesNotContain(
            forwarded.Messages,
            m => m.Role == MessageRole.Tool &&
                 (m.Content?.Contains("<path>", StringComparison.Ordinal) ?? false));
        Assert.Equal("summarize", forwarded.Messages[^1].Content);
    }



    [Fact]
    public async Task HandleAsync_VirtualWithTools_RewritesOutboundToolsSurface()
    {
        _toolSchemaOptions = new ToolSchemaOptions
        {
            Mode = ToolSchemaMode.Virtual
        };
        _estimatedTokensToReturn = 10;

        UpstreamRequest? forwarded = null;
        _conversationRepository.Setup(r => r.FindByKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Conversation?)null);
        _workingMemoryRepository.Setup(r => r.GetLatestAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkingMemory?)null);
        SetupCompressionMapperForNonFileTools("lookup");
        _chatCompletionClient
            .Setup(c => c.CompleteAsync(
                It.IsAny<ProviderEndpoint>(),
                It.Is<UpstreamRequest>(r => r.Purpose == UpstreamRequestPurpose.Chat),
                It.IsAny<CancellationToken>()))
            .Callback<ProviderEndpoint, UpstreamRequest, CancellationToken>((_, request, _) => forwarded = request)
            .ReturnsAsync(new UpstreamChatResult("ack", "stop", 10, 2));

        var service = CreateService();
        await service.HandleAsync(BuildRequest(), CancellationToken.None);

        Assert.NotNull(forwarded);
        Assert.True(forwarded!.RewrittenClientRequest.HasValue);
        Assert.DoesNotContain(
            forwarded.Messages,
            m => m.Role == MessageRole.System &&
                 (m.Content?.Contains("tool schema rules", StringComparison.Ordinal) == true ||
                  m.Content?.Contains("compact index", StringComparison.OrdinalIgnoreCase) == true));
        Assert.True(forwarded.RewrittenClientRequest!.Value.TryGetProperty("tools", out var rewrittenTools));
        var toolNames = rewrittenTools.EnumerateArray()
            .Select(t => t.GetProperty("function").GetProperty("name").GetString())
            .ToList();
        Assert.Contains(ToolSchemaConstants.ConversationIdMetaToolName, toolNames);
        Assert.Contains("lookup", toolNames);
        Assert.DoesNotContain("get_tool_definition", toolNames);
        Assert.DoesNotContain(
            toolNames,
            n => n is not null &&
                 n.StartsWith("comprexy_", StringComparison.Ordinal) &&
                 !ToolSchemaConstants.IsConversationIdMetaTool(n));
        _toolCatalogRepository.Verify(r => r.Add(It.IsAny<ConversationToolCatalog>()), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task HandleAsync_VirtualInvalidMapExhaustion_SaveChangesEvenWhenRewriteNull()
    {
        _toolSchemaOptions = new ToolSchemaOptions
        {
            Mode = ToolSchemaMode.Virtual,
            MappingMaxRetries = 0
        };
        _estimatedTokensToReturn = 10;

        _conversationRepository.Setup(r => r.FindByKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Conversation?)null);
        _workingMemoryRepository.Setup(r => r.GetLatestAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkingMemory?)null);

        var service = CreateService();

        var saveWhileDisabled = 0;
        _unitOfWork
            .Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                if (_toolCatalogs.Values.Any(c => c.ToolIrDisabled))
                {
                    saveWhileDisabled++;
                }
            })
            .Returns(Task.CompletedTask);

        _chatCompletionClient
            .Setup(c => c.CompleteAsync(
                It.IsAny<ProviderEndpoint>(),
                It.Is<UpstreamRequest>(r => r.Purpose == UpstreamRequestPurpose.Compression),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpstreamChatResult("""{"not":"valid"}""", "stop", 1, 1));
        _chatCompletionClient
            .Setup(c => c.CompleteAsync(
                It.IsAny<ProviderEndpoint>(),
                It.Is<UpstreamRequest>(r => r.Purpose == UpstreamRequestPurpose.Chat),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("upstream failed after prepare"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.HandleAsync(BuildRequest(), CancellationToken.None));

        var addedCatalog = Assert.Single(_toolCatalogs.Values);
        Assert.True(addedCatalog.ToolIrDisabled);
        Assert.True(string.IsNullOrWhiteSpace(addedCatalog.MappingJson));
        // Prepare flushed DisableToolIr before upstream; Complete never ran.
        Assert.True(saveWhileDisabled >= 1);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task HandleAsync_InlineMode_VirtualTools_WrapUpStripsToolsFromUpstreamRequest()
    {
        _toolSchemaOptions = new ToolSchemaOptions { Mode = ToolSchemaMode.Virtual };
        _policy.CompressionRetainMessageCount = 1;
        _estimatedTokensToReturn = 150;

        var conversation = Conversation.Create("header:inline-virtual-strip", DateTimeOffset.UtcNow);
        conversation.CaptureSystemPromptIfAbsent("You are helpful.");
        conversation.SetSyncedMessageCount(2, DateTimeOffset.UtcNow);
        var stored = new List<ConversationMessage>
        {
            ConversationMessage.Create(conversation.Id, 0, MessageRole.User, "older", 5, DateTimeOffset.UtcNow)
        };
        var workingMemory = WorkingMemory.Create(
            conversation.Id,
            1,
            "# Working Memory\n## Current Goal\nEarlier",
            8,
            DateTimeOffset.UtcNow);

        _conversationRepository.Setup(r => r.FindByKeyAsync("header:inline-virtual-strip", It.IsAny<CancellationToken>()))
            .ReturnsAsync(conversation);
        _messageRepository.Setup(r => r.GetByConversationIdAsync(conversation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);
        _workingMemoryRepository.Setup(r => r.GetLatestAsync(conversation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(workingMemory);

        SetupCompressionMapperForNonFileTools("lookup");

        var captured = new List<UpstreamRequest>();
        _chatCompletionClient
            .Setup(c => c.CompleteAsync(It.IsAny<ProviderEndpoint>(), It.IsAny<UpstreamRequest>(), It.IsAny<CancellationToken>()))
            .Returns<ProviderEndpoint, UpstreamRequest, CancellationToken>((_, request, _) =>
            {
                captured.Add(request);
                if (IsWrapUpRequest(request))
                {
                    return Task.FromResult(new UpstreamChatResult(ValidWorkingMemory, "stop", 30, 8));
                }

                if (request.Purpose == UpstreamRequestPurpose.Compression)
                {
                    var user = request.Messages.First(m => m.Role == MessageRole.User).Content ?? string.Empty;
                    var hashLine = user.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                        .First(line => line.StartsWith("schema_hash:", StringComparison.Ordinal));
                    var hash = hashLine["schema_hash:".Length..].Trim();
                    var mapping = JsonSerializer.Serialize(new
                    {
                        schema_hash = hash,
                        client_capabilities = new[]
                        {
                            new
                            {
                                client_tool = "lookup",
                                capability = "NON_FILE",
                                risk = "low",
                                supports = new { path = false, offset = false, limit = false, query = false }
                            }
                        },
                        bindings = Array.Empty<object>()
                    });
                    return Task.FromResult(new UpstreamChatResult(mapping, "stop", 1, 1));
                }

                return Task.FromResult(new UpstreamChatResult(
                    "Visible answer",
                    "stop",
                    40,
                    12,
                    AssistantMessageJson: """{"role":"assistant","content":"Visible answer"}"""));
            });

        var service = CreateService();
        var result = await service.HandleAsync(
            BuildRequest(conversationHeader: "inline-virtual-strip", userContent: "new tip"),
            CancellationToken.None);

        Assert.Equal("Visible answer", result.AssistantContent);
        var main = Assert.Single(captured, r => r.Purpose == UpstreamRequestPurpose.Chat);
        var wrapUp = Assert.Single(captured, IsWrapUpRequest);

        Assert.True(main.RewrittenClientRequest.HasValue);
        Assert.True(main.RewrittenClientRequest!.Value.TryGetProperty("tools", out var mainTools));
        Assert.Contains(
            mainTools.EnumerateArray().Select(t => t.GetProperty("function").GetProperty("name").GetString()),
            n => n == "lookup" || n == ToolSchemaConstants.ConversationIdMetaToolName);

        Assert.True(wrapUp.OriginalClientRequest.HasValue);
        Assert.True(wrapUp.OriginalClientRequest!.Value.TryGetProperty("tools", out _));
        Assert.Equal("none", wrapUp.OriginalClientRequest.Value.GetProperty("tool_choice").GetString());
        if (wrapUp.RewrittenClientRequest is { } wrapRewritten)
        {
            Assert.True(wrapRewritten.TryGetProperty("tools", out _));
            Assert.Equal("none", wrapRewritten.GetProperty("tool_choice").GetString());
        }
    }

    [Fact]
    public async Task HandleAsync_AcceptsClientToolResultForPersistedAssistantCall()
    {
        _toolSchemaOptions = new ToolSchemaOptions
        {
            Mode = ToolSchemaMode.Off
        };
        _estimatedTokensToReturn = 10;

        var conversation = Conversation.Create("header:conv-tool-result", DateTimeOffset.UtcNow);
        conversation.CaptureSystemPromptIfAbsent("You are helpful.");
        conversation.SetSyncedMessageCount(3, DateTimeOffset.UtcNow);
        var storedUser = ConversationMessage.Create(
            conversation.Id,
            0,
            MessageRole.User,
            "find it",
            1,
            DateTimeOffset.UtcNow);
        var storedAssistant = ConversationMessage.Create(
            conversation.Id,
            1,
            MessageRole.Assistant,
            string.Empty,
            1,
            DateTimeOffset.UtcNow,
            """{"role":"assistant","content":null,"tool_calls":[{"id":"call_da0beee1","type":"function","function":{"name":"lookup","arguments":"{}"}}]}""");

        _conversationRepository.Setup(r => r.FindByKeyAsync("header:conv-tool-result", It.IsAny<CancellationToken>()))
            .ReturnsAsync(conversation);
        _messageRepository.Setup(r => r.GetByConversationIdAsync(conversation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([storedUser, storedAssistant]);
        _workingMemoryRepository.Setup(r => r.GetLatestAsync(conversation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkingMemory?)null);

        _chatCompletionClient
            .Setup(c => c.CompleteAsync(It.IsAny<ProviderEndpoint>(), It.IsAny<UpstreamRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpstreamChatResult("done", "stop", 10, 2));

        var payload = new
        {
            model = "client-model",
            tools = new object[]
            {
                new { type = "function", function = new { name = "lookup", description = "Lookup." } }
            },
            messages = new object[]
            {
                new { role = "system", content = "You are helpful." },
                new { role = "user", content = "find it" },
                new
                {
                    role = "assistant",
                    content = (string?)null,
                    tool_calls = new object[]
                    {
                        new
                        {
                            id = "call_da0beee1",
                            type = "function",
                            function = new { name = "lookup", arguments = "{}" }
                        }
                    }
                },
                new { role = "tool", tool_call_id = "call_da0beee1", content = "result" }
            }
        };
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        var request = Comprexy.Api.Mapping.ChatCompletionRequestParser.Parse(
            document.RootElement.Clone(),
            "conv-tool-result");

        var service = CreateService();
        var result = await service.HandleAsync(request, CancellationToken.None);

        Assert.Equal(conversation.Id, result.ConversationId);
        Assert.Equal("done", result.AssistantContent);
        _messageRepository.Verify(
            r => r.Add(It.Is<ConversationMessage>(m => m.Role == MessageRole.Tool)),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task HandleStreamingAsync_EmitsUsageChunkWhenIncludeUsageRequested()
    {
        _toolSchemaOptions = new ToolSchemaOptions
        {
            Mode = ToolSchemaMode.Off
        };
        _estimatedTokensToReturn = 10;

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
                    await onRawSseData(
                        """{"id":"chatcmpl-1","object":"chat.completion.chunk","choices":[{"index":0,"delta":{"role":"assistant","content":"hi"},"finish_reason":null}]}""",
                        token);
                    await onRawSseData(
                        """{"id":"chatcmpl-1","object":"chat.completion.chunk","choices":[{"index":0,"delta":{},"finish_reason":"stop"}]}""",
                        token);
                    await onRawSseData(
                        """{"id":"chatcmpl-1","object":"chat.completion.chunk","choices":[],"usage":{"prompt_tokens":100,"completion_tokens":5,"total_tokens":105,"prompt_tokens_details":{"cached_tokens":80}}}""",
                        token);
                    await onRawSseData("[DONE]", token);
                    return new UpstreamChatResult(
                        "hi",
                        "stop",
                        100,
                        5,
                        AssistantMessageJson: """{"role":"assistant","content":"hi"}""");
                });

        var chunks = new List<string>();
        var payload = new
        {
            model = "client-model",
            stream = true,
            stream_options = new { include_usage = true },
            tools = new object[]
            {
                new { type = "function", function = new { name = "lookup", description = "Lookup." } }
            },
            messages = new object[]
            {
                new { role = "system", content = "You are helpful." },
                new { role = "user", content = "hi" }
            }
        };
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        var request = Comprexy.Api.Mapping.ChatCompletionRequestParser.Parse(
            document.RootElement.Clone(),
            "conv-stream-usage");

        var service = CreateService();
        await service.HandleStreamingAsync(
            request,
            _ => { },
            (chunk, _) =>
            {
                chunks.Add(chunk);
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.Equal("[DONE]", chunks[^1]);
        var usageChunk = Assert.Single(chunks, c => c.Contains("\"usage\"", StringComparison.Ordinal));
        using var usageDoc = JsonDocument.Parse(usageChunk);
        Assert.Equal(0, usageDoc.RootElement.GetProperty("choices").GetArrayLength());
        Assert.Equal(100, usageDoc.RootElement.GetProperty("usage").GetProperty("prompt_tokens").GetInt32());
        Assert.Equal(5, usageDoc.RootElement.GetProperty("usage").GetProperty("completion_tokens").GetInt32());
        Assert.Equal(
            80,
            usageDoc.RootElement.GetProperty("usage").GetProperty("prompt_tokens_details").GetProperty("cached_tokens").GetInt32());
    }

    [Fact]
    public async Task HandleStreamingAsync_EmitsReasoningBeforeContent()
    {
        _toolSchemaOptions = new ToolSchemaOptions
        {
            Mode = ToolSchemaMode.Off
        };
        _estimatedTokensToReturn = 10;

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
                    await onRawSseData(
                        """{"id":"chatcmpl-x","object":"chat.completion.chunk","model":"Qwen-35B","choices":[{"index":0,"delta":{"role":"assistant","reasoning_content":"think step by step"}}]}""",
                        token);
                    await onRawSseData(
                        """{"id":"chatcmpl-x","object":"chat.completion.chunk","model":"Qwen-35B","choices":[{"index":0,"delta":{"content":"visible answer"},"finish_reason":"stop"}]}""",
                        token);
                    await onRawSseData("[DONE]", token);
                    return new UpstreamChatResult(
                        "visible answer",
                        "stop",
                        10,
                        2,
                        AssistantMessageJson: """{"role":"assistant","content":"visible answer","reasoning_content":"think step by step"}""");
                });

        var chunks = new List<string>();
        var service = CreateService();
        await service.HandleStreamingAsync(
            BuildRequest(stream: true),
            _ => { },
            (chunk, _) =>
            {
                chunks.Add(chunk);
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.True(chunks.Count >= 3);
        using var first = JsonDocument.Parse(chunks[0]);
        Assert.Equal("chat.completion.chunk", first.RootElement.GetProperty("object").GetString());
        Assert.Equal("chatcmpl-x", first.RootElement.GetProperty("id").GetString());
        Assert.Equal("Qwen-35B", first.RootElement.GetProperty("model").GetString());
        Assert.Equal(0, first.RootElement.GetProperty("choices")[0].GetProperty("index").GetInt32());
        var firstDelta = first.RootElement.GetProperty("choices")[0].GetProperty("delta");
        Assert.Equal("assistant", firstDelta.GetProperty("role").GetString());
        Assert.Equal("think step by step", firstDelta.GetProperty("reasoning_content").GetString());
        Assert.False(firstDelta.TryGetProperty("content", out _));

        using var second = JsonDocument.Parse(chunks[1]);
        Assert.Equal("visible answer", second.RootElement.GetProperty("choices")[0].GetProperty("delta").GetProperty("content").GetString());
        Assert.False(second.RootElement.GetProperty("choices")[0].GetProperty("delta").TryGetProperty("role", out _));
        Assert.Equal("[DONE]", chunks[^1]);
    }

    [Fact]
    public async Task HandleStreamingAsync_ForwardsRefusalAndUsageEnvelope()
    {
        _toolSchemaOptions = new ToolSchemaOptions
        {
            Mode = ToolSchemaMode.Off
        };
        _estimatedTokensToReturn = 10;

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
                    await onRawSseData(
                        """{"id":"chatcmpl-1","object":"chat.completion.chunk","choices":[{"index":0,"delta":{"role":"assistant","content":"no","refusal":"blocked"}}]}""",
                        token);
                    await onRawSseData(
                        """{"id":"chatcmpl-1","object":"chat.completion.chunk","choices":[{"index":0,"delta":{},"finish_reason":"stop"}]}""",
                        token);
                    await onRawSseData(
                        """{"id":"chatcmpl-1","object":"chat.completion.chunk","choices":[],"usage":{"prompt_tokens":100,"completion_tokens":5,"total_tokens":105,"prompt_tokens_details":{"cached_tokens":80}}}""",
                        token);
                    await onRawSseData("[DONE]", token);
                    return new UpstreamChatResult(
                        "no",
                        "stop",
                        100,
                        5,
                        AssistantMessageJson: """{"role":"assistant","content":"no","refusal":"blocked"}""");
                });

        var chunks = new List<string>();
        var payload = new
        {
            model = "client-model",
            stream = true,
            stream_options = new { include_usage = true },
            tools = new object[]
            {
                new { type = "function", function = new { name = "lookup", description = "Lookup." } }
            },
            messages = new object[]
            {
                new { role = "system", content = "You are helpful." },
                new { role = "user", content = "hi" }
            }
        };
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        var request = Comprexy.Api.Mapping.ChatCompletionRequestParser.Parse(
            document.RootElement.Clone(),
            "conv-stream-refusal");

        var service = CreateService();
        await service.HandleStreamingAsync(
            request,
            _ => { },
            (chunk, _) =>
            {
                chunks.Add(chunk);
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.Contains(chunks, c => c.Contains("\"refusal\"", StringComparison.Ordinal) && c.Contains("blocked", StringComparison.Ordinal));
        var usageChunk = Assert.Single(chunks, c => c.Contains("\"usage\"", StringComparison.Ordinal));
        using var usageDoc = JsonDocument.Parse(usageChunk);
        Assert.Equal("chatcmpl-1", usageDoc.RootElement.GetProperty("id").GetString());
        Assert.Equal("chat.completion.chunk", usageDoc.RootElement.GetProperty("object").GetString());
        Assert.Equal(0, usageDoc.RootElement.GetProperty("choices").GetArrayLength());
        Assert.Equal(80, usageDoc.RootElement.GetProperty("usage").GetProperty("prompt_tokens_details").GetProperty("cached_tokens").GetInt32());
    }

    [Fact]
    public async Task HandleStreamingAsync_OmitsUsageChunkWithoutIncludeUsage()
    {
        _toolSchemaOptions = new ToolSchemaOptions
        {
            Mode = ToolSchemaMode.Off
        };
        _estimatedTokensToReturn = 10;

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
                    await onRawSseData("""{"choices":[{"delta":{"content":"hi"},"finish_reason":"stop"}]}""", token);
                    await onRawSseData("[DONE]", token);
                    return new UpstreamChatResult("hi", "stop", 100, 5);
                });

        var chunks = new List<string>();
        var service = CreateService();
        await service.HandleStreamingAsync(
            BuildRequest(stream: true),
            _ => { },
            (chunk, _) =>
            {
                chunks.Add(chunk);
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.DoesNotContain(chunks, c => c.Contains("\"usage\"", StringComparison.Ordinal));
        Assert.Equal("[DONE]", chunks[^1]);
    }

    [Fact]
    public async Task HandleStreamingAsync_ForwardsContentBeforeUpstreamStreamCompletes()
    {
        _toolSchemaOptions = new ToolSchemaOptions
        {
            Mode = ToolSchemaMode.Off
        };
        _estimatedTokensToReturn = 10;

        var firstChunkSeen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseStream = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

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
                    await onRawSseData("""{"choices":[{"delta":{"content":"live"}}]}""", token);
                    firstChunkSeen.TrySetResult();
                    await releaseStream.Task.WaitAsync(token);
                    await onRawSseData("""{"choices":[{"delta":{},"finish_reason":"stop"}]}""", token);
                    await onRawSseData("[DONE]", token);
                    return new UpstreamChatResult(
                        "live",
                        "stop",
                        1,
                        1,
                        AssistantMessageJson: """{"role":"assistant","content":"live"}""");
                });

        var chunks = new List<string>();
        var service = CreateService();
        var handleTask = service.HandleStreamingAsync(
            BuildRequest(stream: true),
            _ => { },
            (chunk, _) =>
            {
                chunks.Add(chunk);
                return Task.CompletedTask;
            },
            CancellationToken.None);

        await firstChunkSeen.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains(chunks, c => c.Contains("live", StringComparison.Ordinal));
        releaseStream.TrySetResult();
        await handleTask;
        Assert.Equal("[DONE]", chunks[^1]);
    }

    [Fact]
    public async Task HandleStreamingAsync_Virtual_SatisfiesConversationIdMetaLocallyAndStreamsFinalAnswer()
    {
        _toolSchemaOptions = new ToolSchemaOptions
        {
            Mode = ToolSchemaMode.Virtual
        };
        _estimatedTokensToReturn = 10;

        const string metaAssistantJson = """
            {"role":"assistant","content":null,"tool_calls":[{"id":"call_meta","type":"function","function":{"name":"comprexy_get_current_conversation_id","arguments":"{}"}}]}
            """;

        var streamCall = 0;
        _conversationRepository.Setup(r => r.FindByKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Conversation?)null);
        _workingMemoryRepository.Setup(r => r.GetLatestAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkingMemory?)null);
        SetupCompressionMapperForNonFileTools("lookup");
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
                    streamCall++;
                    if (streamCall == 1)
                    {
                        await onRawSseData(
                            """{"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call_meta","type":"function","function":{"name":"comprexy_get_current_conversation_id","arguments":"{}"}}]},"finish_reason":"tool_calls"}]}""",
                            token);
                        await onRawSseData("[DONE]", token);
                        return new UpstreamChatResult(
                            string.Empty,
                            "tool_calls",
                            10,
                            5,
                            AssistantMessageJson: metaAssistantJson);
                    }

                    await onRawSseData("""{"choices":[{"delta":{"content":"done"}}]}""", token);
                    await onRawSseData("""{"choices":[{"delta":{},"finish_reason":"stop"}]}""", token);
                    await onRawSseData("[DONE]", token);
                    return new UpstreamChatResult(
                        "done",
                        "stop",
                        12,
                        3,
                        AssistantMessageJson: """{"role":"assistant","content":"done"}""");
                });

        var chunks = new List<string>();
        var service = CreateService();
        var result = await service.HandleStreamingAsync(
            BuildRequest(stream: true),
            _ => { },
            (chunk, _) =>
            {
                chunks.Add(chunk);
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.Equal("done", result.AssistantContent);
        Assert.DoesNotContain(chunks, c => c.Contains("comprexy_get_current_conversation_id", StringComparison.Ordinal));
        Assert.DoesNotContain(chunks, c => c.Contains("get_tool_definition", StringComparison.Ordinal));
        Assert.Contains(chunks, c => c.Contains("done", StringComparison.Ordinal));
        Assert.Contains(chunks, c => c == "[DONE]");
        Assert.Equal(2, streamCall);
    }

    private void SetupCompressionMapperForNonFileTools(params string[] toolNames)
    {
        _chatCompletionClient
            .Setup(c => c.CompleteAsync(
                It.IsAny<ProviderEndpoint>(),
                It.Is<UpstreamRequest>(r => r.Purpose == UpstreamRequestPurpose.Compression),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProviderEndpoint _, UpstreamRequest request, CancellationToken _) =>
            {
                var user = request.Messages.First(m => m.Role == MessageRole.User).Content ?? string.Empty;
                var hashLine = user.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .First(line => line.StartsWith("schema_hash:", StringComparison.Ordinal));
                var hash = hashLine["schema_hash:".Length..].Trim();
                var capabilities = toolNames.Select(name => new
                {
                    client_tool = name,
                    capability = "NON_FILE",
                    risk = "low",
                    supports = new { path = false, offset = false, limit = false, query = false }
                }).ToArray();
                var mapping = JsonSerializer.Serialize(new
                {
                    schema_hash = hash,
                    client_capabilities = capabilities,
                    bindings = Array.Empty<object>()
                });
                return new UpstreamChatResult(mapping, "stop", 1, 1);
            });
    }

    private void SetupCompressionMapperForReadFileAndBash()
    {
        _chatCompletionClient
            .Setup(c => c.CompleteAsync(
                It.IsAny<ProviderEndpoint>(),
                It.Is<UpstreamRequest>(r => r.Purpose == UpstreamRequestPurpose.Compression),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProviderEndpoint _, UpstreamRequest request, CancellationToken _) =>
            {
                var user = request.Messages.First(m => m.Role == MessageRole.User).Content ?? string.Empty;
                var hashLine = user.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .First(line => line.StartsWith("schema_hash:", StringComparison.Ordinal));
                var hash = hashLine["schema_hash:".Length..].Trim();
                var mapping = JsonSerializer.Serialize(new
                {
                    schema_hash = hash,
                    client_capabilities = new object[]
                    {
                        new
                        {
                            client_tool = "read",
                            capability = "FILE_READ_RAW",
                            risk = "low",
                            supports = new { path = true, offset = true, limit = true, query = false }
                        },
                        new
                        {
                            client_tool = "bash",
                            capability = "NON_FILE",
                            risk = "high",
                            supports = new { path = false, offset = false, limit = false, query = false }
                        }
                    },
                    bindings = new object[]
                    {
                        new
                        {
                            comprexy_tool = "comprexy_read_file_range",
                            primary_client_tool = "read",
                            strategy = "direct",
                            arg_map = new { path = "filePath", start_line = "offset", end_line = "limit" }
                        },
                        new
                        {
                            comprexy_tool = "comprexy_read_file_manifest",
                            primary_client_tool = "read",
                            strategy = "direct",
                            arg_map = new { path = "filePath" }
                        }
                    }
                });
                return new UpstreamChatResult(mapping, "stop", 1, 1);
            });
    }

    [Fact]
    public async Task HandleAsync_InlineMode_RunsWrapUp_PersistsVisibleAssistant_AndSkipsQueue()
    {
        _policy.CompressionRetainMessageCount = 1;
        _estimatedTokensToReturn = 150;

        var conversation = Conversation.Create("header:inline-success", DateTimeOffset.UtcNow);
        conversation.CaptureSystemPromptIfAbsent("You are helpful.");
        conversation.SetSyncedMessageCount(2, DateTimeOffset.UtcNow);
        var stored = new List<ConversationMessage>
        {
            ConversationMessage.Create(conversation.Id, 0, MessageRole.User, "older", 5, DateTimeOffset.UtcNow)
        };
        var workingMemory = WorkingMemory.Create(
            conversation.Id,
            1,
            "# Working Memory\n## Current Goal\nEarlier",
            8,
            DateTimeOffset.UtcNow);

        _conversationRepository.Setup(r => r.FindByKeyAsync("header:inline-success", It.IsAny<CancellationToken>()))
            .ReturnsAsync(conversation);
        _messageRepository.Setup(r => r.GetByConversationIdAsync(conversation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);
        _workingMemoryRepository.Setup(r => r.GetLatestAsync(conversation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(workingMemory);

        var addedMessages = new List<ConversationMessage>();
        WorkingMemory? addedWorkingMemory = null;
        CompressionEvent? addedEvent = null;
        _messageRepository.Setup(r => r.Add(It.IsAny<ConversationMessage>()))
            .Callback<ConversationMessage>(message => addedMessages.Add(message));
        _workingMemoryRepository.Setup(r => r.Add(It.IsAny<WorkingMemory>()))
            .Callback<WorkingMemory>(wm => addedWorkingMemory = wm);
        _compressionEventRepository.Setup(r => r.Add(It.IsAny<CompressionEvent>()))
            .Callback<CompressionEvent>(evt => addedEvent = evt);

        var captured = new List<(ProviderEndpoint Endpoint, UpstreamRequest Request)>();
        _chatCompletionClient
            .Setup(c => c.CompleteAsync(It.IsAny<ProviderEndpoint>(), It.IsAny<UpstreamRequest>(), It.IsAny<CancellationToken>()))
            .Returns<ProviderEndpoint, UpstreamRequest, CancellationToken>((endpoint, request, _) =>
            {
                captured.Add((endpoint, request));
                if (IsWrapUpRequest(request))
                {
                    return Task.FromResult(new UpstreamChatResult(ValidWorkingMemory, "stop", 30, 8));
                }

                return Task.FromResult(new UpstreamChatResult(
                    "Visible answer",
                    "stop",
                    40,
                    12,
                    AssistantMessageJson: JsonSerializer.Serialize(new
                    {
                        role = "assistant",
                        content = "Visible answer",
                        reasoning_content = "hidden thoughts"
                    })));
            });
        var metrics = new Mock<IConversationMetricsRecorder>();
        metrics.SetupGet(m => m.IsEnabled).Returns(true);
        metrics.Setup(m => m.RecordSuccessfulTurnAsync(It.IsAny<SuccessfulTurnMetricInput>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        metrics.Setup(m => m.RecordCompressionOverheadAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var service = CreateService(metricsRecorder: metrics.Object);
        var result = await service.HandleAsync(
            BuildRequest(conversationHeader: "inline-success", userContent: "new tip"),
            CancellationToken.None);

        Assert.Equal("Visible answer", result.AssistantContent);
        Assert.Equal(2, captured.Count);
        Assert.Equal(UpstreamRequestPurpose.Chat, captured[0].Request.Purpose);
        Assert.DoesNotContain(
            captured[0].Request.Messages,
            m => m.Content == PromptFactory.BuildInlineWrapUpUserMessage().Content);
        Assert.True(IsWrapUpRequest(captured[1].Request));
        Assert.False(captured[1].Request.Stream);
        Assert.Equal(UpstreamRequestPurpose.Compression, captured[1].Request.Purpose);
        Assert.True(captured[1].Request.OriginalClientRequest.HasValue);
        Assert.True(captured[0].Request.OriginalClientRequest!.Value.TryGetProperty("tools", out _));
        Assert.True(captured[1].Request.OriginalClientRequest!.Value.TryGetProperty("tools", out _));
        Assert.Equal("none", captured[1].Request.OriginalClientRequest!.Value.GetProperty("tool_choice").GetString());
        if (captured[1].Request.RewrittenClientRequest is { } wrapRewritten)
        {
            Assert.True(wrapRewritten.TryGetProperty("tools", out _));
            Assert.Equal("none", wrapRewritten.GetProperty("tool_choice").GetString());
        }

        Assert.Equal(captured[0].Request.CallOptions, captured[1].Request.CallOptions);
        Assert.Equal("target-model", captured[1].Endpoint.Model);
        Assert.NotNull(captured[1].Request.Messages[^2].RawWireMessage);
        Assert.True(
            captured[1].Request.Messages[^2].RawWireMessage!.Value.TryGetProperty("reasoning_content", out _));
        Assert.Equal(PromptFactory.BuildInlineWrapUpUserMessage().Content, captured[1].Request.Messages[^1].Content);
        Assert.Contains("# Working Memory", captured[1].Request.Messages[^1].Content);
        // Stop-turn wrap shape: visible assistant then tip.
        Assert.Equal("Visible answer", captured[1].Request.Messages[^2].Content);
        Assert.Equal(MessageRole.Assistant, captured[1].Request.Messages[^2].Role);
        Assert.Equal(captured[0].Request.Messages.Count + 2, captured[1].Request.Messages.Count);

        Assert.NotNull(addedWorkingMemory);
        Assert.Equal(2, addedWorkingMemory!.Version);
        Assert.Contains("Inline summary", addedWorkingMemory.Content);
        Assert.NotNull(addedEvent);
        Assert.Equal(CompressionMode.Inline, addedEvent!.Mode);
        Assert.Equal(CompressionStatus.Succeeded, addedEvent.Status);
        Assert.Equal(2, addedEvent.WorkingMemoryVersionAfter);
        Assert.Equal(30, addedEvent.PromptTokens);
        Assert.Equal(8, addedEvent.CompletionTokens);
        var assistantEntity = Assert.Single(addedMessages, m => m.Role == MessageRole.Assistant);
        Assert.Equal("Visible answer", assistantEntity.Content);
        Assert.DoesNotContain(
            addedMessages,
            m => m.Content == PromptFactory.BuildInlineWrapUpUserMessage().Content);
        Assert.DoesNotContain(addedMessages, m => m.Content == ValidWorkingMemory);
        Assert.True(stored[0].IsFolded);
        Assert.Equal(2, stored[0].FoldedIntoWorkingMemoryVersion);
        // prepare/persist + inline wrap-up persist (dual-id clear uses isolated map UoW)
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        metrics.Verify(
            m => m.RecordCompressionOverheadAsync(conversation.Id, 38, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_InlineMode_WhenProviderModelUnset_StampsClientModelOnWrapUpEndpoint()
    {
        _policy.CompressionRetainMessageCount = 1;
        _estimatedTokensToReturn = 150;
        _providerModel = null;

        var conversation = Conversation.Create("header:inline-client-model", DateTimeOffset.UtcNow);
        conversation.CaptureSystemPromptIfAbsent("You are helpful.");
        conversation.SetSyncedMessageCount(2, DateTimeOffset.UtcNow);
        var stored = new List<ConversationMessage>
        {
            ConversationMessage.Create(conversation.Id, 0, MessageRole.User, "older", 5, DateTimeOffset.UtcNow)
        };
        var workingMemory = WorkingMemory.Create(
            conversation.Id,
            1,
            "# Working Memory\n## Current Goal\nEarlier",
            8,
            DateTimeOffset.UtcNow);

        _conversationRepository.Setup(r => r.FindByKeyAsync("header:inline-client-model", It.IsAny<CancellationToken>()))
            .ReturnsAsync(conversation);
        _messageRepository.Setup(r => r.GetByConversationIdAsync(conversation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);
        _workingMemoryRepository.Setup(r => r.GetLatestAsync(conversation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(workingMemory);

        ProviderEndpoint? wrapEndpoint = null;
        _chatCompletionClient
            .Setup(c => c.CompleteAsync(It.IsAny<ProviderEndpoint>(), It.IsAny<UpstreamRequest>(), It.IsAny<CancellationToken>()))
            .Returns<ProviderEndpoint, UpstreamRequest, CancellationToken>((endpoint, request, _) =>
            {
                if (IsWrapUpRequest(request))
                {
                    wrapEndpoint = endpoint;
                    return Task.FromResult(new UpstreamChatResult(ValidWorkingMemory, "stop", 30, 8));
                }

                return Task.FromResult(new UpstreamChatResult("Visible answer", "stop", 40, 12));
            });

        var service = CreateService();
        await service.HandleAsync(
            BuildRequest(conversationHeader: "inline-client-model", userContent: "new tip"),
            CancellationToken.None);

        Assert.NotNull(wrapEndpoint);
        Assert.Equal("client-model", wrapEndpoint!.Model);
    }


    [Fact]
    public async Task HandleAsync_InlineMode_DuringCooldown_SkipsWrapUpAndDoesNotCreateInlineEvent()
    {
        _policy.MinTurnsBetweenGenerations = 2;
        _estimatedTokensToReturn = 150;

        var now = DateTimeOffset.UtcNow;
        var conversation = Conversation.Create("header:inline-cooldown", now);
        conversation.CaptureSystemPromptIfAbsent("You are helpful.");
        conversation.SetSyncedMessageCount(2, now);
        var latestSuccess = CompressionEvent.Start(
            conversation.Id,
            CompressionMode.Inline,
            100,
            1,
            1,
            now.AddMinutes(-10));
        latestSuccess.Succeed(10, 2, now.AddMinutes(-5), 20, 5);
        var stored = new List<ConversationMessage>
        {
            ConversationMessage.Create(conversation.Id, 0, MessageRole.Assistant, "recent assistant", 5, now.AddMinutes(-1))
        };
        var workingMemory = WorkingMemory.Create(
            conversation.Id,
            2,
            "# Working Memory\n## Current Goal\nExisting",
            8,
            now.AddMinutes(-5));

        _conversationRepository.Setup(r => r.FindByKeyAsync("header:inline-cooldown", It.IsAny<CancellationToken>()))
            .ReturnsAsync(conversation);
        _messageRepository.Setup(r => r.GetByConversationIdAsync(conversation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);
        _workingMemoryRepository.Setup(r => r.GetLatestAsync(conversation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(workingMemory);
        _chatCompletionClient
            .Setup(c => c.CompleteAsync(It.IsAny<ProviderEndpoint>(), It.IsAny<UpstreamRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpstreamChatResult("normal answer", "stop", 10, 2));

        var service = CreateService();
        _compressionEventRepository
            .Setup(r => r.GetLatestSucceededAsync(conversation.Id, CompressionMode.Inline, It.IsAny<CancellationToken>()))
            .ReturnsAsync(latestSuccess);
        var result = await service.HandleAsync(
            BuildRequest(conversationHeader: "inline-cooldown", userContent: "next tip"),
            CancellationToken.None);

        Assert.Equal("normal answer", result.AssistantContent);
        _chatCompletionClient.Verify(
            c => c.CompleteAsync(
                It.IsAny<ProviderEndpoint>(),
                It.Is<UpstreamRequest>(r => IsWrapUpRequest(r)),
                It.IsAny<CancellationToken>()),
            Times.Never);
        _compressionEventRepository.Verify(r => r.Add(It.IsAny<CompressionEvent>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_InlineMode_OpenStore_NoClosedPrefix_SkipsWrapUp()
    {
        _estimatedTokensToReturn = 150;
        const string openAssistantWire =
            """{"role":"assistant","tool_calls":[{"id":"call_1","type":"function","function":{"name":"lookup","arguments":"{}"}}]}""";

        var now = DateTimeOffset.UtcNow;
        var conversation = Conversation.Create("header:inline-open-empty", now);
        conversation.CaptureSystemPromptIfAbsent("You are helpful.");
        conversation.SetSyncedMessageCount(1, now);
        var openAssistant = ConversationMessage.Create(
            conversation.Id,
            0,
            MessageRole.Assistant,
            string.Empty,
            10,
            now,
            openAssistantWire);
        var stored = new List<ConversationMessage> { openAssistant };
        var workingMemory = WorkingMemory.Create(
            conversation.Id,
            1,
            "# Working Memory\n## Current Goal\nExisting",
            8,
            now);

        _conversationRepository.Setup(r => r.FindByKeyAsync("header:inline-open-empty", It.IsAny<CancellationToken>()))
            .ReturnsAsync(conversation);
        _messageRepository.Setup(r => r.GetByConversationIdAsync(conversation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);
        _workingMemoryRepository.Setup(r => r.GetLatestAsync(conversation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(workingMemory);
        _chatCompletionClient
            .Setup(c => c.CompleteAsync(It.IsAny<ProviderEndpoint>(), It.IsAny<UpstreamRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpstreamChatResult("normal answer", "stop", 10, 2));

        // Tip is the same open assistant (no closed prefix remains after exclusion).
        var requestJson =
            """
            {
              "model":"client-model",
              "stream":false,
              "temperature":0.2,
              "tools":[{"type":"function","function":{"name":"lookup"}}],
              "messages":[
                {"role":"system","content":"You are helpful."},
                {"role":"assistant","tool_calls":[{"id":"call_1","type":"function","function":{"name":"lookup","arguments":"{}"}}]}
              ]
            }
            """;
        using var document = JsonDocument.Parse(requestJson);
        var request = Comprexy.Api.Mapping.ChatCompletionRequestParser.Parse(
            document.RootElement.Clone(),
            "inline-open-empty");

        var service = CreateService();
        await service.HandleAsync(request, CancellationToken.None);

        _chatCompletionClient.Verify(
            c => c.CompleteAsync(
                It.IsAny<ProviderEndpoint>(),
                It.Is<UpstreamRequest>(r => IsWrapUpRequest(r)),
                It.IsAny<CancellationToken>()),
            Times.Never);
        _compressionEventRepository.Verify(r => r.Add(It.IsAny<CompressionEvent>()), Times.Never);
        _workingMemoryRepository.Verify(r => r.Add(It.IsAny<WorkingMemory>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_InlineMode_OpenStore_Unrepairable_SkipsWrapUp()
    {
        _estimatedTokensToReturn = 150;

        var now = DateTimeOffset.UtcNow;
        var conversation = Conversation.Create("header:inline-open-unrepairable", now);
        conversation.CaptureSystemPromptIfAbsent("You are helpful.");
        conversation.SetSyncedMessageCount(2, now);
        const string openAssistantWire =
            """{"role":"assistant","tool_calls":[{"id":"call_1","type":"function","function":{"name":"lookup","arguments":"{}"}}]}""";
        var stored = new List<ConversationMessage>
        {
            ConversationMessage.Create(
                conversation.Id, 0, MessageRole.Assistant, string.Empty, 10, now, openAssistantWire),
            ConversationMessage.Create(
                conversation.Id,
                1,
                MessageRole.Tool,
                "no id",
                4,
                now,
                """{"role":"tool","content":"no id"}""")
        };
        var workingMemory = WorkingMemory.Create(
            conversation.Id,
            1,
            "# Working Memory\n## Current Goal\nExisting",
            8,
            now);

        _conversationRepository.Setup(r => r.FindByKeyAsync("header:inline-open-unrepairable", It.IsAny<CancellationToken>()))
            .ReturnsAsync(conversation);
        _messageRepository.Setup(r => r.GetByConversationIdAsync(conversation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);
        _workingMemoryRepository.Setup(r => r.GetLatestAsync(conversation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(workingMemory);
        _chatCompletionClient
            .Setup(c => c.CompleteAsync(It.IsAny<ProviderEndpoint>(), It.IsAny<UpstreamRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpstreamChatResult("normal answer", "stop", 10, 2));

        var service = CreateService();
        await service.HandleAsync(
            BuildRequest(conversationHeader: "inline-open-unrepairable", userContent: "next hop"),
            CancellationToken.None);

        _chatCompletionClient.Verify(
            c => c.CompleteAsync(
                It.IsAny<ProviderEndpoint>(),
                It.Is<UpstreamRequest>(r => IsWrapUpRequest(r)),
                It.IsAny<CancellationToken>()),
            Times.Never);
        _compressionEventRepository.Verify(r => r.Add(It.IsAny<CompressionEvent>()), Times.Never);
        _workingMemoryRepository.Verify(r => r.Add(It.IsAny<WorkingMemory>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_InlineMode_OpenStoreEmergency_TipAwaiting_RunsMidChainAccept()
    {
        _policy.CompressionRetainMessageCount = 1;
        _estimatedTokensToReturn = 150;
        _cacheAlignmentOptions = new CacheAlignmentOptions { Enabled = false, MaxConversations = 1024 };

        var now = DateTimeOffset.UtcNow;
        var conversation = Conversation.Create("header:inline-open-emergency", now);
        conversation.CaptureSystemPromptIfAbsent("You are helpful.");
        conversation.SetSyncedMessageCount(2, now);
        const string priorAssistantWire =
            """{"role":"assistant","tool_calls":[{"id":"call_prior","type":"function","function":{"name":"lookup","arguments":"{}"}}]}""";
        const string priorToolWire =
            """{"role":"tool","tool_call_id":"call_prior","content":"prior result"}""";
        const string openTipWire =
            """{"role":"assistant","tool_calls":[{"id":"call_open","type":"function","function":{"name":"lookup","arguments":"{}"}}]}""";
        var olderUser = ConversationMessage.Create(conversation.Id, 0, MessageRole.User, "older context", 5, now);
        var priorAssistant = ConversationMessage.Create(
            conversation.Id, 1, MessageRole.Assistant, string.Empty, 8, now, priorAssistantWire);
        var priorTool = ConversationMessage.Create(
            conversation.Id, 2, MessageRole.Tool, "prior result", 4, now, priorToolWire);
        var openTip = ConversationMessage.Create(
            conversation.Id, 3, MessageRole.Assistant, string.Empty, 8, now, openTipWire);
        var stored = new List<ConversationMessage> { olderUser, priorAssistant, priorTool, openTip };
        var workingMemory = WorkingMemory.Create(
            conversation.Id,
            1,
            "# Working Memory\n## Current Goal\nEarlier",
            8,
            now);

        _conversationRepository.Setup(r => r.FindByKeyAsync("header:inline-open-emergency", It.IsAny<CancellationToken>()))
            .ReturnsAsync(conversation);
        _messageRepository.Setup(r => r.GetByConversationIdAsync(conversation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);
        _workingMemoryRepository.Setup(r => r.GetLatestAsync(conversation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(workingMemory);

        WorkingMemory? addedWorkingMemory = null;
        CompressionEvent? addedEvent = null;
        _workingMemoryRepository.Setup(r => r.Add(It.IsAny<WorkingMemory>()))
            .Callback<WorkingMemory>(wm => addedWorkingMemory = wm);
        _compressionEventRepository.Setup(r => r.Add(It.IsAny<CompressionEvent>()))
            .Callback<CompressionEvent>(evt => addedEvent = evt);

        var captured = new List<UpstreamRequest>();
        _chatCompletionClient
            .Setup(c => c.CompleteAsync(It.IsAny<ProviderEndpoint>(), It.IsAny<UpstreamRequest>(), It.IsAny<CancellationToken>()))
            .Returns<ProviderEndpoint, UpstreamRequest, CancellationToken>((_, request, _) =>
            {
                captured.Add(request);
                if (IsWrapUpRequest(request))
                {
                    return Task.FromResult(new UpstreamChatResult(ValidWorkingMemory, "stop", 30, 8));
                }

                return Task.FromResult(new UpstreamChatResult("normal answer", "stop", 40, 12));
            });

        var service = CreateService();
        var result = await service.HandleAsync(
            BuildRequest(conversationHeader: "inline-open-emergency", userContent: "next hop"),
            CancellationToken.None);

        Assert.Equal("stop", result.FinishReason);
        Assert.Equal(2, captured.Count);
        Assert.True(IsWrapUpRequest(captured[1]));
        AssertWrapUpMessagesHaveNoOpenToolCalls(captured[1].Messages);
        Assert.Equal(PromptFactory.BuildInlineWrapUpUserMessage().Content, captured[1].Messages[^1].Content);
        // Live still carries the open tip; emergency wrap rebuilds a closed-world corpus without it.
        Assert.Contains(captured[0].Messages, m => MessageHasToolCallId(m, "call_open"));
        Assert.DoesNotContain(captured[1].Messages, m => MessageHasToolCallId(m, "call_open"));

        Assert.False(openTip.IsFolded);
        Assert.True(olderUser.IsFolded);
        Assert.NotNull(addedWorkingMemory);
        Assert.Equal(2, addedWorkingMemory!.Version);
        Assert.NotNull(addedEvent);
        Assert.Equal(CompressionStatus.Succeeded, addedEvent!.Status);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task HandleAsync_InlineMode_OpenStoreEmergency_PartialBatch_DoesNotFoldOpenAssistantOrTools()
    {
        _policy.CompressionRetainMessageCount = 1;
        _estimatedTokensToReturn = 150;
        _cacheAlignmentOptions = new CacheAlignmentOptions { Enabled = false, MaxConversations = 1024 };

        var now = DateTimeOffset.UtcNow;
        var conversation = Conversation.Create("header:inline-open-partial", now);
        conversation.CaptureSystemPromptIfAbsent("You are helpful.");
        conversation.SetSyncedMessageCount(2, now);
        const string closedAssistantWire =
            """{"role":"assistant","tool_calls":[{"id":"call_closed","type":"function","function":{"name":"lookup","arguments":"{}"}}]}""";
        const string closedToolWire =
            """{"role":"tool","tool_call_id":"call_closed","content":"closed result"}""";
        const string openAssistantWire =
            """{"role":"assistant","tool_calls":[{"id":"call_a","type":"function","function":{"name":"lookup","arguments":"{}"}},{"id":"call_b","type":"function","function":{"name":"lookup","arguments":"{}"}}]}""";
        const string matchedToolWire =
            """{"role":"tool","tool_call_id":"call_a","content":"partial a"}""";
        var olderUser = ConversationMessage.Create(conversation.Id, 0, MessageRole.User, "older", 5, now);
        var closedAssistant = ConversationMessage.Create(
            conversation.Id, 1, MessageRole.Assistant, string.Empty, 8, now, closedAssistantWire);
        var closedTool = ConversationMessage.Create(
            conversation.Id, 2, MessageRole.Tool, "closed result", 4, now, closedToolWire);
        var openAssistant = ConversationMessage.Create(
            conversation.Id, 3, MessageRole.Assistant, string.Empty, 8, now, openAssistantWire);
        var matchedTool = ConversationMessage.Create(
            conversation.Id, 4, MessageRole.Tool, "partial a", 4, now, matchedToolWire);
        var stored = new List<ConversationMessage>
        {
            olderUser, closedAssistant, closedTool, openAssistant, matchedTool
        };
        var workingMemory = WorkingMemory.Create(
            conversation.Id,
            1,
            "# Working Memory\n## Current Goal\nEarlier",
            8,
            now);

        _conversationRepository.Setup(r => r.FindByKeyAsync("header:inline-open-partial", It.IsAny<CancellationToken>()))
            .ReturnsAsync(conversation);
        _messageRepository.Setup(r => r.GetByConversationIdAsync(conversation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);
        _workingMemoryRepository.Setup(r => r.GetLatestAsync(conversation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(workingMemory);

        WorkingMemory? addedWorkingMemory = null;
        CompressionEvent? addedEvent = null;
        _workingMemoryRepository.Setup(r => r.Add(It.IsAny<WorkingMemory>()))
            .Callback<WorkingMemory>(wm => addedWorkingMemory = wm);
        _compressionEventRepository.Setup(r => r.Add(It.IsAny<CompressionEvent>()))
            .Callback<CompressionEvent>(evt => addedEvent = evt);

        UpstreamRequest? wrapRequest = null;
        _chatCompletionClient
            .Setup(c => c.CompleteAsync(It.IsAny<ProviderEndpoint>(), It.IsAny<UpstreamRequest>(), It.IsAny<CancellationToken>()))
            .Returns<ProviderEndpoint, UpstreamRequest, CancellationToken>((_, request, _) =>
            {
                if (IsWrapUpRequest(request))
                {
                    wrapRequest = request;
                    return Task.FromResult(new UpstreamChatResult(ValidWorkingMemory, "stop", 30, 8));
                }

                return Task.FromResult(new UpstreamChatResult("normal answer", "stop", 40, 12));
            });

        var service = CreateService();
        await service.HandleAsync(
            BuildRequest(conversationHeader: "inline-open-partial", userContent: "next hop"),
            CancellationToken.None);

        Assert.NotNull(wrapRequest);
        AssertWrapUpMessagesHaveNoOpenToolCalls(wrapRequest!.Messages);
        Assert.NotNull(addedWorkingMemory);
        Assert.Equal(CompressionStatus.Succeeded, addedEvent!.Status);
        Assert.False(openAssistant.IsFolded);
        Assert.False(matchedTool.IsFolded);
        Assert.True(olderUser.IsFolded);
    }

    [Fact]
    public async Task HandleAsync_InlineMode_OpenStoreEmergency_FoldUnrepairable_SoftFailsWithoutWm()
    {
        _policy.CompressionRetainMessageCount = 1;
        _estimatedTokensToReturn = 150;
        _cacheAlignmentOptions = new CacheAlignmentOptions { Enabled = false, MaxConversations = 1024 };

        var now = DateTimeOffset.UtcNow;
        var conversation = Conversation.Create("header:inline-open-fold-unrepairable", now);
        conversation.CaptureSystemPromptIfAbsent("You are helpful.");
        conversation.SetSyncedMessageCount(2, now);
        const string priorAssistantWire =
            """{"role":"assistant","tool_calls":[{"id":"call_prior","type":"function","function":{"name":"lookup","arguments":"{}"}}]}""";
        const string priorToolWire =
            """{"role":"tool","tool_call_id":"call_prior","content":"prior result"}""";
        const string openTipWire =
            """{"role":"assistant","tool_calls":[{"id":"call_open","type":"function","function":{"name":"lookup","arguments":"{}"}}]}""";
        var olderUser = ConversationMessage.Create(conversation.Id, 0, MessageRole.User, "older context", 5, now);
        var priorAssistant = ConversationMessage.Create(
            conversation.Id, 1, MessageRole.Assistant, string.Empty, 8, now, priorAssistantWire);
        var priorTool = ConversationMessage.Create(
            conversation.Id, 2, MessageRole.Tool, "prior result", 4, now, priorToolWire);
        var openTip = ConversationMessage.Create(
            conversation.Id, 3, MessageRole.Assistant, string.Empty, 8, now, openTipWire);
        var repairableStore = new List<ConversationMessage> { olderUser, priorAssistant, priorTool, openTip };
        var unrepairableStore = new List<ConversationMessage>
        {
            ConversationMessage.Create(
                conversation.Id, 0, MessageRole.Assistant, string.Empty, 8, now, openTipWire),
            ConversationMessage.Create(
                conversation.Id,
                1,
                MessageRole.Tool,
                "no id",
                4,
                now,
                """{"role":"tool","content":"no id"}""")
        };

        var workingMemory = WorkingMemory.Create(
            conversation.Id,
            1,
            "# Working Memory\n## Current Goal\nEarlier",
            8,
            now);

        var getCount = 0;
        _conversationRepository.Setup(r => r.FindByKeyAsync("header:inline-open-fold-unrepairable", It.IsAny<CancellationToken>()))
            .ReturnsAsync(conversation);
        _messageRepository.Setup(r => r.GetByConversationIdAsync(conversation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                getCount++;
                // Prepare sees a repairable open store; wrap-up fold sees an unrepairable frontier.
                return getCount == 1 ? repairableStore.ToList() : unrepairableStore.ToList();
            });
        _messageRepository.Setup(r => r.Add(It.IsAny<ConversationMessage>()));
        _workingMemoryRepository.Setup(r => r.GetLatestAsync(conversation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(workingMemory);

        CompressionEvent? addedEvent = null;
        _compressionEventRepository.Setup(r => r.Add(It.IsAny<CompressionEvent>()))
            .Callback<CompressionEvent>(evt => addedEvent = evt);

        _chatCompletionClient
            .Setup(c => c.CompleteAsync(It.IsAny<ProviderEndpoint>(), It.IsAny<UpstreamRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpstreamChatResult("normal answer", "stop", 40, 12));

        var service = CreateService();
        await service.HandleAsync(
            BuildRequest(conversationHeader: "inline-open-fold-unrepairable", userContent: "next hop"),
            CancellationToken.None);

        Assert.NotNull(addedEvent);
        Assert.Equal(CompressionStatus.Failed, addedEvent!.Status);
        Assert.Equal("wrapup_fold_unrepairable", addedEvent.ErrorMessage);
        Assert.False(openTip.IsFolded);
        Assert.False(olderUser.IsFolded);
        _workingMemoryRepository.Verify(r => r.Add(It.IsAny<WorkingMemory>()), Times.Never);
        _chatCompletionClient.Verify(
            c => c.CompleteAsync(
                It.IsAny<ProviderEndpoint>(),
                It.Is<UpstreamRequest>(r => IsWrapUpRequest(r)),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_InlineMode_OpenStoreEmergency_WhenWrapUpReturnsToolCalls_SoftFailsWithoutWm()
    {
        _policy.CompressionRetainMessageCount = 1;
        _estimatedTokensToReturn = 150;
        _cacheAlignmentOptions = new CacheAlignmentOptions { Enabled = false, MaxConversations = 1024 };

        var now = DateTimeOffset.UtcNow;
        var conversation = Conversation.Create("header:inline-open-softfail", now);
        conversation.CaptureSystemPromptIfAbsent("You are helpful.");
        conversation.SetSyncedMessageCount(2, now);
        const string priorAssistantWire =
            """{"role":"assistant","tool_calls":[{"id":"call_prior","type":"function","function":{"name":"lookup","arguments":"{}"}}]}""";
        const string priorToolWire =
            """{"role":"tool","tool_call_id":"call_prior","content":"prior result"}""";
        const string openTipWire =
            """{"role":"assistant","tool_calls":[{"id":"call_open","type":"function","function":{"name":"lookup","arguments":"{}"}}]}""";
        var olderUser = ConversationMessage.Create(conversation.Id, 0, MessageRole.User, "older context", 5, now);
        var priorAssistant = ConversationMessage.Create(
            conversation.Id, 1, MessageRole.Assistant, string.Empty, 8, now, priorAssistantWire);
        var priorTool = ConversationMessage.Create(
            conversation.Id, 2, MessageRole.Tool, "prior result", 4, now, priorToolWire);
        var openTip = ConversationMessage.Create(
            conversation.Id, 3, MessageRole.Assistant, string.Empty, 8, now, openTipWire);
        var stored = new List<ConversationMessage> { olderUser, priorAssistant, priorTool, openTip };
        var workingMemory = WorkingMemory.Create(
            conversation.Id,
            1,
            "# Working Memory\n## Current Goal\nEarlier",
            8,
            now);

        _conversationRepository.Setup(r => r.FindByKeyAsync("header:inline-open-softfail", It.IsAny<CancellationToken>()))
            .ReturnsAsync(conversation);
        _messageRepository.Setup(r => r.GetByConversationIdAsync(conversation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);
        _workingMemoryRepository.Setup(r => r.GetLatestAsync(conversation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(workingMemory);

        CompressionEvent? addedEvent = null;
        _compressionEventRepository.Setup(r => r.Add(It.IsAny<CompressionEvent>()))
            .Callback<CompressionEvent>(evt => addedEvent = evt);

        _chatCompletionClient
            .Setup(c => c.CompleteAsync(It.IsAny<ProviderEndpoint>(), It.IsAny<UpstreamRequest>(), It.IsAny<CancellationToken>()))
            .Returns<ProviderEndpoint, UpstreamRequest, CancellationToken>((_, request, _) =>
            {
                if (IsWrapUpRequest(request))
                {
                    return Task.FromResult(new UpstreamChatResult(
                        Content: string.Empty,
                        FinishReason: "tool_calls",
                        PromptTokens: 30,
                        CompletionTokens: 8,
                        AssistantMessageJson:
                        """{"role":"assistant","content":null,"tool_calls":[{"id":"call_wrap","type":"function","function":{"name":"lookup","arguments":"{}"}}]}"""));
                }

                return Task.FromResult(new UpstreamChatResult("normal answer", "stop", 40, 12));
            });

        var service = CreateService();
        await service.HandleAsync(
            BuildRequest(conversationHeader: "inline-open-softfail", userContent: "next hop"),
            CancellationToken.None);

        Assert.NotNull(addedEvent);
        Assert.Equal(CompressionStatus.Failed, addedEvent!.Status);
        Assert.Equal("wrapup_tool_calls", addedEvent.ErrorMessage);
        Assert.False(openTip.IsFolded);
        Assert.False(olderUser.IsFolded);
        _workingMemoryRepository.Verify(r => r.Add(It.IsAny<WorkingMemory>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_InlineMode_OpenStoreEmergency_DuringCooldown_SkipsWrapUp()
    {
        // Two stored assistants after last success → need MinTurns > 2 to stay in cooldown.
        _policy.MinTurnsBetweenGenerations = 3;
        _estimatedTokensToReturn = 150;

        var now = DateTimeOffset.UtcNow;
        var conversation = Conversation.Create("header:inline-open-cooldown", now);
        conversation.CaptureSystemPromptIfAbsent("You are helpful.");
        conversation.SetSyncedMessageCount(2, now);
        var latestSuccess = CompressionEvent.Start(
            conversation.Id,
            CompressionMode.Inline,
            100,
            1,
            1,
            now.AddMinutes(-10));
        latestSuccess.Succeed(10, 2, now.AddMinutes(-5), 20, 5);
        const string priorAssistantWire =
            """{"role":"assistant","tool_calls":[{"id":"call_prior","type":"function","function":{"name":"lookup","arguments":"{}"}}]}""";
        const string priorToolWire =
            """{"role":"tool","tool_call_id":"call_prior","content":"prior result"}""";
        const string openTipWire =
            """{"role":"assistant","tool_calls":[{"id":"call_open","type":"function","function":{"name":"lookup","arguments":"{}"}}]}""";
        var stored = new List<ConversationMessage>
        {
            ConversationMessage.Create(conversation.Id, 0, MessageRole.User, "older", 5, now.AddMinutes(-2)),
            ConversationMessage.Create(
                conversation.Id, 1, MessageRole.Assistant, string.Empty, 8, now.AddMinutes(-1), priorAssistantWire),
            ConversationMessage.Create(
                conversation.Id, 2, MessageRole.Tool, "prior result", 4, now.AddSeconds(-45), priorToolWire),
            ConversationMessage.Create(
                conversation.Id, 3, MessageRole.Assistant, string.Empty, 8, now.AddSeconds(-30), openTipWire)
        };
        var workingMemory = WorkingMemory.Create(
            conversation.Id,
            2,
            "# Working Memory\n## Current Goal\nExisting",
            8,
            now.AddMinutes(-5));

        _conversationRepository.Setup(r => r.FindByKeyAsync("header:inline-open-cooldown", It.IsAny<CancellationToken>()))
            .ReturnsAsync(conversation);
        _messageRepository.Setup(r => r.GetByConversationIdAsync(conversation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);
        _workingMemoryRepository.Setup(r => r.GetLatestAsync(conversation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(workingMemory);
        _chatCompletionClient
            .Setup(c => c.CompleteAsync(It.IsAny<ProviderEndpoint>(), It.IsAny<UpstreamRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpstreamChatResult("normal answer", "stop", 10, 2));

        var service = CreateService();
        _compressionEventRepository
            .Setup(r => r.GetLatestSucceededAsync(conversation.Id, CompressionMode.Inline, It.IsAny<CancellationToken>()))
            .ReturnsAsync(latestSuccess);
        await service.HandleAsync(
            BuildRequest(conversationHeader: "inline-open-cooldown", userContent: "next hop"),
            CancellationToken.None);

        _chatCompletionClient.Verify(
            c => c.CompleteAsync(
                It.IsAny<ProviderEndpoint>(),
                It.Is<UpstreamRequest>(r => IsWrapUpRequest(r)),
                It.IsAny<CancellationToken>()),
            Times.Never);
        _compressionEventRepository.Verify(r => r.Add(It.IsAny<CompressionEvent>()), Times.Never);
    }

    private static bool MessageHasToolCallId(ChatMessage message, string toolCallId)
    {
        if (message.Role != MessageRole.Assistant || message.RawWireMessage is not { } wire)
        {
            return false;
        }

        if (!wire.TryGetProperty("tool_calls", out var toolCalls)
            || toolCalls.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var call in toolCalls.EnumerateArray())
        {
            if (call.TryGetProperty("id", out var id)
                && id.ValueKind == JsonValueKind.String
                && string.Equals(id.GetString(), toolCallId, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static void AssertWrapUpMessagesHaveNoOpenToolCalls(IReadOnlyList<ChatMessage> messages)
    {
        var toolResults = new HashSet<string>(StringComparer.Ordinal);
        foreach (var message in messages)
        {
            if (message.Role != MessageRole.Tool || message.RawWireMessage is not { } toolWire)
            {
                continue;
            }

            if (toolWire.TryGetProperty("tool_call_id", out var toolCallId)
                && toolCallId.ValueKind == JsonValueKind.String
                && toolCallId.GetString() is { } id)
            {
                toolResults.Add(id);
            }
        }

        foreach (var message in messages)
        {
            if (message.Role != MessageRole.Assistant || message.RawWireMessage is not { } wire)
            {
                continue;
            }

            if (!wire.TryGetProperty("tool_calls", out var toolCalls)
                || toolCalls.ValueKind != JsonValueKind.Array
                || toolCalls.GetArrayLength() == 0)
            {
                continue;
            }

            foreach (var call in toolCalls.EnumerateArray())
            {
                if (!call.TryGetProperty("id", out var idEl)
                    || idEl.ValueKind != JsonValueKind.String
                    || idEl.GetString() is not { } announcedId)
                {
                    continue;
                }

                Assert.True(
                    toolResults.Contains(announcedId),
                    "Wrap-up corpus must not include unresolved assistant tool_calls.");
            }
        }
    }

    [Fact]
    public async Task HandleAsync_InlineMode_MidChainPrefix_RunsWrapUp_TipOnly_LeavesOpenAssistantUnfolded()
    {
        _policy.CompressionRetainMessageCount = 1;
        _estimatedTokensToReturn = 150;

        var now = DateTimeOffset.UtcNow;
        var conversation = Conversation.Create("header:inline-mid-chain", now);
        conversation.CaptureSystemPromptIfAbsent("You are helpful.");
        conversation.SetSyncedMessageCount(2, now);
        const string priorAssistantWire =
            """{"role":"assistant","tool_calls":[{"id":"call_prior","type":"function","function":{"name":"lookup","arguments":"{}"}}]}""";
        const string priorToolWire =
            """{"role":"tool","tool_call_id":"call_prior","content":"prior result"}""";
        var olderUser = ConversationMessage.Create(conversation.Id, 0, MessageRole.User, "older context", 5, now);
        var priorAssistant = ConversationMessage.Create(
            conversation.Id, 1, MessageRole.Assistant, string.Empty, 8, now, priorAssistantWire);
        var priorTool = ConversationMessage.Create(
            conversation.Id, 2, MessageRole.Tool, "prior result", 4, now, priorToolWire);
        var stored = new List<ConversationMessage> { olderUser, priorAssistant, priorTool };
        var workingMemory = WorkingMemory.Create(
            conversation.Id,
            1,
            "# Working Memory\n## Current Goal\nEarlier",
            8,
            now);

        _conversationRepository.Setup(r => r.FindByKeyAsync("header:inline-mid-chain", It.IsAny<CancellationToken>()))
            .ReturnsAsync(conversation);
        _messageRepository.Setup(r => r.GetByConversationIdAsync(conversation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);
        _workingMemoryRepository.Setup(r => r.GetLatestAsync(conversation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(workingMemory);

        var addedMessages = new List<ConversationMessage>();
        WorkingMemory? addedWorkingMemory = null;
        CompressionEvent? addedEvent = null;
        _messageRepository.Setup(r => r.Add(It.IsAny<ConversationMessage>()))
            .Callback<ConversationMessage>(message => addedMessages.Add(message));
        _workingMemoryRepository.Setup(r => r.Add(It.IsAny<WorkingMemory>()))
            .Callback<WorkingMemory>(wm => addedWorkingMemory = wm);
        _compressionEventRepository.Setup(r => r.Add(It.IsAny<CompressionEvent>()))
            .Callback<CompressionEvent>(evt => addedEvent = evt);

        const string openAssistantWire =
            """{"role":"assistant","content":null,"tool_calls":[{"id":"call_new","type":"function","function":{"name":"lookup","arguments":"{\"q\":\"x\"}"}}]}""";
        var captured = new List<UpstreamRequest>();
        _chatCompletionClient
            .Setup(c => c.CompleteAsync(It.IsAny<ProviderEndpoint>(), It.IsAny<UpstreamRequest>(), It.IsAny<CancellationToken>()))
            .Returns<ProviderEndpoint, UpstreamRequest, CancellationToken>((_, request, _) =>
            {
                captured.Add(request);
                if (IsWrapUpRequest(request))
                {
                    return Task.FromResult(new UpstreamChatResult(ValidWorkingMemory, "stop", 30, 8));
                }

                return Task.FromResult(new UpstreamChatResult(
                    Content: string.Empty,
                    FinishReason: "tool_calls",
                    PromptTokens: 40,
                    CompletionTokens: 12,
                    AssistantMessageJson: openAssistantWire));
            });

        var service = CreateService();
        var result = await service.HandleAsync(
            BuildRequest(conversationHeader: "inline-mid-chain", userContent: "next hop"),
            CancellationToken.None);

        Assert.Equal("tool_calls", result.FinishReason);
        Assert.Equal(2, captured.Count);
        Assert.True(IsWrapUpRequest(captured[1]));
        Assert.True(captured[0].OriginalClientRequest!.Value.TryGetProperty("tools", out _));
        Assert.True(captured[1].OriginalClientRequest!.Value.TryGetProperty("tools", out _));
        Assert.Equal("none", captured[1].OriginalClientRequest!.Value.GetProperty("tool_choice").GetString());
        Assert.Equal(captured[0].Messages.Count + 1, captured[1].Messages.Count);
        Assert.Equal(PromptFactory.BuildInlineWrapUpUserMessage().Content, captured[1].Messages[^1].Content);
        var penultimate = captured[1].Messages[^2];
        Assert.False(
            penultimate.Role == MessageRole.Assistant
            && penultimate.RawWireMessage is { } wire
            && wire.TryGetProperty("tool_calls", out var toolCalls)
            && toolCalls.ValueKind == JsonValueKind.Array
            && toolCalls.GetArrayLength() > 0);

        var openAssistant = Assert.Single(addedMessages, m => m.Role == MessageRole.Assistant && m.RawWireJson == openAssistantWire);
        Assert.False(openAssistant.IsFolded);
        Assert.True(olderUser.IsFolded);
        Assert.False(priorAssistant.IsFolded);
        Assert.False(priorTool.IsFolded);

        Assert.NotNull(addedWorkingMemory);
        Assert.Equal(2, addedWorkingMemory!.Version);
        Assert.Contains("Inline summary", addedWorkingMemory.Content);
        Assert.NotNull(addedEvent);
        Assert.Equal(CompressionMode.Inline, addedEvent!.Mode);
        Assert.Equal(CompressionStatus.Succeeded, addedEvent.Status);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task HandleAsync_InlineMode_MidChainPrefix_WhenWrapUpFails_KeepsClientToolCallsAndPriorWm()
    {
        _policy.CompressionRetainMessageCount = 1;
        _estimatedTokensToReturn = 150;

        var now = DateTimeOffset.UtcNow;
        var conversation = Conversation.Create("header:inline-mid-fail", now);
        conversation.CaptureSystemPromptIfAbsent("You are helpful.");
        conversation.SetSyncedMessageCount(2, now);
        // Single closed-prefix message so retain window covers the entire fold universe → empty_fold.
        var olderUser = ConversationMessage.Create(conversation.Id, 0, MessageRole.User, "only tip", 5, now);
        var stored = new List<ConversationMessage> { olderUser };
        var workingMemory = WorkingMemory.Create(
            conversation.Id,
            1,
            "# Working Memory\n## Current Goal\nEarlier",
            8,
            now);

        _conversationRepository.Setup(r => r.FindByKeyAsync("header:inline-mid-fail", It.IsAny<CancellationToken>()))
            .ReturnsAsync(conversation);
        _messageRepository.Setup(r => r.GetByConversationIdAsync(conversation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);
        _workingMemoryRepository.Setup(r => r.GetLatestAsync(conversation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(workingMemory);

        var addedMessages = new List<ConversationMessage>();
        CompressionEvent? addedEvent = null;
        _messageRepository.Setup(r => r.Add(It.IsAny<ConversationMessage>()))
            .Callback<ConversationMessage>(message => addedMessages.Add(message));
        _compressionEventRepository.Setup(r => r.Add(It.IsAny<CompressionEvent>()))
            .Callback<CompressionEvent>(evt => addedEvent = evt);

        const string openAssistantWire =
            """{"role":"assistant","content":null,"tool_calls":[{"id":"call_new","type":"function","function":{"name":"lookup","arguments":"{}"}}]}""";
        _chatCompletionClient
            .Setup(c => c.CompleteAsync(It.IsAny<ProviderEndpoint>(), It.IsAny<UpstreamRequest>(), It.IsAny<CancellationToken>()))
            .Returns<ProviderEndpoint, UpstreamRequest, CancellationToken>((_, request, _) =>
            {
                if (IsWrapUpRequest(request))
                {
                    return Task.FromResult(new UpstreamChatResult(ValidWorkingMemory, "stop", 30, 8));
                }

                return Task.FromResult(new UpstreamChatResult(
                    Content: string.Empty,
                    FinishReason: "tool_calls",
                    PromptTokens: 40,
                    CompletionTokens: 12,
                    AssistantMessageJson: openAssistantWire));
            });

        var service = CreateService();
        var result = await service.HandleAsync(
            BuildRequest(conversationHeader: "inline-mid-fail", userContent: "next hop"),
            CancellationToken.None);

        Assert.Equal("tool_calls", result.FinishReason);
        var openAssistant = Assert.Single(addedMessages, m => m.Role == MessageRole.Assistant && m.RawWireJson == openAssistantWire);
        Assert.False(openAssistant.IsFolded);
        Assert.False(olderUser.IsFolded);
        Assert.NotNull(addedEvent);
        Assert.Equal(CompressionStatus.Failed, addedEvent!.Status);
        Assert.Equal("empty_fold", addedEvent.ErrorMessage);
        _workingMemoryRepository.Verify(r => r.Add(It.IsAny<WorkingMemory>()), Times.Never);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task HandleAsync_InlineMode_MidChainPrefix_DuringCooldown_SkipsWrapUp()
    {
        _policy.MinTurnsBetweenGenerations = 2;
        _estimatedTokensToReturn = 150;

        var now = DateTimeOffset.UtcNow;
        var conversation = Conversation.Create("header:inline-mid-cooldown", now);
        conversation.CaptureSystemPromptIfAbsent("You are helpful.");
        conversation.SetSyncedMessageCount(2, now);
        var latestSuccess = CompressionEvent.Start(
            conversation.Id,
            CompressionMode.Inline,
            100,
            1,
            1,
            now.AddMinutes(-10));
        latestSuccess.Succeed(10, 2, now.AddMinutes(-5), 20, 5);
        const string priorAssistantWire =
            """{"role":"assistant","tool_calls":[{"id":"call_prior","type":"function","function":{"name":"lookup","arguments":"{}"}}]}""";
        const string priorToolWire =
            """{"role":"tool","tool_call_id":"call_prior","content":"prior result"}""";
        var stored = new List<ConversationMessage>
        {
            ConversationMessage.Create(
                conversation.Id, 0, MessageRole.Assistant, string.Empty, 8, now.AddMinutes(-1), priorAssistantWire),
            ConversationMessage.Create(
                conversation.Id, 1, MessageRole.Tool, "prior result", 4, now.AddSeconds(-30), priorToolWire)
        };
        var workingMemory = WorkingMemory.Create(
            conversation.Id,
            2,
            "# Working Memory\n## Current Goal\nExisting",
            8,
            now.AddMinutes(-5));

        _conversationRepository.Setup(r => r.FindByKeyAsync("header:inline-mid-cooldown", It.IsAny<CancellationToken>()))
            .ReturnsAsync(conversation);
        _messageRepository.Setup(r => r.GetByConversationIdAsync(conversation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);
        _workingMemoryRepository.Setup(r => r.GetLatestAsync(conversation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(workingMemory);
        _chatCompletionClient
            .Setup(c => c.CompleteAsync(It.IsAny<ProviderEndpoint>(), It.IsAny<UpstreamRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpstreamChatResult(
                Content: string.Empty,
                FinishReason: "tool_calls",
                PromptTokens: 10,
                CompletionTokens: 5,
                AssistantMessageJson:
                """{"role":"assistant","content":null,"tool_calls":[{"id":"call_new","type":"function","function":{"name":"lookup","arguments":"{}"}}]}"""));

        var service = CreateService();
        _compressionEventRepository
            .Setup(r => r.GetLatestSucceededAsync(conversation.Id, CompressionMode.Inline, It.IsAny<CancellationToken>()))
            .ReturnsAsync(latestSuccess);
        var result = await service.HandleAsync(
            BuildRequest(conversationHeader: "inline-mid-cooldown", userContent: "next hop"),
            CancellationToken.None);

        Assert.Equal("tool_calls", result.FinishReason);
        _chatCompletionClient.Verify(
            c => c.CompleteAsync(
                It.IsAny<ProviderEndpoint>(),
                It.Is<UpstreamRequest>(r => IsWrapUpRequest(r)),
                It.IsAny<CancellationToken>()),
            Times.Never);
        _compressionEventRepository.Verify(r => r.Add(It.IsAny<CompressionEvent>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_InlineMode_WhenWrapUpFailsSanity_PersistsAssistant_KeepsPriorWm()
    {
        _policy.CompressionRetainMessageCount = 1;
        _estimatedTokensToReturn = 150;

        var conversation = Conversation.Create("header:inline-sanity", DateTimeOffset.UtcNow);
        conversation.CaptureSystemPromptIfAbsent("You are helpful.");
        conversation.SetSyncedMessageCount(2, DateTimeOffset.UtcNow);
        var stored = new List<ConversationMessage>
        {
            ConversationMessage.Create(conversation.Id, 0, MessageRole.User, "older", 5, DateTimeOffset.UtcNow)
        };
        var workingMemory = WorkingMemory.Create(
            conversation.Id,
            1,
            "# Working Memory\n## Current Goal\nEarlier",
            8,
            DateTimeOffset.UtcNow);

        _conversationRepository.Setup(r => r.FindByKeyAsync("header:inline-sanity", It.IsAny<CancellationToken>()))
            .ReturnsAsync(conversation);
        _messageRepository.Setup(r => r.GetByConversationIdAsync(conversation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);
        _workingMemoryRepository.Setup(r => r.GetLatestAsync(conversation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(workingMemory);

        var addedMessages = new List<ConversationMessage>();
        CompressionEvent? addedEvent = null;
        _messageRepository.Setup(r => r.Add(It.IsAny<ConversationMessage>()))
            .Callback<ConversationMessage>(message => addedMessages.Add(message));
        _compressionEventRepository.Setup(r => r.Add(It.IsAny<CompressionEvent>()))
            .Callback<CompressionEvent>(evt => addedEvent = evt);
        _chatCompletionClient
            .Setup(c => c.CompleteAsync(It.IsAny<ProviderEndpoint>(), It.IsAny<UpstreamRequest>(), It.IsAny<CancellationToken>()))
            .Returns<ProviderEndpoint, UpstreamRequest, CancellationToken>((_, request, _) =>
            {
                if (IsWrapUpRequest(request))
                {
                    return Task.FromResult(new UpstreamChatResult("not working memory", "stop", 5, 2));
                }

                return Task.FromResult(new UpstreamChatResult("Visible answer", "stop", 40, 12));
            });

        var service = CreateService();
        var result = await service.HandleAsync(
            BuildRequest(conversationHeader: "inline-sanity", userContent: "new tip"),
            CancellationToken.None);

        Assert.Equal("Visible answer", result.AssistantContent);
        Assert.Equal("Visible answer", Assert.Single(addedMessages, m => m.Role == MessageRole.Assistant).Content);
        Assert.NotNull(addedEvent);
        Assert.Equal(CompressionStatus.Failed, addedEvent!.Status);
        Assert.StartsWith("sanity:", addedEvent.ErrorMessage);
        Assert.False(stored[0].IsFolded);
        _workingMemoryRepository.Verify(r => r.Add(It.IsAny<WorkingMemory>()), Times.Never);
        // prepare/persist + failed wrap-up persist (dual-id clear uses isolated map UoW)
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task HandleAsync_InlineMode_WhenWrapUpReturnsToolCalls_FailsAsToolCalls_KeepsPriorWm()
    {
        _policy.CompressionRetainMessageCount = 1;
        _estimatedTokensToReturn = 150;

        var conversation = Conversation.Create("header:inline-wrap-tools", DateTimeOffset.UtcNow);
        conversation.CaptureSystemPromptIfAbsent("You are helpful.");
        conversation.SetSyncedMessageCount(2, DateTimeOffset.UtcNow);
        var stored = new List<ConversationMessage>
        {
            ConversationMessage.Create(conversation.Id, 0, MessageRole.User, "older", 5, DateTimeOffset.UtcNow)
        };
        var workingMemory = WorkingMemory.Create(
            conversation.Id,
            1,
            "# Working Memory\n## Current Goal\nEarlier",
            8,
            DateTimeOffset.UtcNow);

        _conversationRepository.Setup(r => r.FindByKeyAsync("header:inline-wrap-tools", It.IsAny<CancellationToken>()))
            .ReturnsAsync(conversation);
        _messageRepository.Setup(r => r.GetByConversationIdAsync(conversation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);
        _workingMemoryRepository.Setup(r => r.GetLatestAsync(conversation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(workingMemory);

        CompressionEvent? addedEvent = null;
        _compressionEventRepository.Setup(r => r.Add(It.IsAny<CompressionEvent>()))
            .Callback<CompressionEvent>(evt => addedEvent = evt);
        _chatCompletionClient
            .Setup(c => c.CompleteAsync(It.IsAny<ProviderEndpoint>(), It.IsAny<UpstreamRequest>(), It.IsAny<CancellationToken>()))
            .Returns<ProviderEndpoint, UpstreamRequest, CancellationToken>((_, request, _) =>
            {
                if (IsWrapUpRequest(request))
                {
                    // Model ignored the protocol and kept driving the agent loop.
                    return Task.FromResult(new UpstreamChatResult(
                        "Now let me check the test files.",
                        "tool_calls",
                        5,
                        2,
                        AssistantMessageJson: """
                        {"role":"assistant","content":"Now let me check the test files.","tool_calls":[{"id":"call_1","type":"function","function":{"name":"Read","arguments":"{}"}}]}
                        """));
                }

                return Task.FromResult(new UpstreamChatResult("Visible answer", "stop", 40, 12));
            });

        var service = CreateService();
        var result = await service.HandleAsync(
            BuildRequest(conversationHeader: "inline-wrap-tools", userContent: "new tip"),
            CancellationToken.None);

        Assert.Equal("Visible answer", result.AssistantContent);
        Assert.NotNull(addedEvent);
        Assert.Equal(CompressionStatus.Failed, addedEvent!.Status);
        Assert.Equal("wrapup_tool_calls", addedEvent.ErrorMessage);
        Assert.False(stored[0].IsFolded);
        _workingMemoryRepository.Verify(r => r.Add(It.IsAny<WorkingMemory>()), Times.Never);
    }


    [Fact]
    public async Task HandleStreamingAsync_InlineMode_HoldsDoneUntilWrapUpCompletes()
    {
        _estimatedTokensToReturn = 150;

        var conversation = Conversation.Create("header:inline-stream", DateTimeOffset.UtcNow);
        conversation.CaptureSystemPromptIfAbsent("You are helpful.");
        conversation.SetSyncedMessageCount(1, DateTimeOffset.UtcNow);
        var existingWorkingMemory = WorkingMemory.Create(
            conversation.Id,
            1,
            "# Working Memory\n## Current Goal\nExisting",
            8,
            DateTimeOffset.UtcNow);
        var stored = new List<ConversationMessage>
        {
            ConversationMessage.Create(conversation.Id, 0, MessageRole.User, "older", 5, DateTimeOffset.UtcNow)
        };

        _conversationRepository.Setup(r => r.FindByKeyAsync("header:inline-stream", It.IsAny<CancellationToken>()))
            .ReturnsAsync(conversation);
        _messageRepository.Setup(r => r.GetByConversationIdAsync(conversation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);
        _workingMemoryRepository.Setup(r => r.GetLatestAsync(conversation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingWorkingMemory);

        var wrapUpStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowWrapUp = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

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
                    await onRawSseData("""{"choices":[{"delta":{"content":"Visible answer"}}]}""", token);
                    await onRawSseData("""{"choices":[{"delta":{},"finish_reason":"stop"}]}""", token);
                    await onRawSseData("[DONE]", token);
                    return new UpstreamChatResult(
                        "Visible answer",
                        "stop",
                        40,
                        12,
                        AssistantMessageJson: """{"role":"assistant","content":"Visible answer"}""");
                });

        _chatCompletionClient
            .Setup(c => c.CompleteAsync(It.IsAny<ProviderEndpoint>(), It.IsAny<UpstreamRequest>(), It.IsAny<CancellationToken>()))
            .Returns(async (ProviderEndpoint _, UpstreamRequest request, CancellationToken _) =>
            {
                Assert.True(IsWrapUpRequest(request));
                wrapUpStarted.TrySetResult();
                await allowWrapUp.Task;
                return new UpstreamChatResult(ValidWorkingMemory, "stop", 30, 8);
            });
        var chunks = new List<string>();
        var service = CreateService();
        var handleTask = service.HandleStreamingAsync(
            BuildRequest(conversationHeader: "inline-stream", userContent: "next tip", stream: true),
            _ => { },
            (chunk, _) =>
            {
                chunks.Add(chunk);
                return Task.CompletedTask;
            },
            CancellationToken.None);

        await wrapUpStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.DoesNotContain("[DONE]", chunks);
        Assert.Contains(chunks, c => c.Contains("Visible answer", StringComparison.Ordinal));
        Assert.DoesNotContain(chunks, c => c.Contains("Inline summary", StringComparison.Ordinal));

        allowWrapUp.TrySetResult();
        var result = await handleTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("Visible answer", result.AssistantContent);
        Assert.Equal("[DONE]", chunks[^1]);
    }

    [Fact]
    public async Task HandleStreamingAsync_InlineMode_MidChainPrefix_HoldsDoneUntilWrapUpCompletes()
    {
        _policy.CompressionRetainMessageCount = 1;
        _estimatedTokensToReturn = 150;

        var now = DateTimeOffset.UtcNow;
        var conversation = Conversation.Create("header:inline-stream-mid", now);
        conversation.CaptureSystemPromptIfAbsent("You are helpful.");
        conversation.SetSyncedMessageCount(1, now);
        const string priorAssistantWire =
            """{"role":"assistant","tool_calls":[{"id":"call_prior","type":"function","function":{"name":"lookup","arguments":"{}"}}]}""";
        const string priorToolWire =
            """{"role":"tool","tool_call_id":"call_prior","content":"prior result"}""";
        var olderUser = ConversationMessage.Create(conversation.Id, 0, MessageRole.User, "older", 5, now);
        var priorAssistant = ConversationMessage.Create(
            conversation.Id, 1, MessageRole.Assistant, string.Empty, 8, now, priorAssistantWire);
        var priorTool = ConversationMessage.Create(
            conversation.Id, 2, MessageRole.Tool, "prior result", 4, now, priorToolWire);
        var stored = new List<ConversationMessage> { olderUser, priorAssistant, priorTool };
        var existingWorkingMemory = WorkingMemory.Create(
            conversation.Id,
            1,
            "# Working Memory\n## Current Goal\nExisting",
            8,
            now);

        _conversationRepository.Setup(r => r.FindByKeyAsync("header:inline-stream-mid", It.IsAny<CancellationToken>()))
            .ReturnsAsync(conversation);
        _messageRepository.Setup(r => r.GetByConversationIdAsync(conversation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);
        _workingMemoryRepository.Setup(r => r.GetLatestAsync(conversation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingWorkingMemory);

        var wrapUpStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowWrapUp = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        const string openAssistantWire =
            """{"role":"assistant","content":null,"tool_calls":[{"id":"call_new","type":"function","function":{"name":"lookup","arguments":"{}"}}]}""";

        UpstreamRequest? mainRequest = null;
        _chatCompletionClient
            .Setup(c => c.StreamAsync(
                It.IsAny<ProviderEndpoint>(),
                It.IsAny<UpstreamRequest>(),
                It.IsAny<Func<string, CancellationToken, Task>>(),
                It.IsAny<CancellationToken>()))
            .Returns(
                async (
                    ProviderEndpoint _,
                    UpstreamRequest request,
                    Func<string, CancellationToken, Task> onRawSseData,
                    CancellationToken token) =>
                {
                    mainRequest = request;
                    await onRawSseData(
                        """{"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call_new","type":"function","function":{"name":"lookup","arguments":"{}"}}]},"finish_reason":"tool_calls"}]}""",
                        token);
                    await onRawSseData("[DONE]", token);
                    return new UpstreamChatResult(
                        Content: string.Empty,
                        FinishReason: "tool_calls",
                        PromptTokens: 40,
                        CompletionTokens: 12,
                        AssistantMessageJson: openAssistantWire);
                });

        UpstreamRequest? wrapRequest = null;
        _chatCompletionClient
            .Setup(c => c.CompleteAsync(It.IsAny<ProviderEndpoint>(), It.IsAny<UpstreamRequest>(), It.IsAny<CancellationToken>()))
            .Returns(async (ProviderEndpoint _, UpstreamRequest request, CancellationToken _) =>
            {
                Assert.True(IsWrapUpRequest(request));
                wrapRequest = request;
                wrapUpStarted.TrySetResult();
                await allowWrapUp.Task;
                return new UpstreamChatResult(ValidWorkingMemory, "stop", 30, 8);
            });
        var chunks = new List<string>();
        var service = CreateService();
        var handleTask = service.HandleStreamingAsync(
            BuildRequest(conversationHeader: "inline-stream-mid", userContent: "next hop", stream: true),
            _ => { },
            (chunk, _) =>
            {
                chunks.Add(chunk);
                return Task.CompletedTask;
            },
            CancellationToken.None);

        await wrapUpStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Empty(chunks);
        Assert.NotNull(mainRequest);
        Assert.NotNull(wrapRequest);
        Assert.Equal(mainRequest!.Messages.Count + 1, wrapRequest!.Messages.Count);
        Assert.Equal(PromptFactory.BuildInlineWrapUpUserMessage().Content, wrapRequest.Messages[^1].Content);
        var penultimate = wrapRequest.Messages[^2];
        Assert.False(
            penultimate.Role == MessageRole.Assistant
            && penultimate.RawWireMessage is { } wire
            && wire.TryGetProperty("tool_calls", out var toolCalls)
            && toolCalls.ValueKind == JsonValueKind.Array
            && toolCalls.GetArrayLength() > 0);

        allowWrapUp.TrySetResult();
        var result = await handleTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("tool_calls", result.FinishReason);
        Assert.Equal(2, chunks.Count);
        Assert.Contains("tool_calls", chunks[0], StringComparison.Ordinal);
        Assert.Equal("[DONE]", chunks[^1]);
        Assert.True(olderUser.IsFolded);
        Assert.False(priorAssistant.IsFolded);
    }

    [Fact]
    public async Task HandleAsync_InlineMode_GateHeldThroughWrapUp()
    {
        _estimatedTokensToReturn = 150;

        var conversation = Conversation.Create("header:inline-gate", DateTimeOffset.UtcNow);
        conversation.CaptureSystemPromptIfAbsent("You are helpful.");
        conversation.SetSyncedMessageCount(1, DateTimeOffset.UtcNow);
        var stored = new List<ConversationMessage>
        {
            ConversationMessage.Create(conversation.Id, 0, MessageRole.User, "older", 5, DateTimeOffset.UtcNow)
        };
        var workingMemory = WorkingMemory.Create(
            conversation.Id,
            1,
            "# Working Memory\n## Current Goal\nExisting",
            8,
            DateTimeOffset.UtcNow);

        var prepareCount = 0;
        var secondPrepareStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _conversationRepository.Setup(r => r.FindByKeyAsync("header:inline-gate", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var n = Interlocked.Increment(ref prepareCount);
                if (n >= 2)
                {
                    secondPrepareStarted.TrySetResult();
                }

                return conversation;
            });
        _messageRepository.Setup(r => r.GetByConversationIdAsync(conversation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);
        _workingMemoryRepository.Setup(r => r.GetLatestAsync(conversation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(workingMemory);

        var wrapUpStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowWrapUp = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        _chatCompletionClient
            .Setup(c => c.CompleteAsync(It.IsAny<ProviderEndpoint>(), It.IsAny<UpstreamRequest>(), It.IsAny<CancellationToken>()))
            .Returns(async (ProviderEndpoint _, UpstreamRequest request, CancellationToken _) =>
            {
                if (IsWrapUpRequest(request))
                {
                    wrapUpStarted.TrySetResult();
                    await allowWrapUp.Task;
                    return new UpstreamChatResult(ValidWorkingMemory, "stop", 30, 8);
                }

                return new UpstreamChatResult("Visible answer", "stop", 40, 12);
            });
        var gate = new ConversationRequestGate();
        var service = CreateService(requestGate: gate);

        var first = service.HandleAsync(
            BuildRequest(conversationHeader: "inline-gate", userContent: "first"),
            CancellationToken.None);

        await wrapUpStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var second = service.HandleAsync(
            BuildRequest(conversationHeader: "inline-gate", userContent: "second"),
            CancellationToken.None);

        await Task.Delay(100);
        Assert.False(secondPrepareStarted.Task.IsCompleted);

        allowWrapUp.TrySetResult();
        await first.WaitAsync(TimeSpan.FromSeconds(5));
        await secondPrepareStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await second.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(prepareCount >= 2);
    }

    [Fact]
    public async Task HandleStreamingAsync_InlineMode_ClientAbortAfterMain_StillRunsWrapUp()
    {
        _estimatedTokensToReturn = 150;

        var conversation = Conversation.Create("header:inline-abort", DateTimeOffset.UtcNow);
        conversation.CaptureSystemPromptIfAbsent("You are helpful.");
        conversation.SetSyncedMessageCount(1, DateTimeOffset.UtcNow);
        var stored = new List<ConversationMessage>
        {
            ConversationMessage.Create(conversation.Id, 0, MessageRole.User, "older", 5, DateTimeOffset.UtcNow)
        };
        var workingMemory = WorkingMemory.Create(
            conversation.Id,
            1,
            "# Working Memory\n## Current Goal\nExisting",
            8,
            DateTimeOffset.UtcNow);

        _conversationRepository.Setup(r => r.FindByKeyAsync("header:inline-abort", It.IsAny<CancellationToken>()))
            .ReturnsAsync(conversation);
        _messageRepository.Setup(r => r.GetByConversationIdAsync(conversation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);
        _workingMemoryRepository.Setup(r => r.GetLatestAsync(conversation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(workingMemory);

        WorkingMemory? addedWorkingMemory = null;
        _workingMemoryRepository.Setup(r => r.Add(It.IsAny<WorkingMemory>()))
            .Callback<WorkingMemory>(wm => addedWorkingMemory = wm);

        using var requestCts = new CancellationTokenSource();
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
                    await onRawSseData("""{"choices":[{"delta":{"content":"Visible answer"}}]}""", token);
                    await onRawSseData("[DONE]", token);
                    requestCts.Cancel();
                    return new UpstreamChatResult("Visible answer", "stop", 40, 12);
                });

        _chatCompletionClient
            .Setup(c => c.CompleteAsync(It.IsAny<ProviderEndpoint>(), It.IsAny<UpstreamRequest>(), It.IsAny<CancellationToken>()))
            .Returns<ProviderEndpoint, UpstreamRequest, CancellationToken>((_, request, ct) =>
            {
                Assert.True(IsWrapUpRequest(request));
                Assert.False(ct.IsCancellationRequested);
                return Task.FromResult(new UpstreamChatResult(ValidWorkingMemory, "stop", 30, 8));
            });
        var service = CreateService();
        var result = await service.HandleStreamingAsync(
            BuildRequest(conversationHeader: "inline-abort", userContent: "next tip", stream: true),
            _ => { },
            (_, _) => Task.CompletedTask,
            requestCts.Token);

        Assert.Equal("Visible answer", result.AssistantContent);
        Assert.NotNull(addedWorkingMemory);
    }

    [Fact]
    public async Task HandleStreamingAsync_InlineMode_MidChainPrefix_HoldsToolCallsUntilWrapUpThenFlushesInOrder()
    {
        var fixture = SetupMidChainInlineConversation("inline-v3-hold");
        var ledger = new List<string>();
        WorkingMemory? addedWorkingMemory = null;
        var addedMessages = new List<ConversationMessage>();
        _workingMemoryRepository.Setup(r => r.Add(It.IsAny<WorkingMemory>()))
            .Callback<WorkingMemory>(wm => addedWorkingMemory = wm);
        _messageRepository.Setup(r => r.Add(It.IsAny<ConversationMessage>()))
            .Callback<ConversationMessage>(message => addedMessages.Add(message));

        SetupMidChainStream();
        _chatCompletionClient
            .Setup(c => c.CompleteAsync(It.IsAny<ProviderEndpoint>(), It.IsAny<UpstreamRequest>(), It.IsAny<CancellationToken>()))
            .Returns<ProviderEndpoint, UpstreamRequest, CancellationToken>((_, request, _) =>
            {
                Assert.True(IsWrapUpRequest(request));
                Assert.DoesNotContain(WrittenSse(ledger), ToolCallWireHelper.StreamChunkHasToolCalls);
                Assert.DoesNotContain("[DONE]", WrittenSse(ledger));
                ledger.Add("wrapup:returned");
                return Task.FromResult(new UpstreamChatResult(ValidWorkingMemory, "stop", 30, 8));
            });

        var service = CreateService();
        var result = await service.HandleStreamingAsync(
            BuildRequest(conversationHeader: "inline-v3-hold", userContent: "next hop", stream: true),
            _ => { },
            (chunk, _) =>
            {
                ledger.Add("sse:" + chunk);
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.Equal("tool_calls", result.FinishReason);
        var wrapUpIndex = ledger.IndexOf("wrapup:returned");
        Assert.True(wrapUpIndex >= 0);
        Assert.DoesNotContain(
            ledger.Take(wrapUpIndex),
            entry => entry.StartsWith("sse:", StringComparison.Ordinal)
                && (entry.Contains("tool_calls", StringComparison.Ordinal) || entry == "sse:[DONE]"));

        var written = WrittenSse(ledger);
        Assert.Equal(MidChainContentFrame, written[0]);
        Assert.Equal(
            new[]
            {
                MidChainContentFrame,
                MidChainToolCallFrame,
                MidChainToolArgumentsFrame,
                MidChainFinishFrame,
                "[DONE]"
            },
            written);

        Assert.True(wrapUpIndex < ledger.IndexOf("sse:" + MidChainToolCallFrame));
        Assert.Equal("[DONE]", written[^1]);

        Assert.NotNull(addedWorkingMemory);
        Assert.Equal(2, addedWorkingMemory!.Version);
        var openAssistant = Assert.Single(
            addedMessages,
            m => m.Role == MessageRole.Assistant && m.RawWireJson == MidChainOpenAssistantWire);
        Assert.False(openAssistant.IsFolded);
        Assert.True(fixture.OlderUser.IsFolded);
        Assert.False(fixture.PriorAssistant.IsFolded);
        Assert.False(fixture.PriorTool.IsFolded);
    }

    [Fact]
    public async Task HandleStreamingAsync_InlineMode_OpenStoreEmergency_HoldsToolCallsUntilWrapUp()
    {
        _policy.CompressionRetainMessageCount = 1;
        _estimatedTokensToReturn = 150;
        _cacheAlignmentOptions = new CacheAlignmentOptions { Enabled = false, MaxConversations = 1024 };

        var now = DateTimeOffset.UtcNow;
        var conversation = Conversation.Create("header:inline-open-stream", now);
        conversation.CaptureSystemPromptIfAbsent("You are helpful.");
        conversation.SetSyncedMessageCount(2, now);
        const string priorAssistantWire =
            """{"role":"assistant","tool_calls":[{"id":"call_prior","type":"function","function":{"name":"lookup","arguments":"{}"}}]}""";
        const string priorToolWire =
            """{"role":"tool","tool_call_id":"call_prior","content":"prior result"}""";
        const string openTipWire =
            """{"role":"assistant","tool_calls":[{"id":"call_open","type":"function","function":{"name":"lookup","arguments":"{}"}}]}""";
        var olderUser = ConversationMessage.Create(conversation.Id, 0, MessageRole.User, "older", 5, now);
        var priorAssistant = ConversationMessage.Create(
            conversation.Id, 1, MessageRole.Assistant, string.Empty, 8, now, priorAssistantWire);
        var priorTool = ConversationMessage.Create(
            conversation.Id, 2, MessageRole.Tool, "prior result", 4, now, priorToolWire);
        var openTip = ConversationMessage.Create(
            conversation.Id, 3, MessageRole.Assistant, string.Empty, 8, now, openTipWire);
        var stored = new List<ConversationMessage> { olderUser, priorAssistant, priorTool, openTip };
        var existingWorkingMemory = WorkingMemory.Create(
            conversation.Id,
            1,
            "# Working Memory\n## Current Goal\nExisting",
            8,
            now);

        _conversationRepository.Setup(r => r.FindByKeyAsync("header:inline-open-stream", It.IsAny<CancellationToken>()))
            .ReturnsAsync(conversation);
        _messageRepository.Setup(r => r.GetByConversationIdAsync(conversation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);
        _workingMemoryRepository.Setup(r => r.GetLatestAsync(conversation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingWorkingMemory);

        var ledger = new List<string>();
        WorkingMemory? addedWorkingMemory = null;
        _workingMemoryRepository.Setup(r => r.Add(It.IsAny<WorkingMemory>()))
            .Callback<WorkingMemory>(wm => addedWorkingMemory = wm);

        SetupMidChainStream();
        _chatCompletionClient
            .Setup(c => c.CompleteAsync(It.IsAny<ProviderEndpoint>(), It.IsAny<UpstreamRequest>(), It.IsAny<CancellationToken>()))
            .Returns<ProviderEndpoint, UpstreamRequest, CancellationToken>((_, request, _) =>
            {
                Assert.True(IsWrapUpRequest(request));
                AssertWrapUpMessagesHaveNoOpenToolCalls(request.Messages);
                Assert.DoesNotContain(WrittenSse(ledger), ToolCallWireHelper.StreamChunkHasToolCalls);
                ledger.Add("wrapup:returned");
                return Task.FromResult(new UpstreamChatResult(ValidWorkingMemory, "stop", 30, 8));
            });

        var service = CreateService();
        var result = await service.HandleStreamingAsync(
            BuildRequest(conversationHeader: "inline-open-stream", userContent: "next hop", stream: true),
            _ => { },
            (chunk, _) =>
            {
                ledger.Add("sse:" + chunk);
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.Equal("tool_calls", result.FinishReason);
        var wrapUpIndex = ledger.IndexOf("wrapup:returned");
        Assert.True(wrapUpIndex >= 0);
        Assert.True(wrapUpIndex < ledger.IndexOf("sse:" + MidChainToolCallFrame));
        Assert.NotNull(addedWorkingMemory);
        Assert.False(openTip.IsFolded);
        Assert.True(olderUser.IsFolded);
    }

    [Fact]
    public async Task HandleStreamingAsync_InlineMode_MidChainPrefix_SoftFail_StillFlushesHeldToolCalls()
    {
        var fixture = SetupMidChainInlineConversation("inline-v3-softfail");
        var ledger = new List<string>();
        CompressionEvent? addedEvent = null;
        _compressionEventRepository.Setup(r => r.Add(It.IsAny<CompressionEvent>()))
            .Callback<CompressionEvent>(evt => addedEvent = evt);

        SetupMidChainStream();
        _chatCompletionClient
            .Setup(c => c.CompleteAsync(It.IsAny<ProviderEndpoint>(), It.IsAny<UpstreamRequest>(), It.IsAny<CancellationToken>()))
            .Returns<ProviderEndpoint, UpstreamRequest, CancellationToken>((_, request, _) =>
            {
                Assert.True(IsWrapUpRequest(request));
                Assert.DoesNotContain(WrittenSse(ledger), ToolCallWireHelper.StreamChunkHasToolCalls);
                ledger.Add("wrapup:returned");
                return Task.FromResult(new UpstreamChatResult(
                    Content: string.Empty,
                    FinishReason: "tool_calls",
                    PromptTokens: 30,
                    CompletionTokens: 8,
                    AssistantMessageJson: MidChainOpenAssistantWire));
            });

        var service = CreateService();
        var result = await service.HandleStreamingAsync(
            BuildRequest(conversationHeader: "inline-v3-softfail", userContent: "next hop", stream: true),
            _ => { },
            (chunk, _) =>
            {
                ledger.Add("sse:" + chunk);
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.Equal("tool_calls", result.FinishReason);
        var wrapUpIndex = ledger.IndexOf("wrapup:returned");
        Assert.True(wrapUpIndex >= 0);
        Assert.True(wrapUpIndex < ledger.IndexOf("sse:" + MidChainToolCallFrame));
        Assert.Equal(
            new[]
            {
                MidChainContentFrame,
                MidChainToolCallFrame,
                MidChainToolArgumentsFrame,
                MidChainFinishFrame,
                "[DONE]"
            },
            WrittenSse(ledger));

        Assert.NotNull(addedEvent);
        Assert.Equal(CompressionStatus.Failed, addedEvent!.Status);
        Assert.Equal("wrapup_tool_calls", addedEvent.ErrorMessage);
        _workingMemoryRepository.Verify(r => r.Add(It.IsAny<WorkingMemory>()), Times.Never);
        Assert.False(fixture.OlderUser.IsFolded);
    }

    [Fact]
    public async Task HandleStreamingAsync_InlineMode_StopTurn_StreamsContentLive_HoldsOnlyDone()
    {
        _estimatedTokensToReturn = 150;

        var conversation = Conversation.Create("header:inline-v3-stop", DateTimeOffset.UtcNow);
        conversation.CaptureSystemPromptIfAbsent("You are helpful.");
        conversation.SetSyncedMessageCount(1, DateTimeOffset.UtcNow);
        var existingWorkingMemory = WorkingMemory.Create(
            conversation.Id,
            1,
            "# Working Memory\n## Current Goal\nExisting",
            8,
            DateTimeOffset.UtcNow);
        var stored = new List<ConversationMessage>
        {
            ConversationMessage.Create(conversation.Id, 0, MessageRole.User, "older", 5, DateTimeOffset.UtcNow)
        };

        _conversationRepository.Setup(r => r.FindByKeyAsync("header:inline-v3-stop", It.IsAny<CancellationToken>()))
            .ReturnsAsync(conversation);
        _messageRepository.Setup(r => r.GetByConversationIdAsync(conversation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);
        _workingMemoryRepository.Setup(r => r.GetLatestAsync(conversation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingWorkingMemory);

        const string contentFrame = """{"choices":[{"delta":{"content":"Visible answer"}}]}""";
        const string finishFrame = """{"choices":[{"delta":{},"finish_reason":"stop"}]}""";
        var ledger = new List<string>();

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
                    await onRawSseData(contentFrame, token);
                    await onRawSseData(finishFrame, token);
                    await onRawSseData("[DONE]", token);
                    return new UpstreamChatResult(
                        "Visible answer",
                        "stop",
                        40,
                        12,
                        AssistantMessageJson: """{"role":"assistant","content":"Visible answer"}""");
                });

        _chatCompletionClient
            .Setup(c => c.CompleteAsync(It.IsAny<ProviderEndpoint>(), It.IsAny<UpstreamRequest>(), It.IsAny<CancellationToken>()))
            .Returns<ProviderEndpoint, UpstreamRequest, CancellationToken>((_, request, _) =>
            {
                Assert.True(IsWrapUpRequest(request));
                Assert.Contains("sse:" + contentFrame, ledger);
                Assert.DoesNotContain("sse:[DONE]", ledger);
                ledger.Add("wrapup:returned");
                return Task.FromResult(new UpstreamChatResult(ValidWorkingMemory, "stop", 30, 8));
            });

        var service = CreateService();
        var result = await service.HandleStreamingAsync(
            BuildRequest(conversationHeader: "inline-v3-stop", userContent: "next tip", stream: true),
            _ => { },
            (chunk, _) =>
            {
                ledger.Add("sse:" + chunk);
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.Equal("Visible answer", result.AssistantContent);
        var wrapUpIndex = ledger.IndexOf("wrapup:returned");
        Assert.True(wrapUpIndex > ledger.IndexOf("sse:" + contentFrame));
        Assert.True(wrapUpIndex < ledger.IndexOf("sse:[DONE]"));
        Assert.Equal(new[] { contentFrame, finishFrame, "[DONE]" }, WrittenSse(ledger));
    }

    [Fact]
    public async Task HandleAsync_InlineMode_MidChainPrefix_WaitsForWrapUp_PreservesOriginalToolCallsBody()
    {
        var fixture = SetupMidChainInlineConversation("inline-v3-nonstream");
        const string rawResponse =
            """{"id":"chatcmpl-mid","choices":[{"message":{"role":"assistant","content":null,"tool_calls":[{"id":"call_new","type":"function","function":{"name":"lookup","arguments":"{\"q\":\"x\"}"}}]},"finish_reason":"tool_calls"}]}""";
        var ledger = new List<string>();
        WorkingMemory? addedWorkingMemory = null;
        _workingMemoryRepository.Setup(r => r.Add(It.IsAny<WorkingMemory>()))
            .Callback<WorkingMemory>(wm => addedWorkingMemory = wm);

        _chatCompletionClient
            .Setup(c => c.CompleteAsync(It.IsAny<ProviderEndpoint>(), It.IsAny<UpstreamRequest>(), It.IsAny<CancellationToken>()))
            .Returns<ProviderEndpoint, UpstreamRequest, CancellationToken>((_, request, _) =>
            {
                if (IsWrapUpRequest(request))
                {
                    ledger.Add("wrapup:returned");
                    return Task.FromResult(new UpstreamChatResult(ValidWorkingMemory, "stop", 30, 8));
                }

                ledger.Add("main:returned");
                return Task.FromResult(new UpstreamChatResult(
                    Content: string.Empty,
                    FinishReason: "tool_calls",
                    PromptTokens: 40,
                    CompletionTokens: 12,
                    RawResponseJson: rawResponse,
                    AssistantMessageJson: MidChainOpenAssistantWire));
            });

        var service = CreateService();
        var result = await service.HandleAsync(
            BuildRequest(conversationHeader: "inline-v3-nonstream", userContent: "next hop"),
            CancellationToken.None);
        ledger.Add("handle:returned");

        Assert.Equal(new[] { "main:returned", "wrapup:returned", "handle:returned" }, ledger);
        Assert.Equal("tool_calls", result.FinishReason);
        Assert.Equal(rawResponse, result.RawResponseJson);
        Assert.Contains("call_new", result.RawResponseJson, StringComparison.Ordinal);
        Assert.NotNull(addedWorkingMemory);
        Assert.Equal(2, addedWorkingMemory!.Version);
        Assert.True(fixture.OlderUser.IsFolded);
        Assert.False(fixture.PriorAssistant.IsFolded);
    }

    [Fact]
    public async Task HandleStreamingAsync_InlineMode_MidChainPrefix_ClientGoneDuringFlush_StillReturns()
    {
        SetupMidChainInlineConversation("inline-v3-client-gone");
        WorkingMemory? addedWorkingMemory = null;
        _workingMemoryRepository.Setup(r => r.Add(It.IsAny<WorkingMemory>()))
            .Callback<WorkingMemory>(wm => addedWorkingMemory = wm);

        SetupMidChainStream();
        _chatCompletionClient
            .Setup(c => c.CompleteAsync(It.IsAny<ProviderEndpoint>(), It.IsAny<UpstreamRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpstreamChatResult(ValidWorkingMemory, "stop", 30, 8));

        var service = CreateService();
        var result = await service.HandleStreamingAsync(
            BuildRequest(conversationHeader: "inline-v3-client-gone", userContent: "next hop", stream: true),
            _ => { },
            (chunk, _) =>
            {
                if (ToolCallWireHelper.StreamChunkHasToolCalls(chunk) || chunk == MidChainFinishFrame || chunk == "[DONE]")
                {
                    throw new IOException("client gone");
                }

                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.Equal("tool_calls", result.FinishReason);
        Assert.NotNull(addedWorkingMemory);
        Assert.Equal(2, addedWorkingMemory!.Version);
    }

    [Fact]
    public async Task HandleAsync_AfterWorkingMemoryExists_ForwardsWorkingMemoryAndTip()
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
        var stored = new List<ConversationMessage>
        {
            ConversationMessage.Create(conversation.Id, 0, MessageRole.User, "msg-0", 5, now),
            ConversationMessage.Create(conversation.Id, 1, MessageRole.Assistant, "msg-1", 5, now)
        };

        _conversationRepository.Setup(r => r.FindByKeyAsync("header:conv-wm", It.IsAny<CancellationToken>()))
            .ReturnsAsync(conversation);
        _messageRepository.Setup(r => r.GetByConversationIdAsync(conversation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);
        _workingMemoryRepository.Setup(r => r.GetLatestAsync(conversation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(workingMemory);
        conversation.SetSyncedMessageCount(2, now);
        _chatCompletionClient
            .Setup(c => c.CompleteAsync(It.IsAny<ProviderEndpoint>(), It.IsAny<UpstreamRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ProviderEndpoint, UpstreamRequest, CancellationToken>((_, request, _) => forwarded = request)
            .ReturnsAsync(new UpstreamChatResult("ack", "stop", 10, 2));

        var service = CreateService();
        await service.HandleAsync(BuildRequest(conversationHeader: "conv-wm", userContent: "next tip"), CancellationToken.None);

        Assert.NotNull(forwarded);
        Assert.Contains(forwarded!.Messages, m => m.Role == MessageRole.System && m.Content.Contains("Working Memory"));
        Assert.Equal("next tip", forwarded.Messages[^1].Content);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_VirtualInboundWithPendingDualId_ChatFlushThenIsolatedMapComplete()
    {
        _toolSchemaOptions = new ToolSchemaOptions { Mode = ToolSchemaMode.Virtual };
        _estimatedTokensToReturn = 10;
        const string clientCallId = "cur_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        const string irCallId = "ir_uow_1";
        var now = DateTimeOffset.UtcNow;

        var conversation = Conversation.Create("header:uow-inbound", now);
        conversation.CaptureSystemPromptIfAbsent("You are helpful.");
        conversation.SetSyncedMessageCount(3, now);
        var assistantWire =
            "{\"role\":\"assistant\",\"content\":null,\"tool_calls\":[{\"id\":\"" + clientCallId +
            "\",\"type\":\"function\",\"function\":{\"name\":\"lookup\",\"arguments\":\"{}\"}}]}";
        var stored = new List<ConversationMessage>
        {
            ConversationMessage.Create(conversation.Id, 0, MessageRole.User, "ask", 5, now),
            ConversationMessage.Create(conversation.Id, 1, MessageRole.Assistant, string.Empty, 5, now, assistantWire)
        };

        _conversationRepository.Setup(r => r.FindByKeyAsync("header:uow-inbound", It.IsAny<CancellationToken>()))
            .ReturnsAsync(conversation);
        _messageRepository.Setup(r => r.GetByConversationIdAsync(conversation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);
        _workingMemoryRepository.Setup(r => r.GetLatestAsync(conversation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkingMemory?)null);

        var addedMessages = new List<ConversationMessage>();
        _messageRepository.Setup(r => r.Add(It.IsAny<ConversationMessage>()))
            .Callback<ConversationMessage>(m => addedMessages.Add(m));

        var inboundDistillFlushSeen = false;
        _unitOfWork
            .Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                if (addedMessages.Any(m => m.Role == MessageRole.Tool) &&
                    _callIdMapRepo.Rows.Any(r => r.ClientCallId == clientCallId))
                {
                    inboundDistillFlushSeen = true;
                }
            })
            .Returns(Task.CompletedTask);

        SetupCompressionMapperForNonFileTools("lookup");
        _chatCompletionClient
            .Setup(c => c.CompleteAsync(
                It.IsAny<ProviderEndpoint>(),
                It.Is<UpstreamRequest>(r => r.Purpose == UpstreamRequestPurpose.Chat),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpstreamChatResult("ack", "stop", 10, 2));

        var service = CreateService();
        _callIdMapRepo.Add(ConversationToolCallMap.CreatePending(
            conversation.Id,
            irCallId,
            clientCallId,
            "passthrough_lookup",
            "lookup",
            "{}",
            "{}",
            "passthrough",
            null,
            null,
            null,
            now));

        var payload = new
        {
            model = "client-model",
            tools = new object[]
            {
                new { type = "function", function = new { name = "lookup" } }
            },
            messages = new object[]
            {
                new { role = "system", content = "You are helpful." },
                new { role = "user", content = "ask" },
                new
                {
                    role = "assistant",
                    content = (string?)null,
                    tool_calls = new object[]
                    {
                        new
                        {
                            id = clientCallId,
                            type = "function",
                            function = new { name = "lookup", arguments = "{}" }
                        }
                    }
                },
                new { role = "tool", tool_call_id = clientCallId, content = "native tool body" }
            }
        };
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        var request = Comprexy.Api.Mapping.ChatCompletionRequestParser.Parse(
            document.RootElement.Clone(),
            "uow-inbound");

        await service.HandleAsync(request, CancellationToken.None);

        Assert.True(inboundDistillFlushSeen);
        Assert.DoesNotContain(_callIdMapRepo.Rows, r => r.ClientCallId == clientCallId);
        Assert.True(_mapSaveChangesCount >= 1);
        Assert.Contains(addedMessages, m => m.Role == MessageRole.Tool && m.Content.Contains("passthrough", StringComparison.Ordinal));
    }

    [Fact]
    public async Task HandleAsync_VirtualInboundParallelReads_DoesNotTipRepairNativeClientWire()
    {
        // Regression: after distill, IsSameTip fails (IR call_* vs client cur_*). Tip sync must not
        // re-persist the last native tool result into the IR transcript.
        _toolSchemaOptions = new ToolSchemaOptions { Mode = ToolSchemaMode.Virtual };
        _estimatedTokensToReturn = 10;
        const string irA = "call_ir_a";
        const string irB = "call_ir_b";
        const string irC = "call_ir_c";
        const string curA = "cur_client_a_aaaaaaaaaaaaaaaaaaaaaaaa";
        const string curB = "cur_client_b_bbbbbbbbbbbbbbbbbbbbbbbb";
        const string curC = "cur_client_c_cccccccccccccccccccccccc";
        const string pathA = "docs/a.md";
        const string pathB = "docs/b.md";
        const string pathC = "docs/c.md";
        var now = DateTimeOffset.UtcNow;

        var conversation = Conversation.Create("header:tip-repair-leak", now);
        conversation.CaptureSystemPromptIfAbsent("You are helpful.");
        // Client synced: system + user + remapped native assistant (3 reads).
        conversation.SetSyncedMessageCount(3, now);

        var irAssistantWire =
            "{\"role\":\"assistant\",\"content\":\"\\n\\n\",\"tool_calls\":[" +
            "{\"id\":\"" + irA + "\",\"type\":\"function\",\"function\":{\"name\":\"" +
            ToolSchemaConstants.FileManifestToolName +
            "\",\"arguments\":\"{\\\"path\\\":\\\"" + pathA + "\\\"}\"}}," +
            "{\"id\":\"" + irB + "\",\"type\":\"function\",\"function\":{\"name\":\"" +
            ToolSchemaConstants.FileManifestToolName +
            "\",\"arguments\":\"{\\\"path\\\":\\\"" + pathB + "\\\"}\"}}," +
            "{\"id\":\"" + irC + "\",\"type\":\"function\",\"function\":{\"name\":\"" +
            ToolSchemaConstants.FileManifestToolName +
            "\",\"arguments\":\"{\\\"path\\\":\\\"" + pathC + "\\\"}\"}}]}";

        var stored = new List<ConversationMessage>
        {
            ConversationMessage.Create(conversation.Id, 0, MessageRole.User, "Read the three docs.", 5, now),
            ConversationMessage.Create(
                conversation.Id,
                1,
                MessageRole.Assistant,
                "[tool_calls: comprexy_read_file_manifest, comprexy_read_file_manifest, comprexy_read_file_manifest]",
                5,
                now,
                irAssistantWire)
        };

        var addedMessages = new List<ConversationMessage>();
        _conversationRepository.Setup(r => r.FindByKeyAsync("header:tip-repair-leak", It.IsAny<CancellationToken>()))
            .ReturnsAsync(conversation);
        _messageRepository.Setup(r => r.GetByConversationIdAsync(conversation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => stored.Concat(addedMessages).OrderBy(m => m.Sequence).ToList());
        _workingMemoryRepository.Setup(r => r.GetLatestAsync(conversation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkingMemory?)null);
        _messageRepository.Setup(r => r.Add(It.IsAny<ConversationMessage>()))
            .Callback<ConversationMessage>(m => addedMessages.Add(m));

        SetupCompressionMapperForReadFileAndBash();
        UpstreamRequest? forwarded = null;
        _chatCompletionClient
            .Setup(c => c.CompleteAsync(
                It.IsAny<ProviderEndpoint>(),
                It.Is<UpstreamRequest>(r => r.Purpose == UpstreamRequestPurpose.Chat),
                It.IsAny<CancellationToken>()))
            .Callback<ProviderEndpoint, UpstreamRequest, CancellationToken>((_, request, _) => forwarded = request)
            .ReturnsAsync(new UpstreamChatResult("ack", "stop", 10, 2));

        var service = CreateService();
        void SeedPending(string irId, string curId, string path) =>
            _callIdMapRepo.Add(ConversationToolCallMap.CreatePending(
                conversation.Id,
                irId,
                curId,
                ToolSchemaConstants.FileManifestToolName,
                "read",
                "{\"path\":\"" + path + "\"}",
                "{\"filePath\":\"" + path + "\"}",
                "direct",
                path,
                null,
                null,
                now));

        SeedPending(irA, curA, pathA);
        SeedPending(irB, curB, pathB);
        SeedPending(irC, curC, pathC);

        var payload = new
        {
            model = "client-model",
            tools = new object[]
            {
                new
                {
                    type = "function",
                    function = new
                    {
                        name = "read",
                        description = "Read a file.",
                        parameters = new
                        {
                            type = "object",
                            properties = new { filePath = new { type = "string" } },
                            required = new[] { "filePath" }
                        }
                    }
                },
                new
                {
                    type = "function",
                    function = new
                    {
                        name = "bash",
                        description = "Run a shell command.",
                        parameters = new
                        {
                            type = "object",
                            properties = new { command = new { type = "string" } },
                            required = new[] { "command" }
                        }
                    }
                }
            },
            messages = new object[]
            {
                new { role = "system", content = "You are helpful." },
                new { role = "user", content = "Read the three docs." },
                new
                {
                    role = "assistant",
                    content = "\n\n",
                    tool_calls = new object[]
                    {
                        new
                        {
                            id = curA,
                            type = "function",
                            function = new { name = "read", arguments = "{\"filePath\":\"" + pathA + "\"}" }
                        },
                        new
                        {
                            id = curB,
                            type = "function",
                            function = new { name = "read", arguments = "{\"filePath\":\"" + pathB + "\"}" }
                        },
                        new
                        {
                            id = curC,
                            type = "function",
                            function = new { name = "read", arguments = "{\"filePath\":\"" + pathC + "\"}" }
                        }
                    }
                },
                new
                {
                    role = "tool",
                    tool_call_id = curA,
                    content = "<path>/workspace/repo/" + pathA + "</path><type>file</type><content>\n1: # A\n</content>"
                },
                new
                {
                    role = "tool",
                    tool_call_id = curB,
                    content = "<path>/workspace/repo/" + pathB + "</path><type>file</type><content>\n1: # B\n</content>"
                },
                new
                {
                    role = "tool",
                    tool_call_id = curC,
                    content = "<path>/workspace/repo/" + pathC + "</path><type>file</type><content>\n1: # C\n</content>"
                }
            }
        };
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        var request = Comprexy.Api.Mapping.ChatCompletionRequestParser.Parse(
            document.RootElement.Clone(),
            "tip-repair-leak");

        await service.HandleAsync(request, CancellationToken.None);

        var addedTools = addedMessages.Where(m => m.Role == MessageRole.Tool).ToList();
        Assert.Equal(3, addedTools.Count);
        Assert.All(addedTools, m => Assert.Contains("\"type\":\"file_manifest\"", m.Content, StringComparison.Ordinal));
        Assert.DoesNotContain(addedTools, m => m.Content.Contains("<path>", StringComparison.Ordinal));
        Assert.DoesNotContain(
            addedTools,
            m => m.RawWireJson != null && m.RawWireJson.Contains(curC, StringComparison.Ordinal));

        Assert.NotNull(forwarded);
        var forwardedTools = forwarded!.Messages.Where(m => m.Role == MessageRole.Tool).Select(m => m.Content).ToList();
        Assert.Equal(3, forwardedTools.Count);
        Assert.DoesNotContain(forwardedTools, c => c != null && c.Contains("<path>", StringComparison.Ordinal));
    }

    [Fact]
    public async Task HandleAsync_ModeOff_SuccessfulTurn_ChatSaveOnce_MapClearIsIsolated()
    {
        _toolSchemaOptions = new ToolSchemaOptions { Mode = ToolSchemaMode.Off };
        _estimatedTokensToReturn = 10;
        _conversationRepository.Setup(r => r.FindByKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Conversation?)null);
        _workingMemoryRepository.Setup(r => r.GetLatestAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkingMemory?)null);
        _chatCompletionClient
            .Setup(c => c.CompleteAsync(It.IsAny<ProviderEndpoint>(), It.IsAny<UpstreamRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpstreamChatResult("ok", "stop", 10, 2));

        var service = CreateService();
        await service.HandleAsync(BuildRequest(), CancellationToken.None);

        // Complete flush only on chat UoW; end-of-turn dual-id clear uses isolated map UoW.
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        Assert.True(_mapSaveChangesCount >= 1);
        Assert.Empty(_callIdMapRepo.Rows);
    }

    [Fact]
    public async Task HandleAsync_CacheAlignmentEnabled_PrepareUsesMaterializeLive()
    {
        _estimatedTokensToReturn = 10;
        var spy = new Mock<ICacheAlignmentService>();
        spy.Setup(s => s.GetSnapshot(It.IsAny<Guid>())).Returns((CacheAlignmentSnapshot?)null);
        spy.Setup(s => s.TryStorePrefix(
                It.IsAny<Guid>(),
                It.IsAny<IReadOnlyList<ChatMessage>>(),
                It.IsAny<IReadOnlyList<Guid>>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<string?>()))
            .Returns(true);
        spy.Setup(s => s.MaterializeLive(
                It.IsAny<Guid>(),
                It.IsAny<IReadOnlyDictionary<Guid, ConversationMessage>>(),
                It.IsAny<Func<IReadOnlyList<ConversationMessage>, IReadOnlyList<ConversationMessage>>?>()))
            .Returns((Guid _, IReadOnlyDictionary<Guid, ConversationMessage> _, Func<IReadOnlyList<ConversationMessage>, IReadOnlyList<ConversationMessage>>? _) =>
                new List<ChatMessage>
                {
                    new(MessageRole.System, "You are helpful."),
                    new(MessageRole.User, "Hello")
                });

        _conversationRepository.Setup(r => r.FindByKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Conversation?)null);
        _workingMemoryRepository.Setup(r => r.GetLatestAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkingMemory?)null);
        _chatCompletionClient
            .Setup(c => c.CompleteAsync(It.IsAny<ProviderEndpoint>(), It.IsAny<UpstreamRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpstreamChatResult("ok", "stop", 10, 2));

        var service = CreateService(cacheAlignment: spy.Object);
        await service.HandleAsync(BuildRequest(conversationHeader: "ca-materialize"), CancellationToken.None);

        spy.Verify(s => s.TryStorePrefix(
            It.IsAny<Guid>(),
            It.IsAny<IReadOnlyList<ChatMessage>>(),
            It.IsAny<IReadOnlyList<Guid>>(),
            It.IsAny<int>(),
            It.IsAny<int>(),
            It.IsAny<string?>()), Times.Once);
        // Live materialize forwards Prefix ⊕ Suffix verbatim: no omit callback, so the tip cannot be
        // dropped and frozen Prefix bytes are never rewritten.
        spy.Verify(s => s.MaterializeLive(
            It.IsAny<Guid>(),
            It.IsAny<IReadOnlyDictionary<Guid, ConversationMessage>>(),
            null), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_MaterializeLiveDropsTip_ReAppendsTipBeforeUpstream()
    {
        _estimatedTokensToReturn = 10;
        var spy = new Mock<ICacheAlignmentService>();
        spy.Setup(s => s.GetSnapshot(It.IsAny<Guid>())).Returns((CacheAlignmentSnapshot?)null);
        spy.Setup(s => s.TryStorePrefix(
                It.IsAny<Guid>(),
                It.IsAny<IReadOnlyList<ChatMessage>>(),
                It.IsAny<IReadOnlyList<Guid>>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<string?>()))
            .Returns(true);
        // Projection that loses the newest turn — the failure this guard exists for.
        spy.Setup(s => s.MaterializeLive(
                It.IsAny<Guid>(),
                It.IsAny<IReadOnlyDictionary<Guid, ConversationMessage>>(),
                It.IsAny<Func<IReadOnlyList<ConversationMessage>, IReadOnlyList<ConversationMessage>>?>()))
            .Returns(new List<ChatMessage>
            {
                new(MessageRole.System, "You are helpful."),
                new(MessageRole.Assistant, "mid-chain"),
                new(MessageRole.Tool, "Wrote file successfully.")
            });

        _conversationRepository.Setup(r => r.FindByKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Conversation?)null);
        _workingMemoryRepository.Setup(r => r.GetLatestAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkingMemory?)null);

        UpstreamRequest? forwarded = null;
        _chatCompletionClient
            .Setup(c => c.CompleteAsync(It.IsAny<ProviderEndpoint>(), It.IsAny<UpstreamRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ProviderEndpoint, UpstreamRequest, CancellationToken>((_, request, _) => forwarded = request)
            .ReturnsAsync(new UpstreamChatResult("ok", "stop", 10, 2));

        var service = CreateService(cacheAlignment: spy.Object);
        await service.HandleAsync(
            BuildRequest(conversationHeader: "ca-tip-guard", userContent: "Stop. Explain first."),
            CancellationToken.None);

        Assert.NotNull(forwarded);
        Assert.Equal(MessageRole.User, forwarded!.Messages[^1].Role);
        Assert.Equal("Stop. Explain first.", forwarded.Messages[^1].Content);
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    public async Task HandleAsync_DuplicateFailedEdit_OmitsOlderFailureAndKeepsUserTip(
        bool dedupeEnabled,
        int expectedFailureRows)
    {
        _policy.DedupeDuplicateFailedEdits = dedupeEnabled;
        _estimatedTokensToReturn = 10;
        var now = DateTimeOffset.UtcNow;

        var conversation = Conversation.Create("header:failed-edit-omit", now);
        conversation.CaptureSystemPromptIfAbsent("You are helpful.");
        // Client already synced system + user + two failed edit rounds (6 messages); only the tip is new.
        conversation.SetSyncedMessageCount(6, now);

        const string failure = "Could not find oldString in the file.";
        static string EditAssistantWire(string callId) =>
            "{\"role\":\"assistant\",\"content\":\"\",\"tool_calls\":[{\"id\":\"" + callId +
            "\",\"type\":\"function\",\"function\":{\"name\":\"edit\",\"arguments\":" +
            "\"{\\\"filePath\\\":\\\"scripts/app.ps1\\\",\\\"oldString\\\":\\\"switch block\\\"}\"}}]}";
        static string ToolWire(string callId) =>
            "{\"role\":\"tool\",\"tool_call_id\":\"" + callId + "\",\"content\":\"" +
            "Could not find oldString in the file.\"}";

        var stored = new List<ConversationMessage>
        {
            ConversationMessage.Create(conversation.Id, 0, MessageRole.User, "add the bench command", 5, now),
            ConversationMessage.Create(
                conversation.Id, 1, MessageRole.Assistant, string.Empty, 5, now, EditAssistantWire("call_older")),
            ConversationMessage.Create(
                conversation.Id, 2, MessageRole.Tool, failure, 5, now, ToolWire("call_older")),
            ConversationMessage.Create(
                conversation.Id, 3, MessageRole.Assistant, string.Empty, 5, now, EditAssistantWire("call_newer")),
            ConversationMessage.Create(
                conversation.Id, 4, MessageRole.Tool, failure, 5, now, ToolWire("call_newer"))
        };

        _conversationRepository.Setup(r => r.FindByKeyAsync("header:failed-edit-omit", It.IsAny<CancellationToken>()))
            .ReturnsAsync(conversation);
        _messageRepository.Setup(r => r.GetByConversationIdAsync(conversation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);
        _workingMemoryRepository.Setup(r => r.GetLatestAsync(conversation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkingMemory?)null);

        UpstreamRequest? forwarded = null;
        _chatCompletionClient
            .Setup(c => c.CompleteAsync(It.IsAny<ProviderEndpoint>(), It.IsAny<UpstreamRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ProviderEndpoint, UpstreamRequest, CancellationToken>((_, request, _) => forwarded = request)
            .ReturnsAsync(new UpstreamChatResult("ok", "stop", 10, 2));

        var payload = new
        {
            model = "client-model",
            messages = new object[]
            {
                new { role = "system", content = "You are helpful." },
                new { role = "user", content = "add the bench command" },
                new
                {
                    role = "assistant",
                    content = string.Empty,
                    tool_calls = new object[]
                    {
                        new
                        {
                            id = "call_older",
                            type = "function",
                            function = new
                            {
                                name = "edit",
                                arguments = "{\"filePath\":\"scripts/app.ps1\",\"oldString\":\"switch block\"}"
                            }
                        }
                    }
                },
                new { role = "tool", tool_call_id = "call_older", content = failure },
                new
                {
                    role = "assistant",
                    content = string.Empty,
                    tool_calls = new object[]
                    {
                        new
                        {
                            id = "call_newer",
                            type = "function",
                            function = new
                            {
                                name = "edit",
                                arguments = "{\"filePath\":\"scripts/app.ps1\",\"oldString\":\"switch block\"}"
                            }
                        }
                    }
                },
                new { role = "tool", tool_call_id = "call_newer", content = failure },
                new { role = "user", content = "Stop. Why are we editing that file?" }
            }
        };
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        var request = Comprexy.Api.Mapping.ChatCompletionRequestParser.Parse(
            document.RootElement.Clone(),
            "failed-edit-omit");

        var service = CreateService();
        await service.HandleAsync(request, CancellationToken.None);

        Assert.NotNull(forwarded);
        Assert.Equal(MessageRole.User, forwarded!.Messages[^1].Role);
        Assert.Equal("Stop. Why are we editing that file?", forwarded.Messages[^1].Content);
        Assert.Equal(
            expectedFailureRows,
            forwarded.Messages.Count(m => m.Role == MessageRole.Tool && m.Content == failure));

        var carriesOlderCall = forwarded.Messages.Any(m =>
            m.RawWireMessage is { } raw && raw.GetRawText().Contains("call_older", StringComparison.Ordinal));
        Assert.Equal(!dedupeEnabled, carriesOlderCall);
    }

    [Fact]
    public async Task HandleAsync_CacheAlignmentDisabled_DoesNotCallMaterializeLive()
    {
        _cacheAlignmentOptions = new CacheAlignmentOptions { Enabled = false };
        _estimatedTokensToReturn = 10;
        var spy = new Mock<ICacheAlignmentService>();

        _conversationRepository.Setup(r => r.FindByKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Conversation?)null);
        _workingMemoryRepository.Setup(r => r.GetLatestAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkingMemory?)null);
        _chatCompletionClient
            .Setup(c => c.CompleteAsync(It.IsAny<ProviderEndpoint>(), It.IsAny<UpstreamRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpstreamChatResult("ok", "stop", 10, 2));

        var service = CreateService(cacheAlignment: spy.Object);
        await service.HandleAsync(BuildRequest(conversationHeader: "ca-disabled"), CancellationToken.None);

        spy.Verify(s => s.MaterializeLive(
            It.IsAny<Guid>(),
            It.IsAny<IReadOnlyDictionary<Guid, ConversationMessage>>(),
            It.IsAny<Func<IReadOnlyList<ConversationMessage>, IReadOnlyList<ConversationMessage>>?>()), Times.Never);
        spy.Verify(s => s.TryStorePrefix(
            It.IsAny<Guid>(),
            It.IsAny<IReadOnlyList<ChatMessage>>(),
            It.IsAny<IReadOnlyList<Guid>>(),
            It.IsAny<int>(),
            It.IsAny<int>(),
            It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_TwoPrepares_PrefixBytesStayFrozen()
    {
        _policy.DedupeDuplicateFailedEdits = true;
        _estimatedTokensToReturn = 10;
        var alignment = new CacheAlignmentService(Options.Create(_cacheAlignmentOptions));
        var conversation = Conversation.Create("header:ca-omit", DateTimeOffset.UtcNow);
        conversation.CaptureSystemPromptIfAbsent("You are helpful.");
        conversation.SetSyncedMessageCount(0, DateTimeOffset.UtcNow);
        var stored = new List<ConversationMessage>();

        _conversationRepository.Setup(r => r.FindByKeyAsync("header:ca-omit", It.IsAny<CancellationToken>()))
            .ReturnsAsync(conversation);
        _messageRepository.Setup(r => r.GetByConversationIdAsync(conversation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => stored.ToList());
        _messageRepository.Setup(r => r.Add(It.IsAny<ConversationMessage>()))
            .Callback<ConversationMessage>(m => stored.Add(m));
        _workingMemoryRepository.Setup(r => r.GetLatestAsync(conversation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkingMemory?)null);
        _chatCompletionClient
            .Setup(c => c.CompleteAsync(It.IsAny<ProviderEndpoint>(), It.IsAny<UpstreamRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpstreamChatResult("ok", "stop", 10, 2));

        var service = CreateService(cacheAlignment: alignment);
        await service.HandleAsync(BuildRequest(conversationHeader: "ca-omit", userContent: "first"), CancellationToken.None);
        var afterFirst = alignment.GetSnapshot(conversation.Id);
        Assert.NotNull(afterFirst);
        var prefixBytes = afterFirst!.Prefix.ToList();

        // Grow client history past synced cursor so the second prepare Appends without rewind.
        var payload = new
        {
            model = "client-model",
            stream = false,
            temperature = 0.2,
            tools = new object[]
            {
                new { type = "function", function = new { name = "lookup" } }
            },
            messages = new object[]
            {
                new { role = "system", content = "You are helpful." },
                new { role = "user", content = "first" },
                new { role = "assistant", content = "ok" },
                new { role = "user", content = "second" }
            }
        };
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        var secondRequest = Comprexy.Api.Mapping.ChatCompletionRequestParser.Parse(
            document.RootElement.Clone(),
            "ca-omit");

        await service.HandleAsync(secondRequest, CancellationToken.None);
        var afterSecond = alignment.GetSnapshot(conversation.Id);
        Assert.NotNull(afterSecond);
        Assert.True(CacheAlignmentService.ArePrefixEqual(prefixBytes, afterSecond!.Prefix));
    }

    [Fact]
    public async Task HandleAsync_PassThrough_DoesNotCallCacheAlignmentMaterialize()
    {
        _proxyOptions = new ProxyOptions { PassThrough = true };
        _estimatedTokensToReturn = 10;
        var spy = new Mock<ICacheAlignmentService>();

        _conversationRepository.Setup(r => r.FindByKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Conversation?)null);
        _chatCompletionClient
            .Setup(c => c.CompleteAsync(It.IsAny<ProviderEndpoint>(), It.IsAny<UpstreamRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpstreamChatResult("ok", "stop", 10, 2));

        var service = CreateService(cacheAlignment: spy.Object);
        await service.HandleAsync(BuildRequest(conversationHeader: "ca-passthrough"), CancellationToken.None);

        spy.Verify(s => s.MaterializeLive(
            It.IsAny<Guid>(),
            It.IsAny<IReadOnlyDictionary<Guid, ConversationMessage>>(),
            It.IsAny<Func<IReadOnlyList<ConversationMessage>, IReadOnlyList<ConversationMessage>>?>()), Times.Never);
        spy.Verify(s => s.TryStorePrefix(
            It.IsAny<Guid>(),
            It.IsAny<IReadOnlyList<ChatMessage>>(),
            It.IsAny<IReadOnlyList<Guid>>(),
            It.IsAny<int>(),
            It.IsAny<int>(),
            It.IsAny<string?>()), Times.Never);
    }

    private static string CreateContentSseChunk(string content) =>
        "{\"choices\":[{\"delta\":{\"content\":" + JsonSerializer.Serialize(content) + "}}]}";
}
