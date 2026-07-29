using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Comprexy.Application.Abstractions;
using Comprexy.Application.Configuration;
using Comprexy.Application.Models;
using Comprexy.Application.Services.ToolIr;
using Comprexy.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Comprexy.Application.Services;

/// <summary>
/// Blocking Compression-endpoint LLM mapper. Never caches invalid maps; caller persists on success.
/// Mapper usage is folded into conversation compression-overhead telemetry.
/// </summary>
public class ToolIrSchemaMapper
{
    private readonly ToolSchemaOptions _options;
    private readonly CompressionOptions _compressionOptions;
    private readonly ProviderEndpointResolver _endpointResolver;
    private readonly IChatCompletionClient _chatCompletionClient;
    private readonly ITokenEstimator _tokenEstimator;
    private readonly IConversationMetricsRecorder _metricsRecorder;
    private readonly ILogger<ToolIrSchemaMapper> _logger;

    public ToolIrSchemaMapper(
        IOptions<ToolSchemaOptions> options,
        IOptions<CompressionOptions> compressionOptions,
        ProviderEndpointResolver endpointResolver,
        IChatCompletionClient chatCompletionClient,
        ITokenEstimator tokenEstimator,
        IConversationMetricsRecorder metricsRecorder,
        ILogger<ToolIrSchemaMapper> logger)
    {
        _options = options.Value;
        _compressionOptions = compressionOptions.Value;
        _endpointResolver = endpointResolver;
        _chatCompletionClient = chatCompletionClient;
        _tokenEstimator = tokenEstimator;
        _metricsRecorder = metricsRecorder;
        _logger = logger;
    }

    public async Task<ToolIrMappingValidator.ValidationResult> MapAsync(
        Guid conversationId,
        string catalogHash,
        IReadOnlyDictionary<string, string> fullDefinitionsByName,
        CancellationToken cancellationToken,
        string? preferredModel = null)
    {
        var catalogNames = fullDefinitionsByName.Keys.ToHashSet(StringComparer.Ordinal);
        // When Provider/Compression model are unset, use preferredModel (client chat model).
        var endpoint = _endpointResolver.ResolveCompression().WithPreferredModel(preferredModel);
        var maxAttempts = Math.Max(1, 1 + _options.MappingMaxRetries);
        ToolIrMappingValidator.ValidationResult? last = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? rawContent;
            try
            {
                rawContent = await CallMapperAsync(
                    conversationId,
                    endpoint,
                    catalogHash,
                    fullDefinitionsByName,
                    last?.Error,
                    cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(
                    ex,
                    "Tool IR mapper attempt {Attempt}/{MaxAttempts} failed for schema_hash {CatalogHash}.",
                    attempt,
                    maxAttempts,
                    catalogHash);
                last = new ToolIrMappingValidator.ValidationResult(false, null, ex.Message);
                continue;
            }

            var json = ExtractJsonObject(rawContent);
            last = ToolIrMappingValidator.Validate(
                json ?? string.Empty,
                catalogNames,
                catalogHash,
                fullDefinitionsByName);
            if (last.IsValid)
            {
                _logger.LogInformation(
                    "Tool IR mapping succeeded for schema_hash {CatalogHash} on attempt {Attempt}/{MaxAttempts}.",
                    catalogHash,
                    attempt,
                    maxAttempts);
                return last;
            }

            _logger.LogWarning(
                "Tool IR mapping invalid on attempt {Attempt}/{MaxAttempts} for schema_hash {CatalogHash}: {Error}",
                attempt,
                maxAttempts,
                catalogHash,
                last.Error);
        }

        return last ?? new ToolIrMappingValidator.ValidationResult(false, null, "Mapper produced no output.");
    }

    private async Task<string> CallMapperAsync(
        Guid conversationId,
        ProviderEndpoint endpoint,
        string catalogHash,
        IReadOnlyDictionary<string, string> fullDefinitionsByName,
        string? previousError,
        CancellationToken cancellationToken)
    {
        if (!endpoint.HasConfiguredModel)
        {
            throw new InvalidOperationException(
                "Tool IR mapping requires a model. Set Provider:Model or Compression:Model, or send model on the chat request.");
        }

        var catalogJson = BuildCatalogPayload(fullDefinitionsByName);
        var system = BuildSystemPrompt();
        var user = BuildUserPrompt(catalogHash, catalogJson, previousError);

        var messages = new List<ChatMessage>
        {
            new(MessageRole.System, system),
            new(MessageRole.User, user)
        };

        var result = await _chatCompletionClient.CompleteAsync(
            endpoint,
            new UpstreamRequest(
                messages,
                Stream: false,
                OriginalClientRequest: null,
                CallOptions: new ChatCompletionCallOptions(Temperature: _compressionOptions.Temperature),
                ReplaceMessages: true,
                Purpose: UpstreamRequestPurpose.Compression),
            cancellationToken);

        await RecordMapperOverheadAsync(conversationId, messages, result, cancellationToken);
        return result.Content ?? string.Empty;
    }

    private async Task RecordMapperOverheadAsync(
        Guid conversationId,
        IReadOnlyList<ChatMessage> mapperMessages,
        UpstreamChatResult result,
        CancellationToken cancellationToken)
    {
        var promptTokens = result.PromptTokens
            ?? _tokenEstimator.CountPromptTokens(mapperMessages);
        var completionTokens = result.CompletionTokens
            ?? _tokenEstimator.CountTokens(result.Content ?? string.Empty);
        var overhead = promptTokens + completionTokens;
        if (overhead <= 0)
        {
            return;
        }

        await _metricsRecorder.RecordCompressionOverheadAsync(conversationId, overhead, cancellationToken);
        _logger.LogDebug(
            "Tool IR mapper overhead recorded for conversation {ConversationId}: prompt={PromptTokens} completion={CompletionTokens} total={OverheadTokens}.",
            conversationId,
            promptTokens,
            completionTokens,
            overhead);
    }

    private static string BuildCatalogPayload(IReadOnlyDictionary<string, string> fullDefinitionsByName)
    {
        var array = new JsonArray();
        foreach (var (name, definitionJson) in fullDefinitionsByName.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            try
            {
                array.Add(JsonNode.Parse(definitionJson));
            }
            catch (JsonException)
            {
                array.Add(new JsonObject
                {
                    ["type"] = "function",
                    ["function"] = new JsonObject { ["name"] = name }
                });
            }
        }

        return array.ToJsonString();
    }

    private static string BuildSystemPrompt() =>
        """
        You map an IDE client's OpenAI-compatible tools[] catalog into Comprexy Virtual Tools MappingJson.
        Return ONLY a single JSON object (no markdown fences) with this exact shape:
        {
          "schema_hash": "<echo the provided schema_hash>",
          "client_capabilities": [
            {
              "client_tool": "<exact client tool name>",
              "capability": "FILE_READ_RAW|FILE_SEARCH_BACKEND|DIRECTORY_LIST_BACKEND|FILE_METADATA|OTHER_FILE|SHELL_BACKEND|NON_FILE",
              "risk": "low|medium|high",
              "supports": { "path": true, "offset": false, "limit": false, "query": false }
            }
          ],
          "bindings": [
            {
              "comprexy_tool": "comprexy_read_file_manifest|comprexy_read_file_range|comprexy_read_file_search|comprexy_dir_list|comprexy_shell",
              "primary_client_tool": "<exact client tool name from catalog>",
              "strategy": "direct|read_then_slice",
              "arg_map": { "path": "<client arg name>", "start_line": "<optional>", "end_line": "<optional>", "query": "<optional>", "command": "<optional>", "working_directory": "<optional>", "block_until_ms": "<optional>", "description": "<optional>" },
              "defaults": { "<client required arg with no IR source>": "<literal JSON value>" }
            }
          ]
        }
        Rules:
        - Include every inbound client tool exactly once in client_capabilities.
        - Mark write/edit/ApplyPatch/MCP/browser and other mutate tools as NON_FILE (full-schema passthrough). Never OTHER_FILE for write/edit.
        - Mark terminal / Shell / bash / run_terminal_cmd (and equivalents) as SHELL_BACKEND — not NON_FILE. Bind comprexy_shell to that primary with strategy direct.
        - OTHER_FILE is only for rare file-adjacent tools that are neither Virtual backends nor passthrough mutates; unbound OTHER_FILE/FILE_METADATA still pass through on the model surface.
        - Only emit bindings for MVP comprexy_* tools that have a suitable primary_client_tool.
        - Prefer purpose-fit tools (list_dir / Grep / Read) over overloading Glob when the catalog has them.
        - comprexy_read_file_manifest: bind to FILE_READ_RAW or FILE_METADATA (e.g. Read). Path is a FILE. Never Glob or directory-list tools.
        - comprexy_read_file_range: bind to FILE_READ_RAW (e.g. Read). Use read_then_slice when the native read tool lacks offset/limit; otherwise direct.
        - comprexy_read_file_search: bind to Grep/content search (FILE_SEARCH_BACKEND). Prefer Grep over Glob; if Glob is used, arg_map query→pattern (or glob_pattern).
        - comprexy_dir_list: prefer DIRECTORY_LIST_BACKEND. Glob/glob is allowed (FILE_SEARCH_BACKEND): arg_map must rename IR fields to the exact client parameter names from the catalog schema (e.g. path→path or path→target_directory).
        - comprexy_shell: bind to SHELL_BACKEND. strategy must be direct. arg_map IR→client names for command (required), and optionally working_directory, block_until_ms, description. Prefer short IR surface; do not invent client-only knobs (notify_on_output, smart-mode, etc.) unless the client schema requires them via defaults.
        - defaults: client parameter name → JSON literal. Emit defaults whenever the primary client tool has required properties that Virtual IR cannot supply (classic: directory listing via a glob tool that requires pattern → defaults.pattern = "*" or defaults.glob_pattern = "*"). IR-mapped values win over defaults for the same client key. Never invent client tool or parameter names not in the catalog schema.
        - Never invent client tool names. Never include tools not in the catalog.
        """;

    private static string BuildUserPrompt(string catalogHash, string catalogJson, string? previousError)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"schema_hash: {catalogHash}");
        if (!string.IsNullOrWhiteSpace(previousError))
        {
            sb.AppendLine();
            sb.AppendLine("Previous mapping was invalid. Fix these errors:");
            sb.AppendLine(previousError);
        }

        sb.AppendLine();
        sb.AppendLine("Client tools catalog JSON:");
        sb.AppendLine(catalogJson);
        return sb.ToString();
    }

    private static string? ExtractJsonObject(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        var trimmed = content.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = trimmed.IndexOf('\n');
            if (firstNewline >= 0)
            {
                trimmed = trimmed[(firstNewline + 1)..];
            }

            var fence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (fence >= 0)
            {
                trimmed = trimmed[..fence];
            }

            trimmed = trimmed.Trim();
        }

        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return trimmed;
        }

        return trimmed[start..(end + 1)];
    }
}
