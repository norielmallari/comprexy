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

public class CompressionOrchestratorTests
{
    private readonly Mock<IConversationRepository> _conversationRepository = new();
    private readonly Mock<IConversationMessageRepository> _messageRepository = new();
    private readonly Mock<IWorkingMemoryRepository> _workingMemoryRepository = new();
    private readonly Mock<ICompressionEventRepository> _compressionEventRepository = new();
    private readonly Mock<IChatCompletionClient> _chatCompletionClient = new();
    private readonly Mock<ITokenEstimator> _tokenEstimator = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IClock> _clock = new();

    private ContextPolicyOptions _policy = new()
    {
        CompressionRetainMessageCount = 2,
        EmergencyRecentMessageCount = 1,
        CompressionMaxInputTokens = 52_000
    };

    private CompressionOrchestrator CreateOrchestrator(
        ProviderOptions? providerOptions = null,
        CompressionOptions? compressionOptions = null)
    {
        _clock.Setup(c => c.UtcNow).Returns(DateTimeOffset.UtcNow);
        _tokenEstimator
            .Setup(t => t.CountTokens(It.IsAny<string>()))
            .Returns((string text) => string.IsNullOrEmpty(text) ? 0 : 10);

        providerOptions ??= new ProviderOptions { BaseUrl = "http://upstream", ApiKey = "k", Model = "m" };
        compressionOptions ??= new CompressionOptions();

        return new CompressionOrchestrator(
            _conversationRepository.Object,
            _messageRepository.Object,
            _workingMemoryRepository.Object,
            _compressionEventRepository.Object,
            _chatCompletionClient.Object,
            _tokenEstimator.Object,
            _unitOfWork.Object,
            _clock.Object,
            new ProviderEndpointResolver(
                Options.Create(providerOptions),
                Options.Create(compressionOptions)),
            new CompressionPromptFactory(
                "You are updating working memory.",
                "Update working memory. End with ## Retain Sequences."),
            new ContextBuilder(),
            new RecentContextSelector(Options.Create(_policy)),
            Mock.Of<IConversationMetricsRecorder>(m => m.IsEnabled == false),
            Options.Create(_policy),
            Options.Create(compressionOptions),
            NullLogger<CompressionOrchestrator>.Instance);
    }

    private void SetupMessages(Guid conversationId, IReadOnlyList<ConversationMessage> messages)
    {
        _messageRepository
            .Setup(r => r.GetByConversationIdAsync(conversationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(messages.ToList());
    }

    private static ConversationMessage Message(
        Guid conversationId,
        int sequence,
        MessageRole role = MessageRole.User,
        int tokenCount = 10) =>
        ConversationMessage.Create(conversationId, sequence, role, $"message-{sequence}", tokenCount, DateTimeOffset.UtcNow);

    [Fact]
    public async Task RunAsync_WhenConversationNotFound_ReturnsNull()
    {
        _conversationRepository.Setup(r => r.FindByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Conversation?)null);

        var orchestrator = CreateOrchestrator();

        var result = await orchestrator.RunAsync(Guid.NewGuid(), CompressionMode.Background, CancellationToken.None);

        Assert.Null(result);
        _chatCompletionClient.Verify(
            c => c.CompleteAsync(It.IsAny<ProviderEndpoint>(), It.IsAny<UpstreamRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RunAsync_WhenUnfoldedCountAtOrBelowRetainCount_IsNoOp()
    {
        var conversationId = Guid.NewGuid();
        var conversation = Conversation.Create("key", DateTimeOffset.UtcNow);
        _conversationRepository.Setup(r => r.FindByIdAsync(conversationId, It.IsAny<CancellationToken>())).ReturnsAsync(conversation);
        SetupMessages(conversationId, [Message(conversationId, 0), Message(conversationId, 1)]);

        var orchestrator = CreateOrchestrator();

        var result = await orchestrator.RunAsync(conversationId, CompressionMode.Background, CancellationToken.None);

        Assert.Null(result);
        _workingMemoryRepository.Verify(r => r.Add(It.IsAny<WorkingMemory>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_WhenOpenToolCalls_Soft_IsNoOp()
    {
        var conversationId = Guid.NewGuid();
        var conversation = Conversation.Create("key", DateTimeOffset.UtcNow);
        var messages = new List<ConversationMessage>
        {
            Message(conversationId, 0),
            Message(conversationId, 1),
            Message(conversationId, 2),
            ConversationMessage.Create(
                conversationId,
                3,
                MessageRole.Assistant,
                string.Empty,
                10,
                DateTimeOffset.UtcNow,
                """{"role":"assistant","tool_calls":[{"id":"c1","type":"function","function":{"name":"Read","arguments":"{}"}}]}"""),
            Message(conversationId, 4)
        };

        _conversationRepository.Setup(r => r.FindByIdAsync(conversationId, It.IsAny<CancellationToken>())).ReturnsAsync(conversation);
        SetupMessages(conversationId, messages);

        var orchestrator = CreateOrchestrator();
        var result = await orchestrator.RunAsync(conversationId, CompressionMode.Background, CancellationToken.None);

        Assert.Null(result);
        _chatCompletionClient.Verify(
            c => c.CompleteAsync(It.IsAny<ProviderEndpoint>(), It.IsAny<UpstreamRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _workingMemoryRepository.Verify(r => r.Add(It.IsAny<WorkingMemory>()), Times.Never);
        _compressionEventRepository.Verify(r => r.Add(It.IsAny<CompressionEvent>()), Times.Never);
        Assert.All(messages, m => Assert.False(m.IsFolded));
    }

    [Fact]
    public async Task RunAsync_WhenOpenToolCalls_Emergency_IsNoOp()
    {
        var conversationId = Guid.NewGuid();
        var conversation = Conversation.Create("key", DateTimeOffset.UtcNow);
        var messages = new List<ConversationMessage>
        {
            Message(conversationId, 0),
            Message(conversationId, 1),
            Message(conversationId, 2),
            ConversationMessage.Create(
                conversationId,
                3,
                MessageRole.Assistant,
                string.Empty,
                10,
                DateTimeOffset.UtcNow,
                """{"role":"assistant","tool_calls":[{"id":"c1","type":"function","function":{"name":"Read","arguments":"{}"}}]}""")
        };

        _conversationRepository.Setup(r => r.FindByIdAsync(conversationId, It.IsAny<CancellationToken>())).ReturnsAsync(conversation);
        SetupMessages(conversationId, messages);

        var orchestrator = CreateOrchestrator();
        var result = await orchestrator.RunAsync(conversationId, CompressionMode.Emergency, CancellationToken.None);

        Assert.Null(result);
        _chatCompletionClient.Verify(
            c => c.CompleteAsync(It.IsAny<ProviderEndpoint>(), It.IsAny<UpstreamRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _workingMemoryRepository.Verify(r => r.Add(It.IsAny<WorkingMemory>()), Times.Never);
        _compressionEventRepository.Verify(r => r.Add(It.IsAny<CompressionEvent>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_WhenUnderCompressionMax_UsesFullRawPrompt()
    {
        var conversationId = Guid.NewGuid();
        var conversation = Conversation.Create("key", DateTimeOffset.UtcNow);
        var messages = Enumerable.Range(0, 5)
            .Select(sequence => Message(conversationId, sequence))
            .ToList();

        _conversationRepository.Setup(r => r.FindByIdAsync(conversationId, It.IsAny<CancellationToken>())).ReturnsAsync(conversation);
        SetupMessages(conversationId, messages);
        _workingMemoryRepository.Setup(r => r.GetLatestAsync(conversationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(WorkingMemory.Create(conversationId, 1, "stale wm", 5, DateTimeOffset.UtcNow));
        UpstreamRequest? compressionRequest = null;
        _chatCompletionClient
            .Setup(c => c.CompleteAsync(It.IsAny<ProviderEndpoint>(), It.IsAny<UpstreamRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ProviderEndpoint, UpstreamRequest, CancellationToken>((_, request, _) => compressionRequest = request)
            .ReturnsAsync(new UpstreamChatResult("# Working Memory\n## Current Goal\nDone", "stop", 50, 20));

        var orchestrator = CreateOrchestrator();

        var result = await orchestrator.RunAsync(conversationId, CompressionMode.HighPriorityBackground, CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotNull(compressionRequest);
        var userPrompt = compressionRequest!.Messages.Last().Content;
        Assert.Contains("Full Conversation Transcript", userPrompt);
        Assert.Contains("fresh working memory", userPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("## Existing Working Memory", userPrompt);
        Assert.DoesNotContain("stale wm", userPrompt);
        Assert.True(messages[0].IsFolded);
        Assert.True(messages[1].IsFolded);
        Assert.True(messages[2].IsFolded);
        Assert.False(messages[3].IsFolded);
        Assert.False(messages[4].IsFolded);
        Assert.Equal(50, result!.OriginalTokens); // 5 messages * 10
    }

    [Fact]
    public async Task RunAsync_WhenProviderModelNull_UsesPreferredModel()
    {
        var conversationId = Guid.NewGuid();
        var conversation = Conversation.Create("key", DateTimeOffset.UtcNow);
        var messages = Enumerable.Range(0, 5)
            .Select(sequence => Message(conversationId, sequence))
            .ToList();

        _conversationRepository.Setup(r => r.FindByIdAsync(conversationId, It.IsAny<CancellationToken>())).ReturnsAsync(conversation);
        SetupMessages(conversationId, messages);
        ProviderEndpoint? usedEndpoint = null;
        _chatCompletionClient
            .Setup(c => c.CompleteAsync(It.IsAny<ProviderEndpoint>(), It.IsAny<UpstreamRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ProviderEndpoint, UpstreamRequest, CancellationToken>((endpoint, _, _) => usedEndpoint = endpoint)
            .ReturnsAsync(new UpstreamChatResult("# Working Memory\n## Current Goal\nDone", "stop", 50, 20));

        var orchestrator = CreateOrchestrator(
            new ProviderOptions { BaseUrl = "http://upstream", ApiKey = "k", Model = null });

        var result = await orchestrator.RunAsync(
            conversationId,
            CompressionMode.Background,
            CancellationToken.None,
            preferredModel: "client-model");

        Assert.NotNull(result);
        Assert.NotNull(usedEndpoint);
        Assert.Equal("client-model", usedEndpoint!.Model);
    }

    [Fact]
    public async Task RunAsync_WhenOverCompressionMax_UsesWorkingMemoryMerge()
    {
        _policy = new ContextPolicyOptions
        {
            CompressionRetainMessageCount = 2,
            EmergencyRecentMessageCount = 1,
            CompressionMaxInputTokens = 25
        };

        var conversationId = Guid.NewGuid();
        var conversation = Conversation.Create("key", DateTimeOffset.UtcNow);
        var messages = Enumerable.Range(0, 4)
            .Select(sequence => Message(conversationId, sequence, tokenCount: 10))
            .ToList();
        var existingWm = WorkingMemory.Create(conversationId, 1, "prior memory", 5, DateTimeOffset.UtcNow);

        _conversationRepository.Setup(r => r.FindByIdAsync(conversationId, It.IsAny<CancellationToken>())).ReturnsAsync(conversation);
        SetupMessages(conversationId, messages);
        _workingMemoryRepository.Setup(r => r.GetLatestAsync(conversationId, It.IsAny<CancellationToken>())).ReturnsAsync(existingWm);
        UpstreamRequest? compressionRequest = null;
        _chatCompletionClient
            .Setup(c => c.CompleteAsync(It.IsAny<ProviderEndpoint>(), It.IsAny<UpstreamRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ProviderEndpoint, UpstreamRequest, CancellationToken>((_, request, _) => compressionRequest = request)
            .ReturnsAsync(new UpstreamChatResult("# Working Memory\n## Current Goal\nDone", "stop", 50, 20));

        var orchestrator = CreateOrchestrator();
        _tokenEstimator.Setup(t => t.CountTokens(existingWm.Content)).Returns(5);

        var result = await orchestrator.RunAsync(conversationId, CompressionMode.Background, CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotNull(compressionRequest);
        var userPrompt = compressionRequest!.Messages.Last().Content;
        Assert.Contains("## Existing Working Memory", userPrompt);
        Assert.Contains("prior memory", userPrompt);
        Assert.DoesNotContain("Full Conversation Transcript", userPrompt);
        // Fold oldest two (retain 2); 5 + 10 + 10 = 25 fits cap.
        Assert.True(messages[0].IsFolded);
        Assert.True(messages[1].IsFolded);
        Assert.False(messages[2].IsFolded);
        Assert.False(messages[3].IsFolded);
        Assert.Equal(25, result!.OriginalTokens);
    }

    [Fact]
    public async Task RunAsync_WhenMergeFoldExceedsCap_ShrinksOldestFirst()
    {
        _policy = new ContextPolicyOptions
        {
            CompressionRetainMessageCount = 1,
            EmergencyRecentMessageCount = 1,
            CompressionMaxInputTokens = 15
        };

        var conversationId = Guid.NewGuid();
        var conversation = Conversation.Create("key", DateTimeOffset.UtcNow);
        // raw = 40 > 15 → merge; fold candidates = 3 msgs * 10 = 30, WM 0 → shrink to 1 (oldest)
        var messages = Enumerable.Range(0, 4)
            .Select(sequence => Message(conversationId, sequence, tokenCount: 10))
            .ToList();

        _conversationRepository.Setup(r => r.FindByIdAsync(conversationId, It.IsAny<CancellationToken>())).ReturnsAsync(conversation);
        SetupMessages(conversationId, messages);
        _workingMemoryRepository.Setup(r => r.GetLatestAsync(conversationId, It.IsAny<CancellationToken>())).ReturnsAsync((WorkingMemory?)null);
        _chatCompletionClient
            .Setup(c => c.CompleteAsync(It.IsAny<ProviderEndpoint>(), It.IsAny<UpstreamRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpstreamChatResult("# Working Memory\n## Current Goal\nDone", "stop", 50, 20));

        var orchestrator = CreateOrchestrator();

        var result = await orchestrator.RunAsync(conversationId, CompressionMode.Background, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(10, result!.OriginalTokens);
        Assert.Equal(1, result.FoldedMessageCount);
        Assert.True(messages[0].IsFolded);
        Assert.False(messages[1].IsFolded);
        Assert.False(messages[2].IsFolded);
        Assert.False(messages[3].IsFolded);
    }

    [Fact]
    public async Task RunAsync_Emergency_NeverUsesFullRawEvenWhenUnderCap()
    {
        var conversationId = Guid.NewGuid();
        var conversation = Conversation.Create("key", DateTimeOffset.UtcNow);
        var messages = Enumerable.Range(0, 4)
            .Select(sequence => Message(conversationId, sequence))
            .ToList();

        _conversationRepository.Setup(r => r.FindByIdAsync(conversationId, It.IsAny<CancellationToken>())).ReturnsAsync(conversation);
        SetupMessages(conversationId, messages);
        _workingMemoryRepository.Setup(r => r.GetLatestAsync(conversationId, It.IsAny<CancellationToken>())).ReturnsAsync((WorkingMemory?)null);
        UpstreamRequest? compressionRequest = null;
        _chatCompletionClient
            .Setup(c => c.CompleteAsync(It.IsAny<ProviderEndpoint>(), It.IsAny<UpstreamRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ProviderEndpoint, UpstreamRequest, CancellationToken>((_, request, _) => compressionRequest = request)
            .ReturnsAsync(new UpstreamChatResult("# Working Memory\n## Current Goal\nDone", "stop", 50, 20));

        var orchestrator = CreateOrchestrator();

        var result = await orchestrator.RunAsync(conversationId, CompressionMode.Emergency, CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotNull(compressionRequest);
        Assert.Contains("## Existing Working Memory", compressionRequest!.Messages.Last().Content);
        Assert.DoesNotContain("Full Conversation Transcript", compressionRequest.Messages.Last().Content);
        // Emergency retain = 1 → fold 3
        Assert.True(messages[0].IsFolded);
        Assert.True(messages[1].IsFolded);
        Assert.True(messages[2].IsFolded);
        Assert.False(messages[3].IsFolded);
    }

    [Fact]
    public async Task RunAsync_WhenSuccessful_CreatesNewWorkingMemoryVersionAndFoldsOlderMessages()
    {
        var conversationId = Guid.NewGuid();
        var conversation = Conversation.Create("key", DateTimeOffset.UtcNow);
        var messages = new List<ConversationMessage>
        {
            Message(conversationId, 0),
            Message(conversationId, 1),
            Message(conversationId, 2),
            Message(conversationId, 3)
        };

        _conversationRepository.Setup(r => r.FindByIdAsync(conversationId, It.IsAny<CancellationToken>())).ReturnsAsync(conversation);
        SetupMessages(conversationId, messages);
        _workingMemoryRepository.Setup(r => r.GetLatestAsync(conversationId, It.IsAny<CancellationToken>())).ReturnsAsync((WorkingMemory?)null);
        UpstreamRequest? compressionRequest = null;
        _chatCompletionClient
            .Setup(c => c.CompleteAsync(It.IsAny<ProviderEndpoint>(), It.IsAny<UpstreamRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ProviderEndpoint, UpstreamRequest, CancellationToken>((_, request, _) => compressionRequest = request)
            .ReturnsAsync(new UpstreamChatResult("# Working Memory\n## Current Goal\nDone", "stop", 50, 20));

        WorkingMemory? addedWorkingMemory = null;
        _workingMemoryRepository.Setup(r => r.Add(It.IsAny<WorkingMemory>())).Callback<WorkingMemory>(wm => addedWorkingMemory = wm);

        var orchestrator = CreateOrchestrator();

        var result = await orchestrator.RunAsync(conversationId, CompressionMode.Background, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(CompressionStatus.Succeeded, result!.Status);
        Assert.NotNull(addedWorkingMemory);
        Assert.Equal(1, addedWorkingMemory!.Version);
        Assert.NotNull(compressionRequest);
        Assert.Equal(UpstreamRequestPurpose.Compression, compressionRequest!.Purpose);
        Assert.Contains("Full Conversation Transcript", compressionRequest.Messages.Last().Content);

        Assert.True(messages[0].IsFolded);
        Assert.True(messages[1].IsFolded);
        Assert.False(messages[2].IsFolded);
        Assert.False(messages[3].IsFolded);

        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunAsync_WhenChatClientThrows_RecordsFailureAndLeavesMessagesUnfolded()
    {
        var conversationId = Guid.NewGuid();
        var conversation = Conversation.Create("key", DateTimeOffset.UtcNow);
        var messages = new List<ConversationMessage>
        {
            Message(conversationId, 0),
            Message(conversationId, 1),
            Message(conversationId, 2)
        };

        _conversationRepository.Setup(r => r.FindByIdAsync(conversationId, It.IsAny<CancellationToken>())).ReturnsAsync(conversation);
        SetupMessages(conversationId, messages);
        _workingMemoryRepository.Setup(r => r.GetLatestAsync(conversationId, It.IsAny<CancellationToken>())).ReturnsAsync((WorkingMemory?)null);
        _chatCompletionClient
            .Setup(c => c.CompleteAsync(It.IsAny<ProviderEndpoint>(), It.IsAny<UpstreamRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("upstream down"));

        var orchestrator = CreateOrchestrator();

        var result = await orchestrator.RunAsync(conversationId, CompressionMode.Background, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(CompressionStatus.Failed, result!.Status);
        Assert.Contains("upstream down", result.ErrorMessage);
        Assert.All(messages, m => Assert.False(m.IsFolded));
        _workingMemoryRepository.Verify(r => r.Add(It.IsAny<WorkingMemory>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_WhenWorkingMemoryFailsSanityCheck_RecordsFailureWithoutSaving()
    {
        var conversationId = Guid.NewGuid();
        var conversation = Conversation.Create("key", DateTimeOffset.UtcNow);
        var messages = new List<ConversationMessage>
        {
            Message(conversationId, 0),
            Message(conversationId, 1),
            Message(conversationId, 2)
        };

        _conversationRepository.Setup(r => r.FindByIdAsync(conversationId, It.IsAny<CancellationToken>())).ReturnsAsync(conversation);
        SetupMessages(conversationId, messages);
        _workingMemoryRepository.Setup(r => r.GetLatestAsync(conversationId, It.IsAny<CancellationToken>())).ReturnsAsync((WorkingMemory?)null);
        _chatCompletionClient
            .Setup(c => c.CompleteAsync(It.IsAny<ProviderEndpoint>(), It.IsAny<UpstreamRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpstreamChatResult("Sorry, I cannot compress this.", "stop", 50, 20));

        var orchestrator = CreateOrchestrator();

        var result = await orchestrator.RunAsync(conversationId, CompressionMode.Background, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(CompressionStatus.Failed, result!.Status);
        Assert.Contains("invalid working memory", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("missing_working_memory_heading", result.ErrorMessage);
        Assert.All(messages, m => Assert.False(m.IsFolded));
        _workingMemoryRepository.Verify(r => r.Add(It.IsAny<WorkingMemory>()), Times.Never);
        _unitOfWork.Verify(u => u.SaveChangesAsync(CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task RunAsync_WhenCancelledDuringCompression_RecordsCancelledFailureWithoutWorkingMemory()
    {
        var conversationId = Guid.NewGuid();
        var conversation = Conversation.Create("key", DateTimeOffset.UtcNow);
        var messages = new List<ConversationMessage>
        {
            Message(conversationId, 0),
            Message(conversationId, 1),
            Message(conversationId, 2)
        };

        using var cts = new CancellationTokenSource();
        _conversationRepository.Setup(r => r.FindByIdAsync(conversationId, It.IsAny<CancellationToken>())).ReturnsAsync(conversation);
        SetupMessages(conversationId, messages);
        _workingMemoryRepository.Setup(r => r.GetLatestAsync(conversationId, It.IsAny<CancellationToken>())).ReturnsAsync((WorkingMemory?)null);
        _chatCompletionClient
            .Setup(c => c.CompleteAsync(It.IsAny<ProviderEndpoint>(), It.IsAny<UpstreamRequest>(), It.IsAny<CancellationToken>()))
            .Returns(async (ProviderEndpoint _, UpstreamRequest _, CancellationToken token) =>
            {
                cts.Cancel();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return new UpstreamChatResult("should not commit", "stop", 1, 1);
            });

        var orchestrator = CreateOrchestrator();

        var result = await orchestrator.RunAsync(conversationId, CompressionMode.Background, cts.Token);

        Assert.NotNull(result);
        Assert.Equal(CompressionStatus.Failed, result!.Status);
        Assert.Equal("cancelled", result.ErrorMessage);
        Assert.All(messages, m => Assert.False(m.IsFolded));
        _workingMemoryRepository.Verify(r => r.Add(It.IsAny<WorkingMemory>()), Times.Never);
        _unitOfWork.Verify(u => u.SaveChangesAsync(CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task RunAsync_SmartFullRaw_UsesJsonRetainAndFoldsComplement()
    {
        _policy = new ContextPolicyOptions
        {
            CompressionRetainMessageCount = 2,
            EmergencyRecentMessageCount = 1,
            CompressionMaxInputTokens = 52_000,
            RetainSelection = RetainSelectionMode.Smart,
            SmartRetainMaxMessages = 8,
            SmartRetainMaxTokens = 24_000
        };

        var conversationId = Guid.NewGuid();
        var conversation = Conversation.Create("key", DateTimeOffset.UtcNow);
        var messages = Enumerable.Range(0, 5)
            .Select(sequence => Message(conversationId, sequence))
            .ToList();

        _conversationRepository.Setup(r => r.FindByIdAsync(conversationId, It.IsAny<CancellationToken>())).ReturnsAsync(conversation);
        SetupMessages(conversationId, messages);
        _workingMemoryRepository.Setup(r => r.GetLatestAsync(conversationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkingMemory?)null);
        UpstreamRequest? compressionRequest = null;
        _chatCompletionClient
            .Setup(c => c.CompleteAsync(It.IsAny<ProviderEndpoint>(), It.IsAny<UpstreamRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ProviderEndpoint, UpstreamRequest, CancellationToken>((_, request, _) => compressionRequest = request)
            .ReturnsAsync(new UpstreamChatResult(
                """
                # Working Memory
                ## Current Goal
                Done

                ## Retain Sequences
                1, 4
                """,
                "stop",
                50,
                20));

        var orchestrator = CreateOrchestrator();

        var result = await orchestrator.RunAsync(conversationId, CompressionMode.Background, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(CompressionStatus.Succeeded, result!.Status);
        var last = compressionRequest!.Messages.Last();
        Assert.Equal(MessageRole.User, last.Role);
        Assert.Contains("## Retain Index", last.Content);
        Assert.Contains("seq=0", last.Content);
        Assert.Contains("seq=4", last.Content);
        Assert.Contains("Retain Sequences", last.Content);
        Assert.DoesNotContain("sequence=0", last.Content);
        // Live prefix: system (+ optional WM) then raw messages, then instruction.
        Assert.True(compressionRequest.Messages.Count >= 3);
        Assert.Equal(MessageRole.System, compressionRequest.Messages[0].Role);
        Assert.True(messages[0].IsFolded);
        Assert.False(messages[1].IsFolded);
        Assert.True(messages[2].IsFolded);
        Assert.True(messages[3].IsFolded);
        Assert.False(messages[4].IsFolded); // tip forced
    }

    [Fact]
    public async Task RunAsync_SmartMerge_UsesUnfoldedCandidates()
    {
        _policy = new ContextPolicyOptions
        {
            CompressionRetainMessageCount = 2,
            EmergencyRecentMessageCount = 1,
            CompressionMaxInputTokens = 25,
            RetainSelection = RetainSelectionMode.Smart,
            SmartRetainMaxMessages = 8,
            SmartRetainMaxTokens = 24_000
        };

        var conversationId = Guid.NewGuid();
        var conversation = Conversation.Create("key", DateTimeOffset.UtcNow);
        var messages = Enumerable.Range(0, 4)
            .Select(sequence => Message(conversationId, sequence, tokenCount: 10))
            .ToList();
        var existingWm = WorkingMemory.Create(conversationId, 1, "prior memory", 5, DateTimeOffset.UtcNow);

        _conversationRepository.Setup(r => r.FindByIdAsync(conversationId, It.IsAny<CancellationToken>())).ReturnsAsync(conversation);
        SetupMessages(conversationId, messages);
        _workingMemoryRepository.Setup(r => r.GetLatestAsync(conversationId, It.IsAny<CancellationToken>())).ReturnsAsync(existingWm);
        UpstreamRequest? compressionRequest = null;
        _chatCompletionClient
            .Setup(c => c.CompleteAsync(It.IsAny<ProviderEndpoint>(), It.IsAny<UpstreamRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ProviderEndpoint, UpstreamRequest, CancellationToken>((_, request, _) => compressionRequest = request)
            .ReturnsAsync(new UpstreamChatResult(
                """
                # Working Memory
                ## Current Goal
                Done

                ## Retain Sequences
                0, 3
                """,
                "stop",
                50,
                20));

        var orchestrator = CreateOrchestrator();
        _tokenEstimator.Setup(t => t.CountTokens(existingWm.Content)).Returns(5);

        var result = await orchestrator.RunAsync(conversationId, CompressionMode.Background, CancellationToken.None);

        Assert.NotNull(result);
        var last = compressionRequest!.Messages.Last();
        Assert.Contains("## Retain Index", last.Content);
        Assert.Contains("seq=", last.Content);
        Assert.DoesNotContain("## Unfolded Conversation Messages", last.Content);
        Assert.DoesNotContain("sequence=", last.Content);
        Assert.False(messages[0].IsFolded);
        Assert.True(messages[1].IsFolded);
        Assert.True(messages[2].IsFolded);
        Assert.False(messages[3].IsFolded);
    }

    [Fact]
    public async Task RunAsync_SmartBadJsonWithRecoverableWm_UsesFixedFallback()
    {
        _policy = new ContextPolicyOptions
        {
            CompressionRetainMessageCount = 2,
            EmergencyRecentMessageCount = 1,
            CompressionMaxInputTokens = 52_000,
            RetainSelection = RetainSelectionMode.Smart,
            SmartRetainMaxMessages = 1,
            SmartRetainMaxTokens = 1
        };

        var conversationId = Guid.NewGuid();
        var conversation = Conversation.Create("key", DateTimeOffset.UtcNow);
        var messages = Enumerable.Range(0, 5)
            .Select(sequence => Message(conversationId, sequence))
            .ToList();

        _conversationRepository.Setup(r => r.FindByIdAsync(conversationId, It.IsAny<CancellationToken>())).ReturnsAsync(conversation);
        SetupMessages(conversationId, messages);
        _workingMemoryRepository.Setup(r => r.GetLatestAsync(conversationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkingMemory?)null);
        _chatCompletionClient
            .Setup(c => c.CompleteAsync(It.IsAny<ProviderEndpoint>(), It.IsAny<UpstreamRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpstreamChatResult("# Working Memory\n## Current Goal\nRecovered", "stop", 50, 20));

        WorkingMemory? added = null;
        _workingMemoryRepository.Setup(r => r.Add(It.IsAny<WorkingMemory>())).Callback<WorkingMemory>(wm => added = wm);

        var orchestrator = CreateOrchestrator();

        var result = await orchestrator.RunAsync(conversationId, CompressionMode.Background, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(CompressionStatus.Succeeded, result!.Status);
        Assert.Contains("Recovered", added!.Content);
        // Fixed retain tip = last 2
        Assert.True(messages[0].IsFolded);
        Assert.True(messages[1].IsFolded);
        Assert.True(messages[2].IsFolded);
        Assert.False(messages[3].IsFolded);
        Assert.False(messages[4].IsFolded);
    }

    [Fact]
    public async Task RunAsync_Emergency_IgnoresSmartRetainSelection()
    {
        _policy = new ContextPolicyOptions
        {
            CompressionRetainMessageCount = 2,
            EmergencyRecentMessageCount = 1,
            CompressionMaxInputTokens = 52_000,
            RetainSelection = RetainSelectionMode.Smart,
            SmartRetainMaxMessages = 8,
            SmartRetainMaxTokens = 24_000
        };

        var conversationId = Guid.NewGuid();
        var conversation = Conversation.Create("key", DateTimeOffset.UtcNow);
        var messages = Enumerable.Range(0, 4)
            .Select(sequence => Message(conversationId, sequence))
            .ToList();

        _conversationRepository.Setup(r => r.FindByIdAsync(conversationId, It.IsAny<CancellationToken>())).ReturnsAsync(conversation);
        SetupMessages(conversationId, messages);
        _workingMemoryRepository.Setup(r => r.GetLatestAsync(conversationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkingMemory?)null);
        UpstreamRequest? compressionRequest = null;
        _chatCompletionClient
            .Setup(c => c.CompleteAsync(It.IsAny<ProviderEndpoint>(), It.IsAny<UpstreamRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ProviderEndpoint, UpstreamRequest, CancellationToken>((_, request, _) => compressionRequest = request)
            .ReturnsAsync(new UpstreamChatResult(
                """
                # Working Memory
                ## Current Goal
                Done

                ## Retain Sequences
                0
                """,
                "stop",
                50,
                20));

        WorkingMemory? added = null;
        _workingMemoryRepository.Setup(r => r.Add(It.IsAny<WorkingMemory>())).Callback<WorkingMemory>(wm => added = wm);

        var orchestrator = CreateOrchestrator();

        var result = await orchestrator.RunAsync(conversationId, CompressionMode.Emergency, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(CompressionStatus.Succeeded, result!.Status);
        var userPrompt = compressionRequest!.Messages.Last().Content;
        Assert.DoesNotContain("sequence=", userPrompt);
        Assert.DoesNotContain("Full Conversation Transcript", userPrompt);
        // Emergency Fixed retain=1; Smart markdown is stored as opaque WM text (not parsed).
        Assert.True(messages[0].IsFolded);
        Assert.True(messages[1].IsFolded);
        Assert.True(messages[2].IsFolded);
        Assert.False(messages[3].IsFolded);
        Assert.Contains("Retain Sequences", added!.Content);
    }

    [Fact]
    public async Task RunAsync_Fixed_DedupesDuplicateFileReadsInRetainTip()
    {
        _policy = new ContextPolicyOptions
        {
            CompressionRetainMessageCount = 6,
            EmergencyRecentMessageCount = 1,
            CompressionMaxInputTokens = 52_000,
            MaxRecentRawTokens = 24_000,
            RetainSelection = RetainSelectionMode.Fixed
        };

        var conversationId = Guid.NewGuid();
        var conversation = Conversation.Create("key", DateTimeOffset.UtcNow);
        const string path = "/proj/spec.md";

        ConversationMessage AssistantCall(int sequence, string callId) =>
            ConversationMessage.Create(
                conversationId,
                sequence,
                MessageRole.Assistant,
                string.Empty,
                5,
                DateTimeOffset.UtcNow,
                $"{{\"role\":\"assistant\",\"tool_calls\":[{{\"id\":\"{callId}\",\"type\":\"function\",\"function\":{{\"name\":\"Read\",\"arguments\":\"{{\\\"filePath\\\":\\\"{path}\\\"}}\"}}}}]}}");

        ConversationMessage ToolResult(int sequence, string callId) =>
            ConversationMessage.Create(
                conversationId,
                sequence,
                MessageRole.Tool,
                $"<path>{path}</path>",
                10,
                DateTimeOffset.UtcNow,
                $"{{\"role\":\"tool\",\"tool_call_id\":\"{callId}\",\"content\":\"<path>{path}</path>\"}}");

        var messages = new List<ConversationMessage>
        {
            Message(conversationId, 0),
            AssistantCall(1, "c1"),
            ToolResult(2, "c1"),
            AssistantCall(3, "c2"),
            ToolResult(4, "c2"),
            AssistantCall(5, "c3"),
            ToolResult(6, "c3"),
            Message(conversationId, 7)
        };

        _conversationRepository.Setup(r => r.FindByIdAsync(conversationId, It.IsAny<CancellationToken>())).ReturnsAsync(conversation);
        SetupMessages(conversationId, messages);
        _workingMemoryRepository.Setup(r => r.GetLatestAsync(conversationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkingMemory?)null);
        UpstreamRequest? compressionRequest = null;
        _chatCompletionClient
            .Setup(c => c.CompleteAsync(It.IsAny<ProviderEndpoint>(), It.IsAny<UpstreamRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ProviderEndpoint, UpstreamRequest, CancellationToken>((_, request, _) => compressionRequest = request)
            .ReturnsAsync(new UpstreamChatResult("# Working Memory\n## Current Goal\nDone", "stop", 50, 20));

        var orchestrator = CreateOrchestrator();

        var result = await orchestrator.RunAsync(conversationId, CompressionMode.Background, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(CompressionStatus.Succeeded, result!.Status);
        // Older duplicate reads (and their single-call assistants) fold via pre-LLM tip shrink;
        // newest read pair + tip stay.
        Assert.True(messages[0].IsFolded);
        Assert.True(messages[1].IsFolded);
        Assert.True(messages[2].IsFolded);
        Assert.True(messages[3].IsFolded);
        Assert.True(messages[4].IsFolded);
        Assert.False(messages[5].IsFolded);
        Assert.False(messages[6].IsFolded);
        Assert.False(messages[7].IsFolded);
        var userPrompt = compressionRequest!.Messages.Last().Content;
        Assert.DoesNotContain("tool_call_id\":\"c1\"", userPrompt);
        Assert.DoesNotContain("tool_call_id\":\"c2\"", userPrompt);
        Assert.Contains("tool_call_id\":\"c3\"", userPrompt);
    }

    [Fact]
    public async Task RunAsync_Fixed_WhenDedupeDisabled_KeepsDuplicateFileReads()
    {
        _policy = new ContextPolicyOptions
        {
            CompressionRetainMessageCount = 6,
            EmergencyRecentMessageCount = 1,
            CompressionMaxInputTokens = 52_000,
            MaxRecentRawTokens = 24_000,
            RetainSelection = RetainSelectionMode.Fixed,
            DedupeDuplicateFileReads = false
        };

        var conversationId = Guid.NewGuid();
        var conversation = Conversation.Create("key", DateTimeOffset.UtcNow);
        const string path = "/proj/spec.md";

        ConversationMessage AssistantCall(int sequence, string callId) =>
            ConversationMessage.Create(
                conversationId,
                sequence,
                MessageRole.Assistant,
                string.Empty,
                5,
                DateTimeOffset.UtcNow,
                $"{{\"role\":\"assistant\",\"tool_calls\":[{{\"id\":\"{callId}\",\"type\":\"function\",\"function\":{{\"name\":\"Read\",\"arguments\":\"{{\\\"filePath\\\":\\\"{path}\\\"}}\"}}}}]}}");

        ConversationMessage ToolResult(int sequence, string callId) =>
            ConversationMessage.Create(
                conversationId,
                sequence,
                MessageRole.Tool,
                $"<path>{path}</path>",
                10,
                DateTimeOffset.UtcNow,
                $"{{\"role\":\"tool\",\"tool_call_id\":\"{callId}\",\"content\":\"<path>{path}</path>\"}}");

        // Same shape as the enabled-dedupe test: tip holds three read pairs + user tip.
        var messages = new List<ConversationMessage>
        {
            Message(conversationId, 0),
            AssistantCall(1, "c1"),
            ToolResult(2, "c1"),
            AssistantCall(3, "c2"),
            ToolResult(4, "c2"),
            AssistantCall(5, "c3"),
            ToolResult(6, "c3"),
            Message(conversationId, 7)
        };

        _conversationRepository.Setup(r => r.FindByIdAsync(conversationId, It.IsAny<CancellationToken>())).ReturnsAsync(conversation);
        SetupMessages(conversationId, messages);
        _workingMemoryRepository.Setup(r => r.GetLatestAsync(conversationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkingMemory?)null);
        _chatCompletionClient
            .Setup(c => c.CompleteAsync(It.IsAny<ProviderEndpoint>(), It.IsAny<UpstreamRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpstreamChatResult("# Working Memory\n## Current Goal\nDone", "stop", 50, 20));

        var orchestrator = CreateOrchestrator();

        var result = await orchestrator.RunAsync(conversationId, CompressionMode.Background, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(CompressionStatus.Succeeded, result!.Status);
        // Fixed tip is [3..7]; without dedupe the older in-tip duplicate pair (3,4) stays unfolded.
        Assert.True(messages[0].IsFolded);
        Assert.True(messages[1].IsFolded);
        Assert.True(messages[2].IsFolded);
        Assert.False(messages[3].IsFolded);
        Assert.False(messages[4].IsFolded);
        Assert.False(messages[5].IsFolded);
        Assert.False(messages[6].IsFolded);
        Assert.False(messages[7].IsFolded);
    }

    [Fact]
    public async Task RunAsync_Emergency_SkipsDuplicateFileReadDedupe()
    {
        _policy = new ContextPolicyOptions
        {
            CompressionRetainMessageCount = 4,
            EmergencyRecentMessageCount = 5,
            CompressionMaxInputTokens = 52_000,
            RetainSelection = RetainSelectionMode.Fixed
        };

        var conversationId = Guid.NewGuid();
        var conversation = Conversation.Create("key", DateTimeOffset.UtcNow);
        const string path = "/proj/spec.md";

        ConversationMessage AssistantCall(int sequence, string callId) =>
            ConversationMessage.Create(
                conversationId,
                sequence,
                MessageRole.Assistant,
                string.Empty,
                5,
                DateTimeOffset.UtcNow,
                $"{{\"role\":\"assistant\",\"tool_calls\":[{{\"id\":\"{callId}\",\"type\":\"function\",\"function\":{{\"name\":\"Read\",\"arguments\":\"{{\\\"filePath\\\":\\\"{path}\\\"}}\"}}}}]}}");

        ConversationMessage ToolResult(int sequence, string callId) =>
            ConversationMessage.Create(
                conversationId,
                sequence,
                MessageRole.Tool,
                $"<path>{path}</path>",
                10,
                DateTimeOffset.UtcNow,
                $"{{\"role\":\"tool\",\"tool_call_id\":\"{callId}\",\"content\":\"<path>{path}</path>\"}}");

        var messages = new List<ConversationMessage>
        {
            Message(conversationId, 0),
            Message(conversationId, 1),
            AssistantCall(2, "c1"),
            ToolResult(3, "c1"),
            AssistantCall(4, "c2"),
            ToolResult(5, "c2")
        };

        _conversationRepository.Setup(r => r.FindByIdAsync(conversationId, It.IsAny<CancellationToken>())).ReturnsAsync(conversation);
        SetupMessages(conversationId, messages);
        _workingMemoryRepository.Setup(r => r.GetLatestAsync(conversationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkingMemory?)null);
        _chatCompletionClient
            .Setup(c => c.CompleteAsync(It.IsAny<ProviderEndpoint>(), It.IsAny<UpstreamRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpstreamChatResult("# Working Memory\n## Current Goal\nDone", "stop", 50, 20));

        var orchestrator = CreateOrchestrator();

        var result = await orchestrator.RunAsync(conversationId, CompressionMode.Emergency, CancellationToken.None);

        Assert.NotNull(result);
        // Emergency retain=5 keeps 1..5; message 0 folds; duplicate reads stay (dedupe skipped).
        Assert.True(messages[0].IsFolded);
        Assert.False(messages[1].IsFolded);
        Assert.False(messages[2].IsFolded);
        Assert.False(messages[3].IsFolded);
        Assert.False(messages[4].IsFolded);
        Assert.False(messages[5].IsFolded);
    }
}
