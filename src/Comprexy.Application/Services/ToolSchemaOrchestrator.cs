using System.Text.Json;
using System.Text.Json.Nodes;
using Comprexy.Application.Abstractions;
using Comprexy.Application.Configuration;
using Comprexy.Application.Models;
using Comprexy.Domain.Entities;
using Comprexy.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Comprexy.Application.Services;

public sealed class ToolSchemaSession
{
    public required Guid ConversationId { get; init; }

    public required IReadOnlySet<string> CatalogToolNames { get; init; }

    public Dictionary<string, string> FullDefinitionsByName { get; init; } = new(StringComparer.Ordinal);

    public HashSet<string> HydratedToolNames { get; } = new(StringComparer.Ordinal);

    public List<ToolSchemaPersistedTurn> PendingPersistedTurns { get; } = [];
}

public sealed record ToolSchemaPersistedTurn(
    ChatMessage AssistantMessage,
    ChatMessage ToolMessage,
    bool PinBoth);

public sealed record ToolSchemaPrepareResult(
    IReadOnlyList<ChatMessage> OutgoingMessages,
    JsonElement RewrittenClientRequest,
    ToolSchemaSession Session);

public sealed record ToolSchemaLoopResult(
    UpstreamChatResult FinalUpstreamResult,
    bool RequiresInternalHandling,
    IReadOnlyList<ParsedToolCall> AllowedRealToolCalls);

/// <summary>
/// Compact-index rewrite, meta-tool execution, hydrate loop, and downstream validation.
/// </summary>
public class ToolSchemaOrchestrator
{
    private readonly ToolSchemaOptions _options;
    private readonly ToolCatalogParser _catalogParser;
    private readonly ToolSchemaPromptFactory _promptFactory;
    private readonly ToolArgumentValidator _argumentValidator;
    private readonly IConversationToolCatalogRepository _catalogRepository;
    private readonly IConversationToolDefinitionRepository _definitionRepository;
    private readonly IChatCompletionClient _chatCompletionClient;
    private readonly ITokenEstimator _tokenEstimator;
    private readonly IClock _clock;
    private readonly ILogger<ToolSchemaOrchestrator> _logger;

    public ToolSchemaOrchestrator(
        IOptions<ToolSchemaOptions> options,
        ToolCatalogParser catalogParser,
        ToolSchemaPromptFactory promptFactory,
        ToolArgumentValidator argumentValidator,
        IConversationToolCatalogRepository catalogRepository,
        IConversationToolDefinitionRepository definitionRepository,
        IChatCompletionClient chatCompletionClient,
        ITokenEstimator tokenEstimator,
        IClock clock,
        ILogger<ToolSchemaOrchestrator> logger)
    {
        _options = options.Value;
        _catalogParser = catalogParser;
        _promptFactory = promptFactory;
        _argumentValidator = argumentValidator;
        _catalogRepository = catalogRepository;
        _definitionRepository = definitionRepository;
        _chatCompletionClient = chatCompletionClient;
        _tokenEstimator = tokenEstimator;
        _clock = clock;
        _logger = logger;
    }

    public bool ShouldAttemptActivation(bool passThrough) =>
        _options.Mode == ToolSchemaMode.CompactIndex && !passThrough;

    public async Task<ToolSchemaPrepareResult?> TryPrepareRewriteAsync(
        Guid conversationId,
        IReadOnlyList<ChatMessage> outgoingMessages,
        JsonElement? rawRequest,
        IReadOnlyList<ConversationMessage> storedMessages,
        CancellationToken cancellationToken)
    {
        if (!ShouldAttemptActivation(passThrough: false))
        {
            return null;
        }

        var parsed = _catalogParser.TryParse(rawRequest);
        if (parsed is null || parsed.CompactEntries.Count < _options.MinToolCountToActivate)
        {
            return null;
        }

        if (parsed.HasMetaToolNameCollision)
        {
            _logger.LogWarning(
                "Tool schema compact index disabled for conversation {ConversationId}: client catalog defines {MetaTool}.",
                conversationId,
                ToolSchemaConstants.MetaToolName);
            return null;
        }

        var existingCatalog = await _catalogRepository.GetByConversationIdAsync(conversationId, cancellationToken);
        string compactIndexJson;
        if (existingCatalog is null)
        {
            compactIndexJson = _catalogParser.BuildCompactIndexJson(parsed.CompactEntries);
            var catalog = ConversationToolCatalog.Create(
                conversationId,
                parsed.CatalogHash,
                compactIndexJson,
                _clock.UtcNow);
            _catalogRepository.Add(catalog);

            foreach (var (toolName, definitionJson) in parsed.FullDefinitionsByName)
            {
                var definitionHash = ToolCatalogParser.ComputeSha256Hex(definitionJson);
                _definitionRepository.Add(ConversationToolDefinition.CreateFromSnapshot(
                    conversationId,
                    toolName,
                    definitionHash,
                    definitionJson));
            }
        }
        else
        {
            if (!string.Equals(existingCatalog.CatalogHash, parsed.CatalogHash, StringComparison.Ordinal))
            {
                _logger.LogWarning(
                    "Tool catalog hash mismatch for conversation {ConversationId}: keeping snapshot {SnapshotHash}, inbound {InboundHash}.",
                    conversationId,
                    existingCatalog.CatalogHash,
                    parsed.CatalogHash);
            }

            compactIndexJson = existingCatalog.CompactIndexJson;
        }

        var definitions = await _definitionRepository.GetByConversationIdAsync(conversationId, cancellationToken);
        var definitionsByName = definitions.ToDictionary(d => d.ToolName, d => d.DefinitionJson, StringComparer.Ordinal);
        foreach (var (toolName, definitionJson) in parsed.FullDefinitionsByName)
        {
            definitionsByName[toolName] = definitionJson;
        }

        var session = new ToolSchemaSession
        {
            ConversationId = conversationId,
            CatalogToolNames = parsed.CompactEntries.Select(e => e.Name).ToHashSet(StringComparer.Ordinal),
            FullDefinitionsByName = definitionsByName
        };

        foreach (var definition in definitions.Where(d => d.IsHydrated))
        {
            session.HydratedToolNames.Add(definition.ToolName);
        }

        var rewrittenMessages = InjectToolSchemaSystem(outgoingMessages, compactIndexJson);
        rewrittenMessages = ReinsertHydratedDefinitions(rewrittenMessages, storedMessages, definitions);

        var rewrittenRequest = BuildRewrittenClientRequest(rawRequest, forceStream: false);
        using var rewrittenDoc = JsonDocument.Parse(rewrittenRequest.GetRawText());

        return new ToolSchemaPrepareResult(
            rewrittenMessages,
            rewrittenDoc.RootElement.Clone(),
            session);
    }

    /// <summary>
    /// Ensures each new client <c>role=tool</c> result closes an announced, still-open
    /// <c>tool_call_id</c>. <paramref name="historyMessages"/> must be prior turns only
    /// (assistants that announced calls, earlier tool results) — never the results under validation.
    /// </summary>
    public void ValidateDownstreamToolResults(
        IReadOnlyList<ChatMessage> newClientMessages,
        IReadOnlyList<ConversationMessage> historyMessages)
    {
        var (announced, closed) = CollectToolCallIds(historyMessages);
        foreach (var message in newClientMessages.Where(m => m.Role == MessageRole.Tool))
        {
            var toolCallId = ExtractToolCallIdFromChatMessage(message);
            if (toolCallId is null ||
                !announced.Contains(toolCallId) ||
                closed.Contains(toolCallId))
            {
                throw new InvalidOperationException(
                    $"Downstream tool result references disallowed or unknown tool_call_id '{toolCallId ?? "(missing)"}'.");
            }
        }
    }

    public async Task<ToolSchemaLoopResult> RunInternalLoopAsync(
        ToolSchemaSession session,
        ProviderEndpoint endpoint,
        UpstreamRequest upstreamRequest,
        UpstreamChatResult initialResult,
        CancellationToken cancellationToken)
    {
        var current = initialResult;
        var loopMessages = upstreamRequest.Messages.ToList();
        var rounds = 0;

        while (rounds < _options.MaxHydrateRoundsPerRequest)
        {
            var outcome = await ApplyAssistantRoundAsync(session, loopMessages, current, cancellationToken);
            if (!outcome.NeedsAnotherRound)
            {
                return new ToolSchemaLoopResult(current, RequiresInternalHandling: false, outcome.AllowedRealToolCalls);
            }

            rounds++;
            var nextRequest = upstreamRequest with
            {
                Messages = loopMessages,
                Stream = false,
                ReplaceMessages = true,
                RewrittenClientRequest = upstreamRequest.RewrittenClientRequest ?? upstreamRequest.OriginalClientRequest
            };

            current = await _chatCompletionClient.CompleteAsync(endpoint, nextRequest, cancellationToken);
        }

        _logger.LogWarning(
            "Tool schema hydrate loop reached MaxHydrateRoundsPerRequest ({MaxRounds}) for conversation {ConversationId}.",
            _options.MaxHydrateRoundsPerRequest,
            session.ConversationId);

        return new ToolSchemaLoopResult(current, RequiresInternalHandling: true, []);
    }

    /// <summary>
    /// Streams each hydrate round live. Content/reasoning forward immediately; tool_call
    /// deltas (and trailing finish/usage/[DONE]) are held until the round is classified.
    /// Meta/recover rounds discard the held tail so the client never sees meta tools or an
    /// early [DONE]; the final round flushes the held tail (or already forwarded a clean text stream).
    /// </summary>
    public async Task<ToolSchemaLoopResult> RunStreamingLoopAsync(
        ToolSchemaSession session,
        ProviderEndpoint endpoint,
        UpstreamRequest upstreamRequest,
        Func<string, CancellationToken, Task> onRawSseData,
        CancellationToken cancellationToken)
    {
        var loopMessages = upstreamRequest.Messages.ToList();
        var currentRequest = upstreamRequest with
        {
            Stream = true,
            ReplaceMessages = true,
            RewrittenClientRequest = upstreamRequest.RewrittenClientRequest ?? upstreamRequest.OriginalClientRequest
        };
        var rounds = 0;
        UpstreamChatResult? lastResult = null;

        while (rounds < _options.MaxHydrateRoundsPerRequest)
        {
            var heldChunks = new List<string>();
            var holdingToolTail = false;

            lastResult = await _chatCompletionClient.StreamAsync(
                endpoint,
                currentRequest,
                async (chunk, token) =>
                {
                    if (chunk == "[DONE]")
                    {
                        if (holdingToolTail)
                        {
                            heldChunks.Add(chunk);
                        }
                        else
                        {
                            await onRawSseData(chunk, token);
                        }

                        return;
                    }

                    if (holdingToolTail || ChunkIndicatesToolCalls(chunk))
                    {
                        holdingToolTail = true;
                        heldChunks.Add(chunk);
                        return;
                    }

                    await onRawSseData(chunk, token);
                },
                cancellationToken);

            var outcome = await ApplyAssistantRoundAsync(session, loopMessages, lastResult, cancellationToken);
            if (!outcome.NeedsAnotherRound)
            {
                foreach (var held in heldChunks)
                {
                    await onRawSseData(held, cancellationToken);
                }

                return new ToolSchemaLoopResult(lastResult, RequiresInternalHandling: false, outcome.AllowedRealToolCalls);
            }

            // Discard held meta/invalid tool_calls, finish_reason, usage, and [DONE].
            rounds++;
            currentRequest = currentRequest with { Messages = loopMessages };
        }

        _logger.LogWarning(
            "Tool schema hydrate loop reached MaxHydrateRoundsPerRequest ({MaxRounds}) for conversation {ConversationId}.",
            _options.MaxHydrateRoundsPerRequest,
            session.ConversationId);

        // Client never received [DONE] from discarded meta tails — close the SSE stream.
        await onRawSseData("[DONE]", cancellationToken);

        return new ToolSchemaLoopResult(
            lastResult ?? new UpstreamChatResult(string.Empty, "stop", null, null),
            RequiresInternalHandling: true,
            []);
    }

    private async Task<AssistantRoundOutcome> ApplyAssistantRoundAsync(
        ToolSchemaSession session,
        List<ChatMessage> loopMessages,
        UpstreamChatResult current,
        CancellationToken cancellationToken)
    {
        var toolCalls = ToolCallWireHelper.ParseAssistantToolCalls(current.AssistantMessageJson);
        if (toolCalls.Count == 0)
        {
            return new AssistantRoundOutcome(NeedsAnotherRound: false, []);
        }

        var allowedReal = new List<ParsedToolCall>();
        var needsAnotherRound = false;
        var assistantJson = current.AssistantMessageJson ?? "{}";
        loopMessages.Add(ToolCallWireHelper.BuildAssistantMessage(assistantJson, current.Content));

        foreach (var call in toolCalls)
        {
            if (string.Equals(call.Name, ToolSchemaConstants.MetaToolName, StringComparison.Ordinal))
            {
                var (toolMessage, persist) = await ExecuteMetaToolAsync(session, call, cancellationToken);
                loopMessages.Add(toolMessage);
                session.PendingPersistedTurns.Add(persist);
                needsAnotherRound = true;
                continue;
            }

            var validation = ValidateRealToolCall(session, call);
            if (!validation.IsAllowed)
            {
                var errorJson = BuildToolErrorJson(validation.ErrorCode!, validation.ErrorMessage!);
                loopMessages.Add(ToolCallWireHelper.BuildToolResultMessage(call.Id, errorJson));
                needsAnotherRound = true;
                continue;
            }

            allowedReal.Add(call);
        }

        return new AssistantRoundOutcome(needsAnotherRound, allowedReal);
    }

    private static bool ChunkIndicatesToolCalls(string data)
    {
        try
        {
            using var document = JsonDocument.Parse(data);
            if (!document.RootElement.TryGetProperty("choices", out var choices) ||
                choices.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var choice in choices.EnumerateArray())
            {
                if (choice.ValueKind != JsonValueKind.Object ||
                    !choice.TryGetProperty("delta", out var delta) ||
                    delta.ValueKind != JsonValueKind.Object ||
                    !delta.TryGetProperty("tool_calls", out var toolCalls) ||
                    toolCalls.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                if (toolCalls.GetArrayLength() > 0)
                {
                    return true;
                }
            }
        }
        catch (JsonException)
        {
            // Treat unparseable chunks as non-tool so they can still reach the client.
        }

        return false;
    }

    private sealed record AssistantRoundOutcome(
        bool NeedsAnotherRound,
        IReadOnlyList<ParsedToolCall> AllowedRealToolCalls);

    public JsonElement BuildRewrittenClientRequest(JsonElement? rawRequest, bool forceStream)
    {
        JsonObject root;
        if (rawRequest is { ValueKind: JsonValueKind.Object } original)
        {
            root = JsonNode.Parse(original.GetRawText()) as JsonObject
                ?? throw new InvalidOperationException("Unable to parse client request.");
        }
        else
        {
            root = new JsonObject();
        }

        root["tools"] = JsonNode.Parse($"[{ToolSchemaConstants.MetaToolWireJson}]");
        root.Remove("functions");
        root["stream"] = forceStream;
        using var document = JsonDocument.Parse(root.ToJsonString());
        return document.RootElement.Clone();
    }

    private async Task<(ChatMessage ToolMessage, ToolSchemaPersistedTurn Persist)> ExecuteMetaToolAsync(
        ToolSchemaSession session,
        ParsedToolCall call,
        CancellationToken cancellationToken)
    {
        var arguments = ParseArgumentsObject(call.ArgumentsJson);
        if (!arguments.TryGetValue("tool_name", out var toolName) || string.IsNullOrWhiteSpace(toolName))
        {
            var error = BuildToolErrorJson("invalid_args", "Missing required field 'tool_name'.");
            return (
                ToolCallWireHelper.BuildToolResultMessage(call.Id, error),
                BuildPersistedMetaTurn(call, error));
        }

        toolName = toolName.Trim();
        if (!session.CatalogToolNames.Contains(toolName))
        {
            var error = BuildToolErrorJson("unknown_tool", $"Tool '{toolName}' is not in the compact index.");
            return (
                ToolCallWireHelper.BuildToolResultMessage(call.Id, error),
                BuildPersistedMetaTurn(call, error));
        }

        if (!session.FullDefinitionsByName.TryGetValue(toolName, out var definitionJson))
        {
            var tracked = await _definitionRepository.FindAsync(session.ConversationId, toolName, cancellationToken);
            definitionJson = tracked?.DefinitionJson ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(definitionJson))
        {
            var error = BuildToolErrorJson("unknown_tool", $"No stored definition for tool '{toolName}'.");
            return (
                ToolCallWireHelper.BuildToolResultMessage(call.Id, error),
                BuildPersistedMetaTurn(call, error));
        }

        var definitionHash = ToolCatalogParser.ComputeSha256Hex(definitionJson);
        if (_options.SkipRefetchIfHydrated && session.HydratedToolNames.Contains(toolName))
        {
            var tracked = await _definitionRepository.FindAsync(session.ConversationId, toolName, cancellationToken);
            if (tracked is not null &&
                string.Equals(tracked.DefinitionHash, definitionHash, StringComparison.Ordinal))
            {
                var ack = $$"""{"already_hydrated":true,"tool_name":"{{toolName}}"}""";
                return (
                    ToolCallWireHelper.BuildToolResultMessage(call.Id, ack),
                    BuildPersistedMetaTurn(call, ack));
            }
        }

        await MarkHydratedAsync(session, toolName, definitionHash, definitionJson, cancellationToken);
        var payload = $$"""{"tool_name":"{{toolName}}","definition":{{definitionJson}}}""";
        return (
            ToolCallWireHelper.BuildToolResultMessage(call.Id, payload),
            BuildPersistedMetaTurn(call, payload));
    }

    private async Task MarkHydratedAsync(
        ToolSchemaSession session,
        string toolName,
        string definitionHash,
        string definitionJson,
        CancellationToken cancellationToken)
    {
        session.HydratedToolNames.Add(toolName);
        session.FullDefinitionsByName[toolName] = definitionJson;

        var existing = await _definitionRepository.FindAsync(session.ConversationId, toolName, cancellationToken);
        var now = _clock.UtcNow;
        if (existing is null)
        {
            _definitionRepository.Add(ConversationToolDefinition.Create(
                session.ConversationId,
                toolName,
                definitionHash,
                definitionJson,
                now));
            return;
        }

        existing.MarkHydrated(definitionHash, definitionJson, now);
    }

    private (bool IsAllowed, string? ErrorCode, string? ErrorMessage) ValidateRealToolCall(
        ToolSchemaSession session,
        ParsedToolCall call)
    {
        if (!session.CatalogToolNames.Contains(call.Name))
        {
            return (false, "unknown_tool", $"Tool '{call.Name}' is not in the compact index.");
        }

        if (!session.HydratedToolNames.Contains(call.Name))
        {
            return (false, "not_hydrated", $"Tool '{call.Name}' must be hydrated via get_tool_definition first.");
        }

        if (!session.FullDefinitionsByName.TryGetValue(call.Name, out var definitionJson))
        {
            return (false, "not_hydrated", $"No stored definition for tool '{call.Name}'.");
        }

        var schemaJson = _argumentValidator.ExtractParametersSchemaJson(definitionJson);
        var validation = _argumentValidator.Validate(schemaJson, call.ArgumentsJson);
        if (!validation.IsValid)
        {
            return (false, validation.ErrorCode ?? "schema_invalid", validation.Details ?? "Schema validation failed.");
        }

        return (true, null, null);
    }

    private static string BuildToolErrorJson(string code, string details) =>
        JsonSerializer.Serialize(new { error = details, code, details });

    private static Dictionary<string, string> ParseArgumentsObject(string argumentsJson)
    {
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return new Dictionary<string, string>(StringComparer.Ordinal);
            }

            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    result[property.Name] = property.Value.GetString() ?? string.Empty;
                }
            }

            return result;
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    private IReadOnlyList<ChatMessage> InjectToolSchemaSystem(
        IReadOnlyList<ChatMessage> outgoingMessages,
        string compactIndexJson)
    {
        var toolSchemaSystem = new ChatMessage(
            MessageRole.System,
            _promptFactory.BuildSystemContent(compactIndexJson));

        var messages = outgoingMessages.ToList();
        if (messages.Count == 0 || messages[0].Role != MessageRole.System)
        {
            messages.Insert(0, toolSchemaSystem);
            return messages;
        }

        messages.Insert(1, toolSchemaSystem);
        return messages;
    }

    private IReadOnlyList<ChatMessage> ReinsertHydratedDefinitions(
        IReadOnlyList<ChatMessage> outgoingMessages,
        IReadOnlyList<ConversationMessage> storedMessages,
        IReadOnlyList<ConversationToolDefinition> definitions)
    {
        var hydrated = definitions.Where(d => d.IsHydrated).ToList();
        if (hydrated.Count == 0)
        {
            return outgoingMessages;
        }

        var pinnedToolNames = ExtractPinnedHydratedToolNames(storedMessages);
        var toReinsert = hydrated
            .Where(d => !pinnedToolNames.Contains(d.ToolName))
            .OrderBy(d => d.ToolName, StringComparer.Ordinal)
            .ToList();

        if (toReinsert.Count == 0)
        {
            return outgoingMessages;
        }

        var reinserted = new List<ChatMessage>();
        foreach (var definition in toReinsert)
        {
            var syntheticId = $"reinsert_{definition.ToolName}";
            var assistantWire = $$"""
                {
                  "role": "assistant",
                  "content": "",
                  "tool_calls": [{
                    "id": "{{syntheticId}}",
                    "type": "function",
                    "function": {
                      "name": "get_tool_definition",
                      "arguments": "{\"tool_name\":\"{{definition.ToolName}}\"}"
                    }
                  }]
                }
                """;
            using (var assistantDoc = JsonDocument.Parse(assistantWire))
            {
                reinserted.Add(new ChatMessage(
                    MessageRole.Assistant,
                    string.Empty,
                    assistantDoc.RootElement.Clone()));
            }

            var toolPayload = $$"""{"tool_name":"{{definition.ToolName}}","definition":{{definition.DefinitionJson}}}""";
            reinserted.Add(ToolCallWireHelper.BuildToolResultMessage(syntheticId, toolPayload));
        }

        var result = outgoingMessages.ToList();
        var insertAt = result.Count > 0 && result[0].Role == MessageRole.System ? 1 : 0;
        if (insertAt < result.Count && result[insertAt].Role == MessageRole.System)
        {
            insertAt++;
        }

        result.InsertRange(insertAt, reinserted);
        return result;
    }

    private static HashSet<string> ExtractPinnedHydratedToolNames(IReadOnlyList<ConversationMessage> storedMessages)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var message in storedMessages.Where(m => m.IsPinnedForToolSchema && m.Role == MessageRole.Tool))
        {
            if (string.IsNullOrWhiteSpace(message.Content))
            {
                continue;
            }

            if (message.Content.Contains("already_hydrated", StringComparison.Ordinal) &&
                TryExtractToolNameFromResult(message.Content) is { } ackName)
            {
                names.Add(ackName);
                continue;
            }

            if (TryExtractToolNameFromResult(message.Content) is { } hydratedName)
            {
                names.Add(hydratedName);
            }
        }

        return names;
    }

    private static string? TryExtractToolNameFromResult(string content)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            if (document.RootElement.TryGetProperty("tool_name", out var toolName) &&
                toolName.ValueKind == JsonValueKind.String)
            {
                return toolName.GetString();
            }
        }
        catch (JsonException)
        {
            // ignore
        }

        return null;
    }

    private static ToolSchemaPersistedTurn BuildPersistedMetaTurn(ParsedToolCall call, string toolContent)
    {
        var assistantWire = $$"""
            {
              "role": "assistant",
              "content": "",
              "tool_calls": [{
                "id": "{{call.Id}}",
                "type": "function",
                "function": {
                  "name": "{{call.Name}}",
                  "arguments": {{JsonSerializer.Serialize(call.ArgumentsJson)}}
                }
              }]
            }
            """;
        using var assistantDoc = JsonDocument.Parse(assistantWire);
        var assistant = new ChatMessage(MessageRole.Assistant, string.Empty, assistantDoc.RootElement.Clone());
        var tool = ToolCallWireHelper.BuildToolResultMessage(call.Id, toolContent);
        return new ToolSchemaPersistedTurn(assistant, tool, PinBoth: true);
    }

    private static (HashSet<string> Announced, HashSet<string> Closed) CollectToolCallIds(
        IReadOnlyList<ConversationMessage> storedMessages)
    {
        var announced = new HashSet<string>(StringComparer.Ordinal);
        var closed = new HashSet<string>(StringComparer.Ordinal);

        foreach (var message in storedMessages.OrderBy(m => m.Sequence))
        {
            if (message.Role == MessageRole.Assistant)
            {
                foreach (var id in FileReadPathExtractor.GetAssistantToolCallIds(message))
                {
                    announced.Add(id);
                }

                continue;
            }

            if (message.Role == MessageRole.Tool)
            {
                var toolCallId = FileReadPathExtractor.TryExtractToolCallId(message);
                if (toolCallId is not null)
                {
                    closed.Add(toolCallId);
                }
            }
        }

        return (announced, closed);
    }

    private static string? ExtractToolCallIdFromChatMessage(ChatMessage message)
    {
        if (message.RawWireMessage is not { ValueKind: JsonValueKind.Object } wire)
        {
            return null;
        }

        if (wire.TryGetProperty("tool_call_id", out var idElement) &&
            idElement.ValueKind == JsonValueKind.String)
        {
            return idElement.GetString();
        }

        return null;
    }
}
