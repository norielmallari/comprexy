using System.Security.Cryptography;
using System.Text;
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

public class ToolSchemaOrchestratorTests
{
    private readonly Mock<IConversationToolCatalogRepository> _catalogRepository = new();
    private readonly Mock<IConversationToolDefinitionRepository> _definitionRepository = new();
    private readonly Mock<IChatCompletionClient> _chatCompletionClient = new();
    private readonly Mock<ITokenEstimator> _tokenEstimator = new();
    private readonly Mock<IClock> _clock = new();

    private ToolSchemaOptions _options = new()
    {
        Mode = ToolSchemaMode.CompactIndex,
        MinToolCountToActivate = 1,
        SkipRefetchIfHydrated = true
    };

    private ToolSchemaOrchestrator CreateOrchestrator() =>
        new(
            Options.Create(_options),
            new ToolCatalogParser(),
            new ToolSchemaPromptFactory("tool schema rules"),
            new ToolArgumentValidator(),
            _catalogRepository.Object,
            _definitionRepository.Object,
            _chatCompletionClient.Object,
            _tokenEstimator.Object,
            _clock.Object,
            NullLogger<ToolSchemaOrchestrator>.Instance);

    private static JsonElement ParseRequest(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static JsonElement ToolsRequest(params string[] toolNames)
    {
        var tools = toolNames.Select(name => new
        {
            type = "function",
            function = new
            {
                name,
                description = $"Tool {name}.",
                parameters = new { type = "object", required = Array.Empty<string>() }
            }
        }).ToArray();

        return ParseRequest(JsonSerializer.Serialize(new { tools }));
    }

    [Fact]
    public async Task TryPrepareRewriteAsync_ModeOff_ReturnsNull()
    {
        _options.Mode = ToolSchemaMode.Off;
        var orchestrator = CreateOrchestrator();

        var result = await orchestrator.TryPrepareRewriteAsync(
            Guid.NewGuid(),
            [new ChatMessage(MessageRole.User, "hello")],
            ToolsRequest("lookup"),
            [],
            CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task TryPrepareRewriteAsync_BelowMinToolCount_ReturnsNull()
    {
        _options.MinToolCountToActivate = 3;
        var orchestrator = CreateOrchestrator();

        var result = await orchestrator.TryPrepareRewriteAsync(
            Guid.NewGuid(),
            [new ChatMessage(MessageRole.User, "hello")],
            ToolsRequest("one", "two"),
            [],
            CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task TryPrepareRewriteAsync_MetaToolCollision_ReturnsNull()
    {
        var orchestrator = CreateOrchestrator();

        var result = await orchestrator.TryPrepareRewriteAsync(
            Guid.NewGuid(),
            [new ChatMessage(MessageRole.User, "hello")],
            ToolsRequest(ToolSchemaConstants.MetaToolName, "lookup"),
            [],
            CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task TryPrepareRewriteAsync_ActiveMode_RewritesToolsToMetaToolOnly()
    {
        _clock.Setup(c => c.UtcNow).Returns(DateTimeOffset.UtcNow);
        _catalogRepository
            .Setup(r => r.GetByConversationIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ConversationToolCatalog?)null);
        _definitionRepository
            .Setup(r => r.GetByConversationIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var orchestrator = CreateOrchestrator();
        var result = await orchestrator.TryPrepareRewriteAsync(
            Guid.NewGuid(),
            [new ChatMessage(MessageRole.User, "hello")],
            ToolsRequest("lookup", "search"),
            [],
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result!.RewrittenClientRequest.TryGetProperty("tools", out var tools));
        Assert.Equal(1, tools.GetArrayLength());
        Assert.Equal(
            ToolSchemaConstants.MetaToolName,
            tools[0].GetProperty("function").GetProperty("name").GetString());
        Assert.Contains(result.OutgoingMessages, m => m.Role == MessageRole.System && m.Content.Contains("tool schema rules"));
        _catalogRepository.Verify(r => r.Add(It.IsAny<ConversationToolCatalog>()), Times.Once);
    }

    [Fact]
    public async Task RunInternalLoopAsync_MetaHydrate_ReturnsDefinitionPayload()
    {
        var conversationId = Guid.NewGuid();
        const string definitionJson = """
            {"type":"function","function":{"name":"lookup","parameters":{"type":"object","required":["query"]}}}
            """;
        var definitionHash = ComputeSha256Hex(definitionJson);

        _definitionRepository
            .Setup(r => r.FindAsync(conversationId, "lookup", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ConversationToolDefinition?)null);

        var session = new ToolSchemaSession
        {
            ConversationId = conversationId,
            CatalogToolNames = new HashSet<string>(StringComparer.Ordinal) { "lookup" },
            FullDefinitionsByName = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["lookup"] = definitionJson
            }
        };

        const string assistantJson = """
            {"role":"assistant","content":"","tool_calls":[{"id":"call_meta","type":"function","function":{"name":"get_tool_definition","arguments":"{\"tool_name\":\"lookup\"}"}}]}
            """;
        var initialResult = new UpstreamChatResult(
            Content: string.Empty,
            FinishReason: "tool_calls",
            PromptTokens: 1,
            CompletionTokens: 1,
            AssistantMessageJson: assistantJson);

        _chatCompletionClient
            .Setup(c => c.CompleteAsync(It.IsAny<ProviderEndpoint>(), It.IsAny<UpstreamRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpstreamChatResult("done", "stop", 1, 1));

        var orchestrator = CreateOrchestrator();
        var loopResult = await orchestrator.RunInternalLoopAsync(
            session,
            new ProviderEndpoint("http://upstream", "key", "model", 60),
            new UpstreamRequest([new ChatMessage(MessageRole.User, "hello")], Stream: false),
            initialResult,
            CancellationToken.None);

        Assert.NotNull(loopResult);
        Assert.Single(session.PendingPersistedTurns);
        Assert.Contains("lookup", session.PendingPersistedTurns[0].ToolMessage.Content);
        Assert.Contains("definition", session.PendingPersistedTurns[0].ToolMessage.Content);
        Assert.Contains("lookup", session.HydratedToolNames);
        _definitionRepository.Verify(r => r.Add(It.IsAny<ConversationToolDefinition>()), Times.Once);
    }

    [Fact]
    public async Task RunInternalLoopAsync_SkipRefetchIfHydrated_ReturnsAlreadyHydratedAck()
    {
        var conversationId = Guid.NewGuid();
        const string definitionJson = """
            {"type":"function","function":{"name":"lookup","parameters":{"type":"object","required":["query"]}}}
            """;
        var definitionHash = ComputeSha256Hex(definitionJson);
        var hydrated = ConversationToolDefinition.Create(conversationId, "lookup", definitionHash, definitionJson, DateTimeOffset.UtcNow);

        _definitionRepository
            .Setup(r => r.FindAsync(conversationId, "lookup", It.IsAny<CancellationToken>()))
            .ReturnsAsync(hydrated);

        var session = new ToolSchemaSession
        {
            ConversationId = conversationId,
            CatalogToolNames = new HashSet<string>(StringComparer.Ordinal) { "lookup" },
            FullDefinitionsByName = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["lookup"] = definitionJson
            }
        };
        session.HydratedToolNames.Add("lookup");

        const string assistantJson = """
            {"role":"assistant","content":"","tool_calls":[{"id":"call_meta","type":"function","function":{"name":"get_tool_definition","arguments":"{\"tool_name\":\"lookup\"}"}}]}
            """;
        var initialResult = new UpstreamChatResult(
            Content: string.Empty,
            FinishReason: "tool_calls",
            PromptTokens: 1,
            CompletionTokens: 1,
            AssistantMessageJson: assistantJson);

        _chatCompletionClient
            .Setup(c => c.CompleteAsync(It.IsAny<ProviderEndpoint>(), It.IsAny<UpstreamRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpstreamChatResult("done", "stop", 1, 1));

        var orchestrator = CreateOrchestrator();
        await orchestrator.RunInternalLoopAsync(
            session,
            new ProviderEndpoint("http://upstream", "key", "model", 60),
            new UpstreamRequest([new ChatMessage(MessageRole.User, "hello")], Stream: false),
            initialResult,
            CancellationToken.None);

        Assert.Single(session.PendingPersistedTurns);
        Assert.Contains("already_hydrated", session.PendingPersistedTurns[0].ToolMessage.Content);
        _definitionRepository.Verify(r => r.Add(It.IsAny<ConversationToolDefinition>()), Times.Never);
    }

    [Fact]
    public async Task RunInternalLoopAsync_UnhydratedRealToolCall_ReturnsStructuredErrorJson()
    {
        var conversationId = Guid.NewGuid();
        const string definitionJson = """
            {"type":"function","function":{"name":"lookup","parameters":{"type":"object","required":["query"]}}}
            """;

        var session = new ToolSchemaSession
        {
            ConversationId = conversationId,
            CatalogToolNames = new HashSet<string>(StringComparer.Ordinal) { "lookup" },
            FullDefinitionsByName = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["lookup"] = definitionJson
            }
        };

        const string assistantJson = """
            {"role":"assistant","content":"","tool_calls":[{"id":"call_real","type":"function","function":{"name":"lookup","arguments":"{\"query\":\"x\"}"}}]}
            """;
        var initialResult = new UpstreamChatResult(
            Content: string.Empty,
            FinishReason: "tool_calls",
            PromptTokens: 1,
            CompletionTokens: 1,
            AssistantMessageJson: assistantJson);

        ChatMessage? errorToolMessage = null;
        _chatCompletionClient
            .Setup(c => c.CompleteAsync(It.IsAny<ProviderEndpoint>(), It.IsAny<UpstreamRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ProviderEndpoint, UpstreamRequest, CancellationToken>((_, request, _) =>
            {
                errorToolMessage = request.Messages.LastOrDefault(m => m.Role == MessageRole.Tool);
            })
            .ReturnsAsync(new UpstreamChatResult("done", "stop", 1, 1));

        var orchestrator = CreateOrchestrator();
        await orchestrator.RunInternalLoopAsync(
            session,
            new ProviderEndpoint("http://upstream", "key", "model", 60),
            new UpstreamRequest([new ChatMessage(MessageRole.User, "hello")], Stream: false),
            initialResult,
            CancellationToken.None);

        Assert.NotNull(errorToolMessage);
        using var errorDoc = JsonDocument.Parse(errorToolMessage!.Content);
        var root = errorDoc.RootElement;
        Assert.True(root.TryGetProperty("error", out _));
        Assert.Equal("not_hydrated", root.GetProperty("code").GetString());
        Assert.True(root.TryGetProperty("details", out var details));
        Assert.Contains("hydrated", details.GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateDownstreamToolResults_AcceptsOpenAnnouncedToolCallId()
    {
        var conversationId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var assistant = ConversationMessage.Create(
            conversationId,
            0,
            MessageRole.Assistant,
            string.Empty,
            1,
            now,
            """{"role":"assistant","tool_calls":[{"id":"call_da0beee1","type":"function","function":{"name":"lookup","arguments":"{}"}}]}""");

        using var toolDoc = JsonDocument.Parse(
            """{"role":"tool","tool_call_id":"call_da0beee1","content":"ok"}""");
        var toolMessage = new ChatMessage(MessageRole.Tool, "ok", toolDoc.RootElement.Clone());

        var orchestrator = CreateOrchestrator();
        orchestrator.ValidateDownstreamToolResults([toolMessage], [assistant]);
    }

    [Fact]
    public void ValidateDownstreamToolResults_RejectsWhenToolResultAlreadyInHistory()
    {
        var conversationId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var assistant = ConversationMessage.Create(
            conversationId,
            0,
            MessageRole.Assistant,
            string.Empty,
            1,
            now,
            """{"role":"assistant","tool_calls":[{"id":"call_1","type":"function","function":{"name":"lookup","arguments":"{}"}}]}""");
        var priorTool = ConversationMessage.Create(
            conversationId,
            1,
            MessageRole.Tool,
            "ok",
            1,
            now,
            """{"role":"tool","tool_call_id":"call_1","content":"ok"}""");

        using var toolDoc = JsonDocument.Parse(
            """{"role":"tool","tool_call_id":"call_1","content":"again"}""");
        var toolMessage = new ChatMessage(MessageRole.Tool, "again", toolDoc.RootElement.Clone());

        var orchestrator = CreateOrchestrator();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            orchestrator.ValidateDownstreamToolResults([toolMessage], [assistant, priorTool]));
        Assert.Contains("call_1", ex.Message, StringComparison.Ordinal);
    }

    private static string ComputeSha256Hex(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
