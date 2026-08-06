using System.Text.Json;
using Comprexy.Application.Abstractions;
using Comprexy.Application.Configuration;
using Comprexy.Application.Models;
using Comprexy.Application.Services;
using Comprexy.Application.Services.CacheAlignment;
using Comprexy.Application.Services.ChatTurn;
using Comprexy.Application.Services.Rules;
using Comprexy.Application.Services.Settings;
using Comprexy.Application.Services.ToolIr;
using Comprexy.Domain.Entities;
using Comprexy.Domain.Enums;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Comprexy.Application.Tests.Services.Settings;

/// <summary>
/// Shared wiring for Slice C preparer/completer tests using primary ctors + real accessor Set order.
/// </summary>
internal sealed class SliceCTestHarness
{
    public Mock<IConversationRepository> ConversationRepository { get; } = new();
    public Mock<IConversationMessageRepository> MessageRepository { get; } = new();
    public Mock<IWorkingMemoryRepository> WorkingMemoryRepository { get; } = new();
    public Mock<ICompressionEventRepository> CompressionEventRepository { get; } = new();
    public Mock<ITokenEstimator> TokenEstimator { get; } = new();
    public Mock<IChatCompletionClient> ChatCompletionClient { get; } = new();
    public Mock<IClock> Clock { get; } = new();
    public Mock<IConversationToolCatalogRepository> ToolCatalogRepository { get; } = new();
    public Mock<IConversationToolDefinitionRepository> ToolDefinitionRepository { get; } = new();
    public Mock<ICacheAlignmentService> CacheAlignment { get; } = new();
    public Mock<IConversationMetricsRecorder> MetricsRecorder { get; } = new();

    public ProxyOptions Proxy { get; set; } = new();
    public ContextPolicyOptions Policy { get; set; } = new() { SoftLimitTokens = 100, CompressionRetainMessageCount = 1 };
    public CacheAlignmentOptions CacheAlignmentOptions { get; set; } = new() { Enabled = true, MaxConversations = 1024 };
    public MetricsOptions Metrics { get; set; } = new() { Enabled = false };
    public ToolSchemaOptions ToolSchema { get; set; } = new() { Mode = ToolSchemaMode.Off };

    public EffectiveSettingsAccessor Accessor { get; } = new();
    public int EstimatedTokens { get; set; } = 10;

    private Conversation? _trackedConversation;
    private readonly List<ConversationMessage> _storedMessages = [];
    private ToolSchemaOrchestrator? _lastOrchestrator;

    public SliceCTestHarness()
    {
        Clock.Setup(c => c.UtcNow).Returns(DateTimeOffset.UtcNow);
        TokenEstimator.Setup(t => t.CountTokens(It.IsAny<string>())).Returns(5);
        TokenEstimator.Setup(t => t.CountTokens(It.IsAny<IEnumerable<ChatMessage>>())).Returns(() => EstimatedTokens);
        TokenEstimator.Setup(t => t.CountPromptTokens(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<JsonElement?>()))
            .Returns(() => EstimatedTokens);

        ConversationRepository
            .Setup(r => r.FindByKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, CancellationToken _) =>
                _trackedConversation is not null && _trackedConversation.ConversationKey == key
                    ? _trackedConversation
                    : null);
        ConversationRepository
            .Setup(r => r.Add(It.IsAny<Conversation>()))
            .Callback<Conversation>(c => _trackedConversation = c);

        MessageRepository
            .Setup(r => r.GetByConversationIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _storedMessages.ToList());
        MessageRepository
            .Setup(r => r.Add(It.IsAny<ConversationMessage>()))
            .Callback<ConversationMessage>(m => _storedMessages.Add(m));

        WorkingMemoryRepository
            .Setup(r => r.GetLatestAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkingMemory?)null);
        CompressionEventRepository
            .Setup(r => r.GetLatestSucceededAsync(It.IsAny<Guid>(), CompressionMode.Inline, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CompressionEvent?)null);

        ToolCatalogRepository
            .Setup(r => r.GetByConversationIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ConversationToolCatalog?)null);
        ToolCatalogRepository
            .Setup(r => r.GetTrackedByConversationIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ConversationToolCatalog?)null);
        ToolDefinitionRepository
            .Setup(r => r.GetByConversationIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        MetricsRecorder.SetupGet(m => m.IsEnabled).Returns(() =>
            Accessor.IsSet ? Accessor.Current.MetricsEnabled : Metrics.Enabled);
        MetricsRecorder
            .Setup(m => m.RecordSuccessfulTurnAsync(It.IsAny<SuccessfulTurnMetricInput>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        MetricsRecorder
            .Setup(m => m.RecordCompressionOverheadAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        ChatCompletionClient
            .Setup(c => c.CompleteAsync(It.IsAny<ProviderEndpoint>(), It.IsAny<UpstreamRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpstreamChatResult("ack", "stop", 10, 2));
    }

    public Conversation? TrackedConversation => _trackedConversation;

    public IReadOnlyList<ConversationMessage> StoredMessages => _storedMessages;

    public void SeedExistingConversation(Conversation conversation, IEnumerable<ConversationMessage>? messages = null)
    {
        _trackedConversation = conversation;
        _storedMessages.Clear();
        if (messages is not null)
        {
            _storedMessages.AddRange(messages);
        }
    }

    public ToolSchemaOrchestrator CreateOrchestrator()
    {
        var toolSchemaOptions = new FixedOptionsMonitor<ToolSchemaOptions>(ToolSchema);
        var endpointResolver = new ProviderEndpointResolver(
            Options.Create(new ProviderOptions { BaseUrl = "http://upstream.example.test", ApiKey = "k", Model = "target-model" }),
            Options.Create(new CompressionOptions()));
        var iOptionsToolSchema = Options.Create(ToolSchema);
        var fileCache = new ToolIrFileBodyCache(iOptionsToolSchema);
        var callIdMapService = new ToolIrCallIdMapService(
            new ToolIrCallIdMap(Clock.Object, iOptionsToolSchema),
            new InMemoryToolIrCallIdMapUnitOfWorkFactory(new InMemoryConversationToolCallMapRepository()),
            Clock.Object,
            iOptionsToolSchema);

        _lastOrchestrator = new ToolSchemaOrchestrator(
            Accessor,
            toolSchemaOptions,
            new ToolCatalogParser(),
            new ToolArgumentValidator(),
            new ToolIrSchemaMapper(
                iOptionsToolSchema,
                Options.Create(new CompressionOptions()),
                endpointResolver,
                ChatCompletionClient.Object,
                TokenEstimator.Object,
                MetricsRecorder.Object,
                NullLogger<ToolIrSchemaMapper>.Instance),
            new ToolIrPlanner(iOptionsToolSchema, fileCache),
            ToolIrTestFactory.CreateDistiller(iOptionsToolSchema, fileCache),
            callIdMapService,
            ToolCatalogRepository.Object,
            ToolDefinitionRepository.Object,
            ChatCompletionClient.Object,
            Clock.Object,
            ToolIrTestFactory.CreateShapeStore(ToolSchema),
            NullLogger<ToolSchemaOrchestrator>.Instance);
        return _lastOrchestrator;
    }

    public ChatTurnPreparer CreatePreparer(ToolSchemaOrchestrator? orchestrator = null)
    {
        orchestrator ??= CreateOrchestrator();
        var endpointResolver = new ProviderEndpointResolver(
            Options.Create(new ProviderOptions { BaseUrl = "http://upstream.example.test", ApiKey = "k", Model = "target-model" }),
            Options.Create(new CompressionOptions()));

        var contextBuilder = new ContextBuilder();
        var alignment = CacheAlignment.Object;
        var messageHelper = new ChatTurnMessageHelper(MessageRepository.Object, TokenEstimator.Object);
        var historySynchronizer = new ClientHistorySynchronizer(
            MessageRepository.Object,
            WorkingMemoryRepository.Object,
            orchestrator,
            alignment,
            TokenEstimator.Object,
            Accessor,
            new FixedOptionsMonitor<ProxyOptions>(Proxy),
            new FixedOptionsMonitor<CacheAlignmentOptions>(CacheAlignmentOptions),
            NullLogger<ClientHistorySynchronizer>.Instance);
        var contextMaterializer = new OutgoingContextMaterializer(
            contextBuilder,
            alignment,
            Accessor,
            new FixedOptionsMonitor<ContextPolicyOptions>(Policy),
            new FixedOptionsMonitor<CacheAlignmentOptions>(CacheAlignmentOptions),
            NullLogger<OutgoingContextMaterializer>.Instance);

        return new ChatTurnPreparer(
            ConversationRepository.Object,
            MessageRepository.Object,
            WorkingMemoryRepository.Object,
            CompressionEventRepository.Object,
            TokenEstimator.Object,
            contextBuilder,
            alignment,
            new ContextBudgetEvaluator(Options.Create(Policy)),
            new CompressionPromptFactory(
                "inline instruction",
                """
                # Working Memory

                ## Current Goal
                ...
                """),
            orchestrator,
            historySynchronizer,
            contextMaterializer,
            new IrFullPromptEstimator(
                new RulesInjector(),
                contextBuilder,
                contextMaterializer,
                TokenEstimator.Object),
            messageHelper,
            new SystemRulesDetector(),
            new TranscriptRulesDetector(),
            new RulesConsolidator(NullLogger<RulesConsolidator>.Instance),
            new RulesInjector(),
            endpointResolver,
            MetricsRecorder.Object,
            Accessor,
            Clock.Object,
            new FixedOptionsMonitor<ContextPolicyOptions>(Policy),
            new FixedOptionsMonitor<ProxyOptions>(Proxy),
            new FixedOptionsMonitor<CacheAlignmentOptions>(CacheAlignmentOptions),
            new FixedOptionsMonitor<MetricsOptions>(Metrics),
            new FixedOptionsMonitor<ToolSchemaOptions>(ToolSchema),
            Mock.Of<IPayloadTraceLogger>(),
            Mock.Of<IRequestTraceFileSession>(),
            NullLogger<ChatTurnPreparer>.Instance);
    }

    public ChatTurnCompleter CreateCompleter(ToolSchemaOrchestrator? orchestrator = null)
    {
        orchestrator ??= _lastOrchestrator ?? CreateOrchestrator();
        var messageHelper = new ChatTurnMessageHelper(MessageRepository.Object, TokenEstimator.Object);
        var contextBuilder = new ContextBuilder();
        var inlineWrapUpRunner = new InlineWrapUpRunner(
            MessageRepository.Object,
            WorkingMemoryRepository.Object,
            CompressionEventRepository.Object,
            new CompressionPromptFactory("inline instruction", "# Working Memory\n## Current Goal\n..."),
            ChatCompletionClient.Object,
            TokenEstimator.Object,
            contextBuilder,
            CacheAlignment.Object,
            new RecentContextSelector(Options.Create(Policy)),
            MetricsRecorder.Object,
            Clock.Object,
            Accessor,
            new FixedOptionsMonitor<CacheAlignmentOptions>(CacheAlignmentOptions),
            NullLogger<InlineWrapUpRunner>.Instance);

        return new ChatTurnCompleter(
            MessageRepository.Object,
            orchestrator,
            MetricsRecorder.Object,
            inlineWrapUpRunner,
            messageHelper,
            TokenEstimator.Object,
            Clock.Object,
            Accessor,
            new FixedOptionsMonitor<ContextPolicyOptions>(Policy),
            NullLogger<ChatTurnCompleter>.Instance);
    }

    public ProxyChatCompletionService CreateService()
    {
        var orchestrator = CreateOrchestrator();
        return new ProxyChatCompletionService(
            new ConversationIdentityResolver(),
            new ConversationRequestGate(),
            ChatCompletionClient.Object,
            orchestrator,
            CreatePreparer(orchestrator),
            CreateCompleter(orchestrator),
            Mock.Of<IUnitOfWork>(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()) == Task.CompletedTask),
            Mock.Of<IHostApplicationLifetime>(h => h.ApplicationStopping == CancellationToken.None),
            NullLogger<ProxyChatCompletionService>.Instance);
    }

    public static IncomingChatRequest BuildRequest(
        string conversationHeader = "slice-c-1",
        string userContent = "Hello",
        string systemContent = "You are helpful.")
    {
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
                new { role = "system", content = systemContent },
                new { role = "user", content = userContent }
            }
        };

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        return Comprexy.Api.Mapping.ChatCompletionRequestParser.Parse(document.RootElement.Clone(), conversationHeader);
    }
}
