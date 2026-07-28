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
        Assert.Equal(2, tools.GetArrayLength());
        Assert.Equal(
            ToolSchemaConstants.MetaToolName,
            tools[0].GetProperty("function").GetProperty("name").GetString());
        Assert.Equal(
            ToolSchemaConstants.ConversationIdMetaToolName,
            tools[1].GetProperty("function").GetProperty("name").GetString());
        Assert.Contains(result.OutgoingMessages, m => m.Role == MessageRole.System && m.Content.Contains("tool schema rules"));
        _catalogRepository.Verify(r => r.Add(It.IsAny<ConversationToolCatalog>()), Times.Once);
    }

    [Fact]
    public async Task RunInternalLoopAsync_GetCurrentConversationId_ReturnsSessionId()
    {
        var conversationId = Guid.Parse("dcd03d1d-b473-41b2-ac74-b2e52121eeb4");
        var session = new ToolSchemaSession
        {
            ConversationId = conversationId,
            CatalogToolNames = new HashSet<string>(StringComparer.Ordinal) { "lookup" },
            FullDefinitionsByName = new Dictionary<string, string>(StringComparer.Ordinal)
        };

        const string assistantJson = """
            {"role":"assistant","content":"","tool_calls":[{"id":"call_cid","type":"function","function":{"name":"get_current_conversation_id","arguments":"{}"}}]}
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
        Assert.Contains("dcd03d1d-b473-41b2-ac74-b2e52121eeb4", session.PendingPersistedTurns[0].ToolMessage.Content);
        Assert.Contains("conversation_id", session.PendingPersistedTurns[0].ToolMessage.Content);
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
    public async Task RunInternalLoopAsync_SkipRefetchIfHydrated_ReturnsDefinitionWithAlreadyHydratedAck()
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
        var content = session.PendingPersistedTurns[0].ToolMessage.Content;
        Assert.Contains("already_hydrated", content);
        Assert.Contains("definition", content);
        Assert.Contains("instruction", content);
        Assert.Contains("function name is exactly", content);
        Assert.Contains("CallMcpTool.toolName", content);
        Assert.Contains("\"query\"", content);
        Assert.Contains("lookup", session.LoopExposedToolNames);
        _definitionRepository.Verify(r => r.Add(It.IsAny<ConversationToolDefinition>()), Times.Never);
    }

    [Fact]
    public async Task RunInternalLoopAsync_AfterHydrate_ExposesToolInNextRoundToolsArray()
    {
        var conversationId = Guid.NewGuid();
        const string definitionJson = """
            {"type":"function","function":{"name":"lookup","parameters":{"type":"object","required":["query"]}}}
            """;

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

        const string metaAssistantJson = """
            {"role":"assistant","content":"","tool_calls":[{"id":"call_meta","type":"function","function":{"name":"get_tool_definition","arguments":"{\"tool_name\":\"lookup\"}"}}]}
            """;
        var initialResult = new UpstreamChatResult(
            Content: string.Empty,
            FinishReason: "tool_calls",
            PromptTokens: 1,
            CompletionTokens: 1,
            AssistantMessageJson: metaAssistantJson);

        UpstreamRequest? followUp = null;
        _chatCompletionClient
            .Setup(c => c.CompleteAsync(It.IsAny<ProviderEndpoint>(), It.IsAny<UpstreamRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ProviderEndpoint, UpstreamRequest, CancellationToken>((_, request, _) => followUp = request)
            .ReturnsAsync(new UpstreamChatResult("done", "stop", 1, 1));

        var originalRequest = ParseRequest("""
            {"model":"m","messages":[{"role":"user","content":"hi"}],"tools":[{"type":"function","function":{"name":"lookup","parameters":{"type":"object"}}}]}
            """);
        var orchestrator = CreateOrchestrator();
        await orchestrator.RunInternalLoopAsync(
            session,
            new ProviderEndpoint("http://upstream", "key", "model", 60),
            new UpstreamRequest(
                [new ChatMessage(MessageRole.User, "hello")],
                Stream: false,
                OriginalClientRequest: originalRequest),
            initialResult,
            CancellationToken.None);

        Assert.NotNull(followUp);
        Assert.True(followUp!.RewrittenClientRequest.HasValue);
        using var doc = JsonDocument.Parse(followUp.RewrittenClientRequest!.Value.GetRawText());
        var tools = doc.RootElement.GetProperty("tools");
        Assert.Equal(JsonValueKind.Array, tools.ValueKind);
        var names = tools.EnumerateArray()
            .Select(t => t.GetProperty("function").GetProperty("name").GetString())
            .ToList();
        Assert.Equal(["get_tool_definition", "get_current_conversation_id", "lookup"], names);
        Assert.Contains("lookup", session.LoopExposedToolNames);
        Assert.Contains("instruction", session.PendingPersistedTurns[0].ToolMessage.Content);
        Assert.Contains("CallMcpTool.toolName", session.PendingPersistedTurns[0].ToolMessage.Content);
    }

    [Fact]
    public async Task RunInternalLoopAsync_RepeatedAlreadyHydratedOnly_StopsWithoutForwardingMetaTool()
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
        var metaResult = new UpstreamChatResult(
            Content: string.Empty,
            FinishReason: "tool_calls",
            PromptTokens: 1,
            CompletionTokens: 1,
            AssistantMessageJson: assistantJson);

        UpstreamRequest? secondRequest = null;
        _chatCompletionClient
            .Setup(c => c.CompleteAsync(It.IsAny<ProviderEndpoint>(), It.IsAny<UpstreamRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ProviderEndpoint, UpstreamRequest, CancellationToken>((_, request, _) => secondRequest = request)
            .ReturnsAsync(metaResult);

        var orchestrator = CreateOrchestrator();
        var loopResult = await orchestrator.RunInternalLoopAsync(
            session,
            new ProviderEndpoint("http://upstream", "key", "model", 60),
            new UpstreamRequest([new ChatMessage(MessageRole.User, "hello")], Stream: false),
            metaResult,
            CancellationToken.None);

        Assert.True(loopResult.RequiresInternalHandling);
        Assert.Equal("stop", loopResult.FinalUpstreamResult.FinishReason);
        Assert.Contains("already-hydrated", loopResult.FinalUpstreamResult.Content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"tool_calls\"", loopResult.FinalUpstreamResult.AssistantMessageJson ?? string.Empty);
        Assert.Empty(loopResult.AllowedRealToolCalls);
        Assert.NotNull(secondRequest);
        Assert.Contains(
            secondRequest!.Messages,
            m => m.Role == MessageRole.System &&
                 m.Content.Contains("already hydrated", StringComparison.OrdinalIgnoreCase) &&
                 m.Content.Contains("CallMcpTool.toolName", StringComparison.Ordinal) &&
                 m.Content.Contains("\"lookup\"", StringComparison.Ordinal));
        Assert.True(secondRequest.RewrittenClientRequest.HasValue);
        using (var doc = JsonDocument.Parse(secondRequest.RewrittenClientRequest!.Value.GetRawText()))
        {
            var names = doc.RootElement.GetProperty("tools").EnumerateArray()
                .Select(t => t.GetProperty("function").GetProperty("name").GetString())
                .ToList();
            Assert.Equal(["get_tool_definition", "get_current_conversation_id", "lookup"], names);
        }
        _chatCompletionClient.Verify(
            c => c.CompleteAsync(It.IsAny<ProviderEndpoint>(), It.IsAny<UpstreamRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RunInternalLoopAsync_MaxHydrateRounds_StopsWithoutForwardingMetaTool()
    {
        _options = new ToolSchemaOptions
        {
            Mode = ToolSchemaMode.CompactIndex,
            MinToolCountToActivate = 1,
            SkipRefetchIfHydrated = false,
            MaxHydrateRoundsPerRequest = 2
        };

        var conversationId = Guid.NewGuid();

        _definitionRepository
            .Setup(r => r.FindAsync(conversationId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ConversationToolDefinition?)null);

        var session = new ToolSchemaSession
        {
            ConversationId = conversationId,
            CatalogToolNames = new HashSet<string>(StringComparer.Ordinal) { "alpha", "beta", "gamma" },
            FullDefinitionsByName = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["alpha"] = """{"type":"function","function":{"name":"alpha","parameters":{"type":"object"}}}""",
                ["beta"] = """{"type":"function","function":{"name":"beta","parameters":{"type":"object"}}}""",
                ["gamma"] = """{"type":"function","function":{"name":"gamma","parameters":{"type":"object"}}}"""
            }
        };

        UpstreamChatResult Meta(string toolName, string callId) =>
            new(
                Content: string.Empty,
                FinishReason: "tool_calls",
                PromptTokens: 1,
                CompletionTokens: 1,
                AssistantMessageJson:
                "{\"role\":\"assistant\",\"content\":\"\",\"tool_calls\":[{\"id\":\"" + callId +
                "\",\"type\":\"function\",\"function\":{\"name\":\"get_tool_definition\",\"arguments\":\"{\\\"tool_name\\\":\\\"" +
                toolName + "\\\"}\"}}]}");

        var queue = new Queue<UpstreamChatResult>([
            Meta("beta", "call_2"),
            Meta("gamma", "call_3")
        ]);
        _chatCompletionClient
            .Setup(c => c.CompleteAsync(It.IsAny<ProviderEndpoint>(), It.IsAny<UpstreamRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => queue.Dequeue());

        var orchestrator = CreateOrchestrator();
        var loopResult = await orchestrator.RunInternalLoopAsync(
            session,
            new ProviderEndpoint("http://upstream", "key", "model", 60),
            new UpstreamRequest([new ChatMessage(MessageRole.User, "hello")], Stream: false),
            Meta("alpha", "call_1"),
            CancellationToken.None);

        Assert.True(loopResult.RequiresInternalHandling);
        Assert.Equal("stop", loopResult.FinalUpstreamResult.FinishReason);
        Assert.Contains("MaxHydrateRoundsPerRequest", loopResult.FinalUpstreamResult.Content);
        Assert.DoesNotContain("\"tool_calls\"", loopResult.FinalUpstreamResult.AssistantMessageJson ?? string.Empty);
        Assert.Empty(loopResult.AllowedRealToolCalls);
    }

    [Fact]
    public async Task TryPrepareRewriteAsync_ReinsertsWhenPinnedDefinitionIsFolded()
    {
        var conversationId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        const string definitionJson = """
            {"type":"function","function":{"name":"lookup","parameters":{"type":"object","required":["query"]}}}
            """;
        var definitionHash = ComputeSha256Hex(definitionJson);
        var hydrated = ConversationToolDefinition.Create(
            conversationId,
            "lookup",
            definitionHash,
            definitionJson,
            now);

        var foldedPin = ConversationMessage.Create(
            conversationId,
            0,
            MessageRole.Tool,
            $$"""{"tool_name":"lookup","definition":{{definitionJson}}}""",
            10,
            now);
        foldedPin.MarkPinnedForToolSchema();
        foldedPin.MarkFoldedInto(1);

        _clock.Setup(c => c.UtcNow).Returns(now);
        _catalogRepository
            .Setup(r => r.GetByConversationIdAsync(conversationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ConversationToolCatalog.Create(
                conversationId,
                "hash",
                """[{"name":"lookup","description":"Tool lookup.","required":[]}]""",
                now));
        _definitionRepository
            .Setup(r => r.GetByConversationIdAsync(conversationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([hydrated]);

        var orchestrator = CreateOrchestrator();
        var result = await orchestrator.TryPrepareRewriteAsync(
            conversationId,
            [new ChatMessage(MessageRole.User, "hello")],
            ToolsRequest("lookup"),
            [foldedPin],
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains(
            result!.OutgoingMessages,
            m => m.Role == MessageRole.Tool &&
                 m.Content.Contains("definition", StringComparison.Ordinal) &&
                 m.Content.Contains("lookup", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TryPrepareRewriteAsync_DoesNotReinsertWhenUnfoldedPinHasDefinition()
    {
        var conversationId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        const string definitionJson = """
            {"type":"function","function":{"name":"lookup","parameters":{"type":"object","required":["query"]}}}
            """;
        var definitionHash = ComputeSha256Hex(definitionJson);
        var hydrated = ConversationToolDefinition.Create(
            conversationId,
            "lookup",
            definitionHash,
            definitionJson,
            now);

        var livePin = ConversationMessage.Create(
            conversationId,
            0,
            MessageRole.Tool,
            $$"""{"tool_name":"lookup","definition":{{definitionJson}}}""",
            10,
            now);
        livePin.MarkPinnedForToolSchema();

        _clock.Setup(c => c.UtcNow).Returns(now);
        _catalogRepository
            .Setup(r => r.GetByConversationIdAsync(conversationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ConversationToolCatalog.Create(
                conversationId,
                "hash",
                """[{"name":"lookup","description":"Tool lookup.","required":[]}]""",
                now));
        _definitionRepository
            .Setup(r => r.GetByConversationIdAsync(conversationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([hydrated]);

        var orchestrator = CreateOrchestrator();
        var result = await orchestrator.TryPrepareRewriteAsync(
            conversationId,
            [new ChatMessage(MessageRole.User, "hello")],
            ToolsRequest("lookup"),
            [livePin],
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.DoesNotContain(
            result!.OutgoingMessages,
            m => m.Role == MessageRole.Assistant &&
                 m.RawWireMessage is { ValueKind: JsonValueKind.Object } wire &&
                 wire.GetRawText().Contains("reinsert_lookup", StringComparison.Ordinal));
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
