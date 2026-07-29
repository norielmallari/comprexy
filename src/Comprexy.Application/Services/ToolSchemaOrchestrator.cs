using System.Text.Json;
using System.Text.Json.Nodes;
using Comprexy.Application.Abstractions;
using Comprexy.Application.Configuration;
using Comprexy.Application.Models;
using Comprexy.Application.Services.ToolIr;
using Comprexy.Domain.Entities;
using Comprexy.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Comprexy.Application.Services;

public sealed class ToolSchemaSession
{
    public required Guid ConversationId { get; init; }

    public required IReadOnlySet<string> CatalogToolNames { get; init; }

    public required ToolIrMappingDocument Mapping { get; init; }

    public Dictionary<string, string> FullDefinitionsByName { get; init; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Client tool names replaced by Virtual IR (hidden from model catalog). Prefer this name.
    /// </summary>
    public HashSet<string> ReplacedClientToolNames { get; init; } = new(StringComparer.Ordinal);

    /// <summary>Obsolete alias — use <see cref="ReplacedClientToolNames"/>.</summary>
    public HashSet<string> FileClientToolNames
    {
        get => ReplacedClientToolNames;
        init => ReplacedClientToolNames = value;
    }

    public HashSet<string> BoundVirtualToolNames { get; init; } = new(StringComparer.Ordinal);

    public List<ToolSchemaPersistedTurn> PendingPersistedTurns { get; } = [];

    /// <summary>
    /// Locally satisfied IR tool results for the final assistant turn (persist after that assistant).
    /// </summary>
    public List<ChatMessage> PendingLocalToolResults { get; } = [];
}

public sealed record ToolSchemaPersistedTurn(
    ChatMessage AssistantMessage,
    ChatMessage ToolMessage);

public sealed record ToolSchemaPrepareResult(
    IReadOnlyList<ChatMessage> OutgoingMessages,
    JsonElement RewrittenClientRequest,
    ToolSchemaSession Session);

/// <summary>
/// Prepare outcome: optional Virtual rewrite plus whether catalog/definition mutations need UoW flush
/// (MappingJson success or DisableToolIr).
/// </summary>
public sealed record ToolSchemaPrepareOutcome(
    ToolSchemaPrepareResult? Result,
    bool CatalogMutated);

/// <summary>
/// Inbound tool rewrite: IR observations ready to persist, plus client ids to complete after save.
/// </summary>
public sealed record ToolInboundRewriteResult(
    IReadOnlyList<ChatMessage> Messages,
    IReadOnlyList<string> CompletedClientCallIds);

public sealed record ToolSchemaLoopResult(
    UpstreamChatResult FinalUpstreamResult,
    bool RequiresInternalHandling,
    IReadOnlyList<ParsedToolCall> AllowedRealToolCalls);

/// <summary>
/// Virtual Tools (Tool IR): schema mapping, outbound rewrite, deterministic planner, wire remap, distillation.
/// </summary>
public class ToolSchemaOrchestrator
{
    private readonly ToolSchemaOptions _options;
    private readonly ToolCatalogParser _catalogParser;
    private readonly ToolArgumentValidator _argumentValidator;
    private readonly ToolIrSchemaMapper _schemaMapper;
    private readonly ToolIrPlanner _planner;
    private readonly ToolIrResultDistiller _distiller;
    private readonly IToolIrCallIdMapService _callIdMap;
    private readonly IConversationToolCatalogRepository _catalogRepository;
    private readonly IConversationToolDefinitionRepository _definitionRepository;
    private readonly IChatCompletionClient _chatCompletionClient;
    private readonly IClock _clock;
    private readonly ILogger<ToolSchemaOrchestrator> _logger;

    public ToolSchemaOrchestrator(
        IOptions<ToolSchemaOptions> options,
        ToolCatalogParser catalogParser,
        ToolArgumentValidator argumentValidator,
        ToolIrSchemaMapper schemaMapper,
        ToolIrPlanner planner,
        ToolIrResultDistiller distiller,
        IToolIrCallIdMapService callIdMap,
        IConversationToolCatalogRepository catalogRepository,
        IConversationToolDefinitionRepository definitionRepository,
        IChatCompletionClient chatCompletionClient,
        IClock clock,
        ILogger<ToolSchemaOrchestrator> logger)
    {
        _options = options.Value;
        _catalogParser = catalogParser;
        _argumentValidator = argumentValidator;
        _schemaMapper = schemaMapper;
        _planner = planner;
        _distiller = distiller;
        _callIdMap = callIdMap;
        _catalogRepository = catalogRepository;
        _definitionRepository = definitionRepository;
        _chatCompletionClient = chatCompletionClient;
        _clock = clock;
        _logger = logger;
    }

    public bool ShouldAttemptActivation(bool passThrough) =>
        _options.Mode == ToolSchemaMode.Virtual && !passThrough;

    /// <summary>
    /// Validates inbound client tool results and rewrites Virtual Tools results into IR observations
    /// (IR tool_call_id + distilled content) for persistence and model rebuild.
    /// Dual-id rows are listed in <see cref="ToolInboundRewriteResult.CompletedClientCallIds"/> —
    /// the caller must <c>CompleteAsync</c> them only after the rewritten tool messages are persisted.
    /// </summary>
    /// <param name="clientSyncedPrefix">
    /// Client messages already covered by <c>SyncedMessageCount</c> (authoritative snapshot prefix).
    /// Used so rewind / mid-chain tool results can close ids announced earlier in the client history
    /// even when those assistants are not in <paramref name="newClientMessages"/>.
    /// </param>
    /// <param name="replacedClientToolNames">
    /// Native client tools replaced by Virtual <c>comprexy_*</c> backends (see
    /// <see cref="ToolIrMappingValidator.GetReplacedClientToolNames"/>). Assistants/results for these
    /// names are never persisted into the IR transcript — client wire only.
    /// </param>
    public async Task<ToolInboundRewriteResult> ValidateAndRewriteInboundToolResultsAsync(
        Guid conversationId,
        IReadOnlyList<ChatMessage> newClientMessages,
        IReadOnlyList<ConversationMessage> historyMessages,
        IReadOnlyList<ChatMessage> clientSyncedPrefix,
        CancellationToken cancellationToken,
        IReadOnlySet<string>? replacedClientToolNames = null)
    {
        var replaced = replacedClientToolNames ?? (IReadOnlySet<string>)new HashSet<string>(StringComparer.Ordinal);
        var (announced, closed) = CollectToolCallIds(historyMessages);
        // Client snapshot prefix is authoritative for wire ids after rewind / time-travel.
        MergeChatMessageToolCallIds(clientSyncedPrefix, announced, closed);

        var announcedCalls = IndexAnnouncedToolCalls(historyMessages, clientSyncedPrefix);
        var rewritten = new List<ChatMessage>(newClientMessages.Count);
        var completedClientIds = new List<string>();
        var suppressedClientIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var message in newClientMessages)
        {
            // Same-request assistant turns announce tool_call_ids before later role=tool
            // messages in the batch (empty DB / sync rewind / full client history replay).
            if (message.Role == MessageRole.Assistant)
            {
                foreach (var id in GetAssistantToolCallIdsFromChatMessage(message))
                {
                    announced.Add(id);
                }

                IndexAssistantToolCallsFromChatMessage(message, announcedCalls);

                if (AssistantUsesReplacedClientTool(message, replaced, out var replacedIds))
                {
                    foreach (var id in replacedIds)
                    {
                        suppressedClientIds.Add(id);
                    }

                    // Do not persist client-native remapped file-tool assistants into the IR transcript.
                    continue;
                }

                rewritten.Add(message);
                continue;
            }

            if (message.Role != MessageRole.Tool)
            {
                rewritten.Add(message);
                continue;
            }

            var toolCallId = ExtractToolCallIdFromChatMessage(message);
            if (toolCallId is null)
            {
                throw new InvalidOperationException(
                    "Downstream tool result references disallowed or unknown tool_call_id '(missing)'.");
            }

            var mapping = await _callIdMap.TryGetByClientIdAsync(conversationId, toolCallId, cancellationToken);
            if (mapping is not null)
            {
                var observation = _distiller.Distill(conversationId, mapping, message.Content ?? string.Empty);
                rewritten.Add(ToolCallWireHelper.BuildToolResultMessage(mapping.IrCallId, observation));
                completedClientIds.Add(toolCallId);
                closed.Add(mapping.IrCallId);
                closed.Add(toolCallId);
                continue;
            }

            if (!announced.Contains(toolCallId) || closed.Contains(toolCallId))
            {
                throw new InvalidOperationException(
                    $"Downstream tool result references disallowed or unknown tool_call_id '{toolCallId}'.");
            }

            if (suppressedClientIds.Contains(toolCallId) ||
                IsReplacedClientToolCall(toolCallId, announcedCalls, replaced))
            {
                // Swallow native results for replaced file tools — never store client wire in IR history.
                closed.Add(toolCallId);
                continue;
            }

            // Announced passthrough (NON_FILE etc.) with no dual-id row: persist native as-is.
            MaybeInvalidateFileCacheAfterMutation(conversationId, toolCallId, message.Content, announcedCalls);
            rewritten.Add(message);
            closed.Add(toolCallId);
        }

        return new ToolInboundRewriteResult(rewritten, completedClientIds);
    }

    private static bool AssistantUsesReplacedClientTool(
        ChatMessage message,
        IReadOnlySet<string> replacedClientToolNames,
        out List<string> replacedCallIds)
    {
        replacedCallIds = [];
        if (replacedClientToolNames.Count == 0 ||
            message.RawWireMessage is not { ValueKind: JsonValueKind.Object } wire ||
            !wire.TryGetProperty("tool_calls", out var toolCalls) ||
            toolCalls.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var anyReplaced = false;
        var allIds = new List<string>();
        foreach (var call in toolCalls.EnumerateArray())
        {
            if (call.ValueKind != JsonValueKind.Object ||
                !call.TryGetProperty("id", out var idElement) ||
                idElement.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var id = idElement.GetString();
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            allIds.Add(id.Trim());

            var name = string.Empty;
            if (call.TryGetProperty("function", out var function) &&
                function.ValueKind == JsonValueKind.Object &&
                function.TryGetProperty("name", out var nameElement) &&
                nameElement.ValueKind == JsonValueKind.String)
            {
                name = nameElement.GetString() ?? string.Empty;
            }

            if (replacedClientToolNames.Contains(name))
            {
                anyReplaced = true;
            }
        }

        if (!anyReplaced)
        {
            return false;
        }

        // Drop the whole assistant when any call is a replaced file tool (keeps tool-result chains atomic).
        replacedCallIds = allIds;
        return true;
    }

    private static bool IsReplacedClientToolCall(
        string toolCallId,
        Dictionary<string, AnnouncedClientToolCall> announcedCalls,
        IReadOnlySet<string> replacedClientToolNames) =>
        replacedClientToolNames.Count > 0 &&
        announcedCalls.TryGetValue(toolCallId, out var announced) &&
        replacedClientToolNames.Contains(announced.Name);

    /// <summary>
    /// Drops pending dual-id rows after a client snapshot rewind abandons an open tool round.
    /// </summary>
    public Task ClearPendingToolCallMapsAsync(Guid conversationId, CancellationToken cancellationToken) =>
        _callIdMap.ClearIfNoOpenToolCallsAsync(conversationId, assistantHasOpenToolCalls: false, cancellationToken);

    /// <summary>
    /// Deletes a dual-id row after the corresponding IR tool observation has been persisted.
    /// </summary>
    public Task CompleteInboundToolCallAsync(
        Guid conversationId,
        string clientCallId,
        CancellationToken cancellationToken) =>
        _callIdMap.CompleteAsync(conversationId, clientCallId, cancellationToken);

    /// <summary>
    /// Ensures each new client <c>role=tool</c> result closes an announced, still-open
    /// <c>tool_call_id</c>. Prefer <see cref="ValidateAndRewriteInboundToolResultsAsync"/> on the Virtual path.
    /// </summary>
    public void ValidateDownstreamToolResults(
        IReadOnlyList<ChatMessage> newClientMessages,
        IReadOnlyList<ConversationMessage> historyMessages,
        IReadOnlyList<ChatMessage>? clientSyncedPrefix = null)
    {
        var (announced, closed) = CollectToolCallIds(historyMessages);
        if (clientSyncedPrefix is not null)
        {
            MergeChatMessageToolCallIds(clientSyncedPrefix, announced, closed);
        }

        foreach (var message in newClientMessages)
        {
            if (message.Role == MessageRole.Assistant)
            {
                foreach (var id in GetAssistantToolCallIdsFromChatMessage(message))
                {
                    announced.Add(id);
                }

                continue;
            }

            if (message.Role != MessageRole.Tool)
            {
                continue;
            }

            var toolCallId = ExtractToolCallIdFromChatMessage(message);
            if (toolCallId is null ||
                !announced.Contains(toolCallId) ||
                closed.Contains(toolCallId))
            {
                throw new InvalidOperationException(
                    $"Downstream tool result references disallowed or unknown tool_call_id '{toolCallId ?? "(missing)"}'.");
            }

            closed.Add(toolCallId);
        }
    }

    /// <summary>
    /// Resolves native client tools replaced by Virtual <c>comprexy_*</c> backends for inbound
    /// ingest filtering. Ensures MappingJson when the catalog hash needs a map so the first
    /// Virtual turn can drop client-native remapped history before it is staged.
    /// </summary>
    public async Task<(IReadOnlySet<string> ReplacedClientToolNames, bool CatalogMutated)> ResolveReplacedClientToolNamesAsync(
        Guid conversationId,
        JsonElement? rawRequest,
        CancellationToken cancellationToken)
    {
        if (!ShouldAttemptActivation(passThrough: false))
        {
            return (new HashSet<string>(StringComparer.Ordinal), CatalogMutated: false);
        }

        var parsed = _catalogParser.TryParse(rawRequest);
        if (parsed is null || parsed.HasMetaToolNameCollision)
        {
            return (new HashSet<string>(StringComparer.Ordinal), CatalogMutated: false);
        }

        var existingCatalog = await _catalogRepository.GetByConversationIdAsync(conversationId, cancellationToken);
        if (existingCatalog is not null &&
            string.Equals(existingCatalog.CatalogHash, parsed.CatalogHash, StringComparison.Ordinal) &&
            existingCatalog.ToolIrDisabled)
        {
            return (new HashSet<string>(StringComparer.Ordinal), CatalogMutated: false);
        }

        var catalogToolNames = parsed.CompactEntries.Select(e => e.Name).ToHashSet(StringComparer.Ordinal);
        if (existingCatalog is not null &&
            string.Equals(existingCatalog.CatalogHash, parsed.CatalogHash, StringComparison.Ordinal) &&
            !existingCatalog.ToolIrDisabled &&
            !string.IsNullOrWhiteSpace(existingCatalog.MappingJson))
        {
            var cached = ToolIrMappingValidator.Validate(
                existingCatalog.MappingJson,
                catalogToolNames,
                parsed.CatalogHash,
                parsed.FullDefinitionsByName);
            if (cached.IsValid && cached.Document is not null)
            {
                return (ToolIrMappingValidator.GetReplacedClientToolNames(cached.Document), CatalogMutated: false);
            }
        }

        // Cold / invalid map: reuse prepare path (persists MappingJson or DisableToolIr).
        var outcome = await TryPrepareRewriteAsync(
            conversationId,
            [],
            rawRequest,
            cancellationToken);

        if (outcome.Result is null)
        {
            return (new HashSet<string>(StringComparer.Ordinal), outcome.CatalogMutated);
        }

        return (outcome.Result.Session.ReplacedClientToolNames, outcome.CatalogMutated);
    }

    public async Task<ToolSchemaPrepareOutcome> TryPrepareRewriteAsync(
        Guid conversationId,
        IReadOnlyList<ChatMessage> outgoingMessages,
        JsonElement? rawRequest,
        CancellationToken cancellationToken)
    {
        if (!ShouldAttemptActivation(passThrough: false))
        {
            return new ToolSchemaPrepareOutcome(null, CatalogMutated: false);
        }

        var parsed = _catalogParser.TryParse(rawRequest);
        if (parsed is null)
        {
            return new ToolSchemaPrepareOutcome(null, CatalogMutated: false);
        }

        if (parsed.HasMetaToolNameCollision)
        {
            _logger.LogWarning(
                "Tool schema Virtual mode disabled for conversation {ConversationId}: client catalog defines a reserved tool name ({ConversationIdMetaTool} or comprexy_*).",
                conversationId,
                ToolSchemaConstants.ConversationIdMetaToolName);
            return new ToolSchemaPrepareOutcome(null, CatalogMutated: false);
        }

        var existingCatalog = await _catalogRepository.GetByConversationIdAsync(conversationId, cancellationToken);
        ToolIrMappingDocument mapping;

        if (existingCatalog is not null &&
            string.Equals(existingCatalog.CatalogHash, parsed.CatalogHash, StringComparison.Ordinal) &&
            existingCatalog.ToolIrDisabled)
        {
            _logger.LogInformation(
                "Tool IR disabled for conversation {ConversationId} schema_hash {CatalogHash}; forwarding client tools unchanged.",
                conversationId,
                parsed.CatalogHash);
            return new ToolSchemaPrepareOutcome(null, CatalogMutated: false);
        }

        var catalogToolNames = parsed.CompactEntries.Select(e => e.Name).ToHashSet(StringComparer.Ordinal);
        ToolIrMappingDocument? cachedMapping = null;
        if (existingCatalog is not null &&
            string.Equals(existingCatalog.CatalogHash, parsed.CatalogHash, StringComparison.Ordinal) &&
            !existingCatalog.ToolIrDisabled &&
            !string.IsNullOrWhiteSpace(existingCatalog.MappingJson))
        {
            var cached = ToolIrMappingValidator.Validate(
                existingCatalog.MappingJson,
                catalogToolNames,
                parsed.CatalogHash,
                parsed.FullDefinitionsByName);
            if (cached.IsValid && cached.Document is not null)
            {
                cachedMapping = cached.Document;
            }
            else
            {
                _logger.LogWarning(
                    "Persisted MappingJson failed validation for conversation {ConversationId}: {Error}. Remapping.",
                    conversationId,
                    cached.Error);
            }
        }

        if (cachedMapping is not null)
        {
            mapping = cachedMapping;
        }
        else
        {
            _logger.LogInformation(
                "Tool IR mapping required for conversation {ConversationId} schema_hash {CatalogHash} (blocking).",
                conversationId,
                parsed.CatalogHash);

            var preferredModel = TryGetClientModel(rawRequest);
            var mapped = await _schemaMapper.MapAsync(
                conversationId,
                parsed.CatalogHash,
                parsed.FullDefinitionsByName,
                cancellationToken,
                preferredModel);

            if (!mapped.IsValid || mapped.Document is null)
            {
                _logger.LogError(
                    "Tool IR mapping failed for conversation {ConversationId} schema_hash {CatalogHash}: {Error}. DisableToolIr — native tools forwarded; compression remains on.",
                    conversationId,
                    parsed.CatalogHash,
                    mapped.Error);

                if (existingCatalog is null)
                {
                    _catalogRepository.Add(ConversationToolCatalog.Create(
                        conversationId,
                        parsed.CatalogHash,
                        mappingJson: string.Empty,
                        _clock.UtcNow,
                        toolIrDisabled: true));
                }
                else
                {
                    existingCatalog.DisableToolIr(parsed.CatalogHash, _clock.UtcNow);
                }

                await UpsertDefinitionsAsync(conversationId, parsed.FullDefinitionsByName, cancellationToken);
                return new ToolSchemaPrepareOutcome(null, CatalogMutated: true);
            }

            mapping = mapped.Document;
            var mappingJson = JsonSerializer.Serialize(mapping);
            if (existingCatalog is null)
            {
                _catalogRepository.Add(ConversationToolCatalog.Create(
                    conversationId,
                    parsed.CatalogHash,
                    mappingJson,
                    _clock.UtcNow));
            }
            else
            {
                existingCatalog.ReplaceMapping(parsed.CatalogHash, mappingJson, _clock.UtcNow);
            }
        }

        await UpsertDefinitionsAsync(conversationId, parsed.FullDefinitionsByName, cancellationToken);

        var definitions = await _definitionRepository.GetByConversationIdAsync(conversationId, cancellationToken);
        var definitionsByName = definitions.ToDictionary(d => d.ToolName, d => d.DefinitionJson, StringComparer.Ordinal);
        foreach (var (toolName, definitionJson) in parsed.FullDefinitionsByName)
        {
            definitionsByName[toolName] = definitionJson;
        }

        var replacedClientTools = ToolIrMappingValidator.GetReplacedClientToolNames(mapping)
            .ToHashSet(StringComparer.Ordinal);
        var boundVirtual = mapping.Bindings
            .Select(b => b.ComprexyTool)
            .Where(ToolSchemaConstants.IsVirtualTool)
            .ToHashSet(StringComparer.Ordinal);

        var session = new ToolSchemaSession
        {
            ConversationId = conversationId,
            CatalogToolNames = parsed.CompactEntries.Select(e => e.Name).ToHashSet(StringComparer.Ordinal),
            Mapping = mapping,
            FullDefinitionsByName = definitionsByName,
            ReplacedClientToolNames = replacedClientTools,
            BoundVirtualToolNames = boundVirtual
        };

        var rewrittenRequest = BuildRewrittenClientRequest(rawRequest, session, forceStream: false);
        using var rewrittenDoc = JsonDocument.Parse(rewrittenRequest.GetRawText());

        return new ToolSchemaPrepareOutcome(
            new ToolSchemaPrepareResult(
                outgoingMessages,
                rewrittenDoc.RootElement.Clone(),
                session),
            CatalogMutated: true);
    }

    /// <summary>
    /// Clears abandoned dual-id pending when the turn ends without open tool_calls.
    /// Open IR→client rounds keep pending until inbound results (or TTL).
    /// </summary>
    public Task OnRequestCompletedAsync(Guid conversationId, string? assistantMessageJson, CancellationToken cancellationToken)
    {
        var hasOpen = ToolCallWireHelper.ParseAssistantToolCalls(assistantMessageJson).Count > 0;
        return _callIdMap.ClearIfNoOpenToolCallsAsync(conversationId, hasOpen, cancellationToken);
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
        const int maxMetaRounds = 8;
        var rounds = 0;

        while (rounds < maxMetaRounds)
        {
            var outcome = await ApplyAssistantRoundAsync(session, loopMessages, current, cancellationToken);
            if (!outcome.NeedsAnotherRound)
            {
                var final = ApplyClientFacingRewrite(current, outcome);
                return new ToolSchemaLoopResult(final, RequiresInternalHandling: false, outcome.ClientFacingToolCalls);
            }

            rounds++;
            var nextRequest = upstreamRequest with
            {
                Messages = loopMessages,
                Stream = false,
                ReplaceMessages = true,
                RewrittenClientRequest = BuildRewrittenClientRequest(
                    upstreamRequest.OriginalClientRequest,
                    session,
                    forceStream: false)
            };
            current = await _chatCompletionClient.CompleteAsync(endpoint, nextRequest, cancellationToken);
        }

        _logger.LogWarning(
            "Tool IR internal loop reached round cap for conversation {ConversationId}.",
            session.ConversationId);

        return BuildStoppedResult(
            current,
            "Tool IR loop stopped: exceeded meta round cap without a client-bound tool call or final answer.");
    }

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
        const int maxMetaRounds = 8;
        var rounds = 0;
        UpstreamChatResult? lastResult = null;

        while (rounds < maxMetaRounds)
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

                    if (holdingToolTail || ToolCallWireHelper.StreamChunkHasToolCalls(chunk))
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
                var final = ApplyClientFacingRewrite(lastResult, outcome);
                if (outcome.ClientFacingToolCalls.Count > 0 ||
                    !string.IsNullOrWhiteSpace(outcome.ClientFacingAssistantMessageJson))
                {
                    await EmitRewrittenToolCallsSseAsync(final, onRawSseData, cancellationToken);
                }
                else
                {
                    foreach (var held in heldChunks)
                    {
                        await onRawSseData(held, cancellationToken);
                    }
                }

                return new ToolSchemaLoopResult(final, RequiresInternalHandling: false, outcome.ClientFacingToolCalls);
            }

            rounds++;
            currentRequest = currentRequest with
            {
                Messages = loopMessages,
                RewrittenClientRequest = BuildRewrittenClientRequest(
                    upstreamRequest.OriginalClientRequest,
                    session,
                    forceStream: true)
            };
        }

        _logger.LogWarning(
            "Tool IR streaming loop reached round cap for conversation {ConversationId}.",
            session.ConversationId);
        await onRawSseData("[DONE]", cancellationToken);
        return BuildStoppedResult(
            lastResult,
            "Tool IR loop stopped: exceeded meta round cap without a client-bound tool call or final answer.");
    }

    private async Task UpsertDefinitionsAsync(
        Guid conversationId,
        IReadOnlyDictionary<string, string> fullDefinitionsByName,
        CancellationToken cancellationToken)
    {
        var existing = await _definitionRepository.GetByConversationIdAsync(conversationId, cancellationToken);
        var byName = existing.ToDictionary(d => d.ToolName, StringComparer.Ordinal);
        foreach (var (toolName, definitionJson) in fullDefinitionsByName)
        {
            var definitionHash = ToolCatalogParser.ComputeSha256Hex(definitionJson);
            if (byName.TryGetValue(toolName, out var row))
            {
                if (!string.Equals(row.DefinitionHash, definitionHash, StringComparison.Ordinal))
                {
                    row.ReplaceSnapshot(definitionHash, definitionJson);
                }
            }
            else
            {
                _definitionRepository.Add(ConversationToolDefinition.CreateFromSnapshot(
                    conversationId,
                    toolName,
                    definitionHash,
                    definitionJson));
            }
        }
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
            return new AssistantRoundOutcome(
                NeedsAnotherRound: false,
                [],
                IrAssistantMessageJson: current.AssistantMessageJson,
                ClientFacingAssistantMessageJson: current.AssistantMessageJson,
                ClientFacingRawResponseJson: current.RawResponseJson);
        }

        var assistantJson = current.AssistantMessageJson ?? "{}";
        loopMessages.Add(ToolCallWireHelper.BuildAssistantMessage(assistantJson, current.Content));

        var metaHandled = false;
        var localOnly = true;
        var nativePlans = new List<ToolIrPlanItem>();
        var passthroughCalls = new List<ParsedToolCall>();
        var irCallsForPlan = new List<ParsedToolCall>();

        foreach (var call in toolCalls)
        {
            if (ToolSchemaConstants.IsConversationIdMetaTool(call.Name))
            {
                var (toolMessage, persist) = ExecuteConversationIdMetaTool(session, call);
                loopMessages.Add(toolMessage);
                session.PendingPersistedTurns.Add(persist);
                metaHandled = true;
                continue;
            }

            if (ToolSchemaConstants.IsVirtualTool(call.Name))
            {
                if (!session.BoundVirtualToolNames.Contains(call.Name))
                {
                    var error = BuildToolErrorJson("unbound_tool", $"Virtual tool '{call.Name}' has no validated binding.");
                    loopMessages.Add(ToolCallWireHelper.BuildToolResultMessage(call.Id, error));
                    metaHandled = true;
                    continue;
                }

                var schemaJson = ExtractVirtualParametersSchema(call.Name);
                var validation = _argumentValidator.Validate(schemaJson, call.ArgumentsJson);
                if (!validation.IsValid)
                {
                    var error = BuildToolErrorJson(
                        validation.ErrorCode ?? "schema_invalid",
                        validation.Details ?? "Schema validation failed.");
                    loopMessages.Add(ToolCallWireHelper.BuildToolResultMessage(call.Id, error));
                    metaHandled = true;
                    continue;
                }

                var normalized = call with
                {
                    ArgumentsJson = validation.NormalizedArgumentsJson ?? call.ArgumentsJson
                };
                irCallsForPlan.Add(normalized);
                continue;
            }

            // Passthrough (or unexpected name).
            if (!session.CatalogToolNames.Contains(call.Name) ||
                session.ReplacedClientToolNames.Contains(call.Name))
            {
                var error = BuildToolErrorJson(
                    "unknown_tool",
                    $"Tool '{call.Name}' is not available on the Virtual Tools surface.");
                loopMessages.Add(ToolCallWireHelper.BuildToolResultMessage(call.Id, error));
                metaHandled = true;
                continue;
            }

            if (!session.FullDefinitionsByName.TryGetValue(call.Name, out var definitionJson))
            {
                var error = BuildToolErrorJson("unknown_tool", $"No stored definition for tool '{call.Name}'.");
                loopMessages.Add(ToolCallWireHelper.BuildToolResultMessage(call.Id, error));
                metaHandled = true;
                continue;
            }

            var passthroughSchema = _argumentValidator.ExtractParametersSchemaJson(definitionJson);
            var passthroughValidation = _argumentValidator.Validate(passthroughSchema, call.ArgumentsJson);
            if (!passthroughValidation.IsValid)
            {
                var error = BuildToolErrorJson(
                    passthroughValidation.ErrorCode ?? "schema_invalid",
                    passthroughValidation.Details ?? "Schema validation failed.");
                loopMessages.Add(ToolCallWireHelper.BuildToolResultMessage(call.Id, error));
                metaHandled = true;
                continue;
            }

            passthroughCalls.Add(call with
            {
                ArgumentsJson = passthroughValidation.NormalizedArgumentsJson ?? call.ArgumentsJson
            });
            localOnly = false;
        }

        if (irCallsForPlan.Count > 0)
        {
            var plans = _planner.Plan(session.ConversationId, irCallsForPlan, session.Mapping);
            foreach (var plan in plans)
            {
                if (plan.Kind == ToolIrPlanKind.LocalObservation)
                {
                    var toolMessage = ToolCallWireHelper.BuildToolResultMessage(
                        plan.IrCall.Id,
                        plan.ObservationJson ?? "{}");
                    loopMessages.Add(toolMessage);
                    session.PendingLocalToolResults.Add(toolMessage);
                    metaHandled = true;
                    continue;
                }

                var nativeArgs = plan.ClientArgumentsJson ?? "{}";
                if (!string.IsNullOrWhiteSpace(plan.ClientToolName) &&
                    session.FullDefinitionsByName.TryGetValue(plan.ClientToolName, out var nativeDefinitionJson))
                {
                    var nativeSchema = _argumentValidator.ExtractParametersSchemaJson(nativeDefinitionJson);
                    var nativeValidation = _argumentValidator.Validate(nativeSchema, nativeArgs);
                    if (!nativeValidation.IsValid)
                    {
                        var error = BuildToolErrorJson(
                            nativeValidation.ErrorCode ?? "schema_invalid",
                            nativeValidation.Details ?? "Native arguments failed client schema validation.");
                        loopMessages.Add(ToolCallWireHelper.BuildToolResultMessage(plan.IrCall.Id, error));
                        metaHandled = true;
                        continue;
                    }

                    nativeArgs = nativeValidation.NormalizedArgumentsJson ?? nativeArgs;
                }

                var mapping = plan.Mapping;
                if (mapping is not null &&
                    !string.Equals(mapping.ClientArgumentsJson, nativeArgs, StringComparison.Ordinal))
                {
                    mapping = mapping with { ClientArgumentsJson = nativeArgs };
                }

                localOnly = false;
                if (mapping is not null)
                {
                    await _callIdMap.RegisterAsync(mapping, cancellationToken);
                }

                nativePlans.Add(plan with
                {
                    ClientArgumentsJson = nativeArgs,
                    Mapping = mapping
                });
            }
        }

        if (metaHandled && localOnly && passthroughCalls.Count == 0 && nativePlans.Count == 0)
        {
            // Pure local-satisfy / meta internal round: IR assistant+observation live only in
            // ephemeral loopMessages for this request (not PendingPersistedTurns). Cleared so a
            // later final assistant does not double-persist; stored transcript rebuild will not
            // see these intermediate cache-hit turns (MVP ephemeral semantics).
            session.PendingLocalToolResults.Clear();
            return new AssistantRoundOutcome(
                NeedsAnotherRound: true,
                [],
                IrAssistantMessageJson: assistantJson,
                ClientFacingAssistantMessageJson: null,
                ClientFacingRawResponseJson: null);
        }

        // Mixed or client-bound round: keep only local results that close this final assistant.
        if (nativePlans.Count == 0 && passthroughCalls.Count == 0)
        {
            session.PendingLocalToolResults.Clear();
        }

        var clientFacingCalls = new List<ParsedToolCall>();
        foreach (var plan in nativePlans)
        {
            clientFacingCalls.Add(new ParsedToolCall(
                plan.ClientCallId!,
                plan.ClientToolName!,
                plan.ClientArgumentsJson ?? "{}"));
        }

        foreach (var call in passthroughCalls)
        {
            clientFacingCalls.Add(call);
        }

        var irAssistantJson = BuildAssistantToolCallsJson(
            assistantJson,
            irCallsForPlan.Concat(passthroughCalls).ToList(),
            current.Content);
        var clientAssistantJson = BuildAssistantToolCallsJson(
            assistantJson,
            clientFacingCalls,
            current.Content);
        var clientRaw = RewriteRawResponseToolCalls(current.RawResponseJson, clientAssistantJson);

        return new AssistantRoundOutcome(
            NeedsAnotherRound: false,
            clientFacingCalls,
            IrAssistantMessageJson: irAssistantJson,
            ClientFacingAssistantMessageJson: clientAssistantJson,
            ClientFacingRawResponseJson: clientRaw);
    }

    private static UpstreamChatResult ApplyClientFacingRewrite(
        UpstreamChatResult current,
        AssistantRoundOutcome outcome)
    {
        return current with
        {
            AssistantMessageJson = outcome.IrAssistantMessageJson ?? current.AssistantMessageJson,
            RawResponseJson = outcome.ClientFacingRawResponseJson ?? current.RawResponseJson
        };
    }

    private static string BuildAssistantToolCallsJson(
        string originalAssistantJson,
        IReadOnlyList<ParsedToolCall> calls,
        string? content)
    {
        JsonObject root;
        try
        {
            root = JsonNode.Parse(string.IsNullOrWhiteSpace(originalAssistantJson) ? "{}" : originalAssistantJson)
                as JsonObject ?? new JsonObject();
        }
        catch (JsonException)
        {
            root = new JsonObject();
        }

        root["role"] = "assistant";
        if (!string.IsNullOrEmpty(content))
        {
            root["content"] = content;
        }
        else if (!root.ContainsKey("content"))
        {
            root["content"] = "";
        }

        var toolCalls = new JsonArray();
        foreach (var call in calls)
        {
            toolCalls.Add(new JsonObject
            {
                ["id"] = call.Id,
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = call.Name,
                    ["arguments"] = call.ArgumentsJson
                }
            });
        }

        root["tool_calls"] = toolCalls;
        return root.ToJsonString();
    }

    private static string? RewriteRawResponseToolCalls(string? rawResponseJson, string clientAssistantJson)
    {
        if (string.IsNullOrWhiteSpace(rawResponseJson))
        {
            return BuildSyntheticRawResponse(clientAssistantJson);
        }

        try
        {
            var root = JsonNode.Parse(rawResponseJson) as JsonObject;
            if (root is null)
            {
                return BuildSyntheticRawResponse(clientAssistantJson);
            }

            if (root["choices"] is JsonArray choices && choices.Count > 0 && choices[0] is JsonObject choice)
            {
                choice["message"] = JsonNode.Parse(clientAssistantJson);
                choice["finish_reason"] = "tool_calls";
            }

            return root.ToJsonString();
        }
        catch (JsonException)
        {
            return BuildSyntheticRawResponse(clientAssistantJson);
        }
    }

    private static string BuildSyntheticRawResponse(string assistantJson) =>
        new JsonObject
        {
            ["choices"] = new JsonArray
            {
                new JsonObject
                {
                    ["index"] = 0,
                    ["message"] = JsonNode.Parse(assistantJson),
                    ["finish_reason"] = "tool_calls"
                }
            }
        }.ToJsonString();

    private JsonElement BuildRewrittenClientRequest(
        JsonElement? rawRequest,
        ToolSchemaSession session,
        bool forceStream)
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

        var tools = new JsonArray();
        foreach (var name in VirtualToolRegistry.VirtualToolNames.OrderBy(n => n, StringComparer.Ordinal))
        {
            if (session.BoundVirtualToolNames.Contains(name))
            {
                tools.Add(ToolIrVirtualToolDefinitions.ParseWire(name));
            }
        }

        tools.Add(JsonNode.Parse(ToolSchemaConstants.ConversationIdMetaToolWireJson)!);

        foreach (var (toolName, definitionJson) in session.FullDefinitionsByName
                     .OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (session.ReplacedClientToolNames.Contains(toolName))
            {
                continue;
            }

            if (ToolSchemaConstants.IsReservedToolName(toolName))
            {
                continue;
            }

            var node = JsonNode.Parse(definitionJson);
            if (node is not null)
            {
                tools.Add(node);
            }
        }

        root["tools"] = tools;
        root.Remove("functions");
        root["stream"] = forceStream;
        using var document = JsonDocument.Parse(root.ToJsonString());
        return document.RootElement.Clone();
    }

    private static (ChatMessage ToolMessage, ToolSchemaPersistedTurn Persist) ExecuteConversationIdMetaTool(
        ToolSchemaSession session,
        ParsedToolCall call)
    {
        var payload = JsonSerializer.Serialize(new
        {
            conversation_id = session.ConversationId.ToString("D"),
            instruction =
                "Use this conversation_id as conversationId for tools that require it (for example comprexy telemetry MCP). " +
                "Do not invent or guess a UUID."
        });
        return (
            ToolCallWireHelper.BuildToolResultMessage(call.Id, payload),
            BuildPersistedMetaTurn(call, payload));
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
        return new ToolSchemaPersistedTurn(assistant, tool);
    }

    private static string ExtractVirtualParametersSchema(string toolName)
    {
        using var document = JsonDocument.Parse(ToolIrVirtualToolDefinitions.GetWireJson(toolName));
        if (document.RootElement.TryGetProperty("function", out var function) &&
            function.TryGetProperty("parameters", out var parameters))
        {
            return parameters.GetRawText();
        }

        return "{}";
    }

    private static string BuildToolErrorJson(string code, string details) =>
        JsonSerializer.Serialize(new { error = details, code, details });

    private static ToolSchemaLoopResult BuildStoppedResult(UpstreamChatResult? last, string message)
    {
        var assistantJson = JsonSerializer.Serialize(new
        {
            role = "assistant",
            content = message
        });
        return new ToolSchemaLoopResult(
            new UpstreamChatResult(
                Content: message,
                FinishReason: "stop",
                PromptTokens: last?.PromptTokens,
                CompletionTokens: last?.CompletionTokens,
                RawResponseJson: null,
                AssistantMessageJson: assistantJson),
            RequiresInternalHandling: true,
            []);
    }

    private static async Task EmitRewrittenToolCallsSseAsync(
        UpstreamChatResult result,
        Func<string, CancellationToken, Task> onRawSseData,
        CancellationToken cancellationToken)
    {
        // Prefer client-facing message embedded in RawResponseJson; fall back to AssistantMessageJson.
        string? assistantJson = null;
        if (!string.IsNullOrWhiteSpace(result.RawResponseJson))
        {
            try
            {
                using var rawDoc = JsonDocument.Parse(result.RawResponseJson);
                if (rawDoc.RootElement.TryGetProperty("choices", out var choices) &&
                    choices.ValueKind == JsonValueKind.Array &&
                    choices.GetArrayLength() > 0 &&
                    choices[0].TryGetProperty("message", out var message))
                {
                    assistantJson = message.GetRawText();
                }
            }
            catch (JsonException)
            {
                // fall through
            }
        }

        assistantJson ??= result.AssistantMessageJson;
        if (string.IsNullOrWhiteSpace(assistantJson))
        {
            await onRawSseData("[DONE]", cancellationToken);
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(assistantJson);
            if (!document.RootElement.TryGetProperty("tool_calls", out var toolCalls) ||
                toolCalls.ValueKind != JsonValueKind.Array)
            {
                await onRawSseData("[DONE]", cancellationToken);
                return;
            }

            var deltaToolCalls = new JsonArray();
            var index = 0;
            foreach (var call in toolCalls.EnumerateArray())
            {
                if (call.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var deltaCall = new JsonObject
                {
                    ["index"] = index++,
                    ["id"] = call.TryGetProperty("id", out var id) ? id.GetString() : null,
                    ["type"] = call.TryGetProperty("type", out var type) ? type.GetString() : "function"
                };

                if (call.TryGetProperty("function", out var function) &&
                    function.ValueKind == JsonValueKind.Object)
                {
                    var fn = new JsonObject();
                    if (function.TryGetProperty("name", out var name))
                    {
                        fn["name"] = name.GetString();
                    }

                    if (function.TryGetProperty("arguments", out var arguments))
                    {
                        fn["arguments"] = arguments.ValueKind == JsonValueKind.String
                            ? arguments.GetString()
                            : arguments.GetRawText();
                    }

                    deltaCall["function"] = fn;
                }

                deltaToolCalls.Add(deltaCall);
            }

            var chunk = new JsonObject
            {
                ["choices"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["index"] = 0,
                        ["delta"] = new JsonObject
                        {
                            ["tool_calls"] = deltaToolCalls
                        },
                        ["finish_reason"] = "tool_calls"
                    }
                }
            };

            if (result.PromptTokens is not null || result.CompletionTokens is not null)
            {
                var usage = new JsonObject();
                if (result.PromptTokens is not null)
                {
                    usage["prompt_tokens"] = result.PromptTokens.Value;
                }

                if (result.CompletionTokens is not null)
                {
                    usage["completion_tokens"] = result.CompletionTokens.Value;
                }

                if (result.PromptTokens is not null && result.CompletionTokens is not null)
                {
                    usage["total_tokens"] = result.PromptTokens.Value + result.CompletionTokens.Value;
                }

                chunk["usage"] = usage;
            }

            await onRawSseData(chunk.ToJsonString(), cancellationToken);
        }
        catch (JsonException)
        {
            // Fall through to DONE so the client is not left hanging.
        }

        await onRawSseData("[DONE]", cancellationToken);
    }

    private sealed record AssistantRoundOutcome(
        bool NeedsAnotherRound,
        IReadOnlyList<ParsedToolCall> ClientFacingToolCalls,
        string? IrAssistantMessageJson,
        string? ClientFacingAssistantMessageJson,
        string? ClientFacingRawResponseJson);

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

    private static void MergeChatMessageToolCallIds(
        IReadOnlyList<ChatMessage> messages,
        HashSet<string> announced,
        HashSet<string> closed)
    {
        foreach (var message in messages)
        {
            if (message.Role == MessageRole.Assistant)
            {
                foreach (var id in GetAssistantToolCallIdsFromChatMessage(message))
                {
                    announced.Add(id);
                }

                continue;
            }

            if (message.Role != MessageRole.Tool)
            {
                continue;
            }

            var toolCallId = ExtractToolCallIdFromChatMessage(message);
            if (toolCallId is not null)
            {
                closed.Add(toolCallId);
            }
        }
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

    private static IEnumerable<string> GetAssistantToolCallIdsFromChatMessage(ChatMessage message)
    {
        if (message.RawWireMessage is not { ValueKind: JsonValueKind.Object } wire)
        {
            yield break;
        }

        if (!wire.TryGetProperty("tool_calls", out var toolCalls) ||
            toolCalls.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var call in toolCalls.EnumerateArray())
        {
            if (call.ValueKind == JsonValueKind.Object &&
                call.TryGetProperty("id", out var id) &&
                id.ValueKind == JsonValueKind.String)
            {
                var value = id.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    yield return value.Trim();
                }
            }
        }
    }

    private void MaybeInvalidateFileCacheAfterMutation(
        Guid conversationId,
        string toolCallId,
        string? toolResultContent,
        Dictionary<string, AnnouncedClientToolCall> announcedCalls)
    {
        if (!LooksLikeSuccessfulFileMutation(toolResultContent) ||
            !announcedCalls.TryGetValue(toolCallId, out var call) ||
            !IsMutatingFileTool(call.Name))
        {
            return;
        }

        var path = TryExtractPathFromToolArguments(call.ArgumentsJson);
        if (path is null)
        {
            return;
        }

        var removed = _distiller.InvalidateCachedFile(conversationId, path);
        if (removed > 0)
        {
            _logger.LogDebug(
                "Invalidated {Count} file-cache entr(y/ies) for conversation {ConversationId} path {Path} after {Tool} success.",
                removed,
                conversationId,
                path,
                call.Name);
        }
    }

    private static Dictionary<string, AnnouncedClientToolCall> IndexAnnouncedToolCalls(
        IReadOnlyList<ConversationMessage> historyMessages,
        IReadOnlyList<ChatMessage> clientSyncedPrefix)
    {
        var index = new Dictionary<string, AnnouncedClientToolCall>(StringComparer.Ordinal);
        foreach (var message in historyMessages.OrderBy(m => m.Sequence))
        {
            if (message.Role != MessageRole.Assistant || string.IsNullOrWhiteSpace(message.RawWireJson))
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(message.RawWireJson);
                IndexToolCallsFromWire(document.RootElement, index);
            }
            catch (JsonException)
            {
                // Ignore unparseable history rows.
            }
        }

        foreach (var message in clientSyncedPrefix)
        {
            IndexAssistantToolCallsFromChatMessage(message, index);
        }

        return index;
    }

    private static void IndexAssistantToolCallsFromChatMessage(
        ChatMessage message,
        Dictionary<string, AnnouncedClientToolCall> index)
    {
        if (message.RawWireMessage is not { ValueKind: JsonValueKind.Object } wire)
        {
            return;
        }

        IndexToolCallsFromWire(wire, index);
    }

    private static void IndexToolCallsFromWire(
        JsonElement wire,
        Dictionary<string, AnnouncedClientToolCall> index)
    {
        if (!wire.TryGetProperty("tool_calls", out var toolCalls) ||
            toolCalls.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var call in toolCalls.EnumerateArray())
        {
            if (call.ValueKind != JsonValueKind.Object ||
                !call.TryGetProperty("id", out var idElement) ||
                idElement.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var id = idElement.GetString();
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            var name = string.Empty;
            var argsJson = "{}";
            if (call.TryGetProperty("function", out var function) &&
                function.ValueKind == JsonValueKind.Object)
            {
                if (function.TryGetProperty("name", out var nameElement) &&
                    nameElement.ValueKind == JsonValueKind.String)
                {
                    name = nameElement.GetString() ?? string.Empty;
                }

                if (function.TryGetProperty("arguments", out var argsElement))
                {
                    argsJson = argsElement.ValueKind == JsonValueKind.String
                        ? argsElement.GetString() ?? "{}"
                        : argsElement.GetRawText();
                }
            }

            index[id.Trim()] = new AnnouncedClientToolCall(name, argsJson);
        }
    }

    private static bool IsMutatingFileTool(string toolName) =>
        toolName.Equals("edit", StringComparison.OrdinalIgnoreCase) ||
        toolName.Equals("write", StringComparison.OrdinalIgnoreCase) ||
        toolName.Equals("StrReplace", StringComparison.OrdinalIgnoreCase) ||
        toolName.Equals("Write", StringComparison.OrdinalIgnoreCase) ||
        toolName.Equals("search_replace", StringComparison.OrdinalIgnoreCase) ||
        toolName.Equals("ApplyPatch", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeSuccessfulFileMutation(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        if (content.Contains("Could not find", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("Error:", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("failed", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return content.Contains("Edit applied successfully", StringComparison.OrdinalIgnoreCase) ||
               content.Contains("Wrote contents", StringComparison.OrdinalIgnoreCase) ||
               content.Contains("Updated file", StringComparison.OrdinalIgnoreCase) ||
               content.Contains("has been written", StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryExtractPathFromToolArguments(string argumentsJson)
    {
        if (string.IsNullOrWhiteSpace(argumentsJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(argumentsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (var name in new[] { "filePath", "file_path", "target_file", "path" })
            {
                if (document.RootElement.TryGetProperty(name, out var value) &&
                    value.ValueKind == JsonValueKind.String)
                {
                    var path = value.GetString();
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        return path.Trim();
                    }
                }
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private sealed record AnnouncedClientToolCall(string Name, string ArgumentsJson);

    private static string? TryGetClientModel(JsonElement? rawRequest)
    {
        if (rawRequest is { ValueKind: JsonValueKind.Object } raw &&
            raw.TryGetProperty("model", out var model) &&
            model.ValueKind == JsonValueKind.String)
        {
            return model.GetString();
        }

        return null;
    }
}
