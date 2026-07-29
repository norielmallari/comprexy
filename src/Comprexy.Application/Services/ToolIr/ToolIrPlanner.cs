using System.Text.Json;
using System.Text.Json.Nodes;
using Comprexy.Application.Configuration;
using Comprexy.Application.Services.ToolIr;
using Microsoft.Extensions.Options;

namespace Comprexy.Application.Services;

public enum ToolIrPlanKind
{
    LocalObservation,
    NativeClientCall
}

public sealed record ToolIrPlanItem(
    ToolIrPlanKind Kind,
    ParsedToolCall IrCall,
    string? ObservationJson,
    string? ClientCallId,
    string? ClientToolName,
    string? ClientArgumentsJson,
    ToolIrCallMapping? Mapping);

/// <summary>
/// Deterministic IR → native (or local cache) planner. No mid-conversation LLM re-planning.
/// </summary>
public class ToolIrPlanner
{
    private readonly ToolSchemaOptions _options;
    private readonly ToolIrFileBodyCache _fileCache;

    public ToolIrPlanner(IOptions<ToolSchemaOptions> options, ToolIrFileBodyCache fileCache)
    {
        _options = options.Value;
        _fileCache = fileCache;
    }

    public IReadOnlyList<ToolIrPlanItem> Plan(
        Guid conversationId,
        IReadOnlyList<ParsedToolCall> irCalls,
        ToolIrMappingDocument mapping)
    {
        var items = new List<ToolIrPlanItem>(irCalls.Count);
        foreach (var call in irCalls)
        {
            items.Add(PlanOne(conversationId, call, mapping));
        }

        return items;
    }

    private ToolIrPlanItem PlanOne(
        Guid conversationId,
        ParsedToolCall call,
        ToolIrMappingDocument mapping)
    {
        var binding = ToolIrMappingValidator.FindBinding(mapping, call.Name);
        if (binding is null)
        {
            return new ToolIrPlanItem(
                ToolIrPlanKind.LocalObservation,
                call,
                BuildErrorObservation("unbound_tool", $"No validated binding for '{call.Name}'."),
                null,
                null,
                null,
                null);
        }

        var args = ParseArgs(call.ArgumentsJson);
        return call.Name switch
        {
            ToolSchemaConstants.FileRangeToolName => PlanFileRange(conversationId, call, binding, mapping, args),
            ToolSchemaConstants.FileManifestToolName => PlanFileManifest(conversationId, call, binding, mapping, args),
            ToolSchemaConstants.FileSearchToolName => PlanNativeOrError(conversationId, call, binding, mapping, args, requirePath: false),
            ToolSchemaConstants.DirListToolName => PlanNativeOrError(conversationId, call, binding, mapping, args, requirePath: true),
            ToolSchemaConstants.ShellToolName => PlanShell(conversationId, call, binding, args),
            _ => new ToolIrPlanItem(
                ToolIrPlanKind.LocalObservation,
                call,
                BuildErrorObservation("unknown_tool", $"Unsupported virtual tool '{call.Name}'."),
                null,
                null,
                null,
                null)
        };
    }

    private ToolIrPlanItem PlanShell(
        Guid conversationId,
        ParsedToolCall call,
        ToolIrBinding binding,
        Dictionary<string, JsonElement> args)
    {
        if (!TryGetString(args, "command", out _))
        {
            return LocalError(call, "invalid_args", "comprexy_shell requires command.");
        }

        var clientArgs = BuildNativeArgs(binding, args, ToolIrStrategies.Direct, startLine: 0, endLine: 0);
        var clientCallId = NewClientCallId(call.Id);
        var map = new ToolIrCallMapping(
            conversationId,
            call.Id,
            clientCallId,
            call.Name,
            binding.PrimaryClientTool,
            call.ArgumentsJson,
            clientArgs,
            ToolIrStrategies.Direct,
            Path: null,
            StartLine: null,
            EndLine: null,
            Pending: true);

        return new ToolIrPlanItem(
            ToolIrPlanKind.NativeClientCall,
            call,
            null,
            clientCallId,
            binding.PrimaryClientTool,
            clientArgs,
            map);
    }

    private ToolIrPlanItem PlanFileRange(
        Guid conversationId,
        ParsedToolCall call,
        ToolIrBinding binding,
        ToolIrMappingDocument mapping,
        Dictionary<string, JsonElement> args)
    {
        if (!TryGetString(args, "path", out var path) ||
            !TryGetInt(args, "start_line", out var startLine) ||
            !TryGetInt(args, "end_line", out var endLine))
        {
            return LocalError(call, "invalid_args", "comprexy_read_file_range requires path, start_line, and end_line.");
        }

        if (startLine < 1 || endLine < startLine)
        {
            return LocalError(call, "invalid_args", "start_line/end_line must be 1-based with end_line >= start_line.");
        }

        if (_fileCache.TryGetCovering(conversationId, path!, startLine, endLine, out var cached) &&
            cached is not null &&
            ToolIrFileBodyCache.TrySliceLines(
                cached,
                startLine,
                endLine,
                _options.MaxRangeLines,
                out var text,
                out var truncated))
        {
            var observation = JsonSerializer.Serialize(new
            {
                type = "file_range",
                path = cached.Path,
                start_line = startLine,
                end_line = Math.Min(endLine, startLine + _options.MaxRangeLines - 1),
                truncated,
                content_hash = cached.ContentHash,
                content = text
            });
            return new ToolIrPlanItem(
                ToolIrPlanKind.LocalObservation,
                call,
                observation,
                null,
                null,
                null,
                null);
        }

        var capability = ToolIrMappingValidator.FindCapability(mapping, binding.PrimaryClientTool);
        var strategy = binding.Strategy;
        if (string.Equals(strategy, ToolIrStrategies.Direct, StringComparison.Ordinal) &&
            capability is not null &&
            (!capability.Supports.Offset || !capability.Supports.Limit))
        {
            strategy = ToolIrStrategies.ReadThenSlice;
        }

        var clientArgs = BuildNativeArgs(binding, args, strategy, startLine, endLine);
        var clientCallId = NewClientCallId(call.Id);
        var map = new ToolIrCallMapping(
            conversationId,
            call.Id,
            clientCallId,
            call.Name,
            binding.PrimaryClientTool,
            call.ArgumentsJson,
            clientArgs,
            strategy,
            path,
            startLine,
            endLine,
            Pending: true);

        return new ToolIrPlanItem(
            ToolIrPlanKind.NativeClientCall,
            call,
            null,
            clientCallId,
            binding.PrimaryClientTool,
            clientArgs,
            map);
    }

    private ToolIrPlanItem PlanFileManifest(
        Guid conversationId,
        ParsedToolCall call,
        ToolIrBinding binding,
        ToolIrMappingDocument mapping,
        Dictionary<string, JsonElement> args)
    {
        // Manifest is a file metadata read — never Glob/directory tools (path is a file).
        var effective = ResolveFileReadBinding(binding, mapping);
        if (effective is null)
        {
            return LocalError(
                call,
                "unbound_tool",
                "comprexy_read_file_manifest requires a FILE_READ_RAW (or FILE_METADATA) client tool; Glob/directory bindings are invalid.");
        }

        return PlanNativeOrError(conversationId, call, effective, mapping, args, requirePath: true);
    }

    /// <summary>
    /// Prefer the binding when its primary is a file reader; otherwise pick the first FILE_READ_RAW tool.
    /// </summary>
    private static ToolIrBinding? ResolveFileReadBinding(ToolIrBinding binding, ToolIrMappingDocument mapping)
    {
        var allowed = ToolIrCapabilities.AllowedForVirtualTool(ToolSchemaConstants.FileManifestToolName)!;
        var primaryCap = ToolIrMappingValidator.FindCapability(mapping, binding.PrimaryClientTool);
        if (primaryCap is not null && allowed.Contains(primaryCap.Capability))
        {
            return binding;
        }

        var reader = mapping.ClientCapabilities.FirstOrDefault(c =>
            allowed.Contains(c.Capability) && c.Supports.Path);
        reader ??= mapping.ClientCapabilities.FirstOrDefault(c => allowed.Contains(c.Capability));
        if (reader is null)
        {
            return null;
        }

        return new ToolIrBinding
        {
            ComprexyTool = ToolSchemaConstants.FileManifestToolName,
            PrimaryClientTool = reader.ClientTool,
            Strategy = ToolIrStrategies.Direct,
            ArgMap = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["path"] = GuessPathArgName(reader.ClientTool)
            }
        };
    }

    private static string GuessPathArgName(string clientTool) =>
        clientTool switch
        {
            "Read" => "path",
            _ => "path"
        };

    private ToolIrPlanItem PlanNativeOrError(
        Guid conversationId,
        ParsedToolCall call,
        ToolIrBinding binding,
        ToolIrMappingDocument mapping,
        Dictionary<string, JsonElement> args,
        bool requirePath)
    {
        if (requirePath && !TryGetString(args, "path", out _))
        {
            return LocalError(call, "invalid_args", $"{call.Name} requires path.");
        }

        if (string.Equals(call.Name, ToolSchemaConstants.FileSearchToolName, StringComparison.Ordinal) &&
            !TryGetString(args, "query", out _))
        {
            return LocalError(call, "invalid_args", "comprexy_read_file_search requires query.");
        }

        TryGetString(args, "path", out var path);
        TryGetInt(args, "start_line", out var startLine);
        TryGetInt(args, "end_line", out var endLine);

        // Manifest can be satisfied from cache without a native round-trip.
        if (string.Equals(call.Name, ToolSchemaConstants.FileManifestToolName, StringComparison.Ordinal) &&
            path is not null &&
            _fileCache.TryGet(conversationId, path, out var cached) &&
            cached is not null)
        {
            return new ToolIrPlanItem(
                ToolIrPlanKind.LocalObservation,
                call,
                ToolIrResultDistiller.BuildManifestFromCache(cached),
                null,
                null,
                null,
                null);
        }

        var clientArgs = BuildNativeArgs(binding, args, binding.Strategy, startLine, endLine);
        var clientCallId = NewClientCallId(call.Id);
        var map = new ToolIrCallMapping(
            conversationId,
            call.Id,
            clientCallId,
            call.Name,
            binding.PrimaryClientTool,
            call.ArgumentsJson,
            clientArgs,
            binding.Strategy,
            path,
            startLine > 0 ? startLine : null,
            endLine > 0 ? endLine : null,
            Pending: true);

        return new ToolIrPlanItem(
            ToolIrPlanKind.NativeClientCall,
            call,
            null,
            clientCallId,
            binding.PrimaryClientTool,
            clientArgs,
            map);
    }

    private static string BuildNativeArgs(
        ToolIrBinding binding,
        Dictionary<string, JsonElement> irArgs,
        string strategy,
        int startLine,
        int endLine)
    {
        var root = new JsonObject();
        var argMap = binding.ArgMap ?? new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (irName, value) in irArgs)
        {
            if (string.Equals(strategy, ToolIrStrategies.ReadThenSlice, StringComparison.Ordinal) &&
                (string.Equals(irName, "start_line", StringComparison.Ordinal) ||
                 string.Equals(irName, "end_line", StringComparison.Ordinal)))
            {
                continue;
            }

            var clientName = argMap.TryGetValue(irName, out var mapped) && !string.IsNullOrWhiteSpace(mapped)
                ? mapped
                : irName;
            root[clientName] = JsonNode.Parse(value.GetRawText());
        }

        if (string.Equals(strategy, ToolIrStrategies.Direct, StringComparison.Ordinal) &&
            startLine > 0 &&
            endLine >= startLine)
        {
            if (argMap.TryGetValue("start_line", out var offsetName) && !string.IsNullOrWhiteSpace(offsetName))
            {
                root[offsetName] = startLine;
            }
            else if (!root.ContainsKey("offset") && !root.ContainsKey("start_line"))
            {
                root["offset"] = startLine;
            }

            var limit = endLine - startLine + 1;
            if (argMap.TryGetValue("end_line", out var limitName) && !string.IsNullOrWhiteSpace(limitName) &&
                !string.Equals(limitName, offsetName, StringComparison.Ordinal))
            {
                // Prefer limit-style when arg_map end_line points at a distinct field.
                if (limitName.Contains("limit", StringComparison.OrdinalIgnoreCase) ||
                    limitName.Contains("count", StringComparison.OrdinalIgnoreCase))
                {
                    root[limitName] = limit;
                }
                else
                {
                    root[limitName] = endLine;
                }
            }
            else if (!root.ContainsKey("limit"))
            {
                root["limit"] = limit;
            }
        }

        // Ensure primary path argument exists under mapped name.
        if (TryGetString(irArgs, "path", out var path) &&
            argMap.TryGetValue("path", out var pathName) &&
            !string.IsNullOrWhiteSpace(pathName))
        {
            root[pathName] = path;
        }

        ApplyBindingDefaults(binding, root);

        return root.ToJsonString();
    }

    /// <summary>
    /// Fill missing client keys from binding <c>defaults</c>. IR-mapped values already in
    /// <paramref name="root"/> win.
    /// </summary>
    private static void ApplyBindingDefaults(ToolIrBinding binding, JsonObject root)
    {
        if (binding.Defaults is null || binding.Defaults.Count == 0)
        {
            return;
        }

        foreach (var (clientKey, literal) in binding.Defaults)
        {
            if (string.IsNullOrWhiteSpace(clientKey) || root.ContainsKey(clientKey))
            {
                continue;
            }

            try
            {
                root[clientKey] = JsonNode.Parse(literal.GetRawText());
            }
            catch (JsonException)
            {
                // Skip unparseable default literals; outbound schema validation will reject if required.
            }
        }
    }

    private static ToolIrPlanItem LocalError(ParsedToolCall call, string code, string message) =>
        new(
            ToolIrPlanKind.LocalObservation,
            call,
            BuildErrorObservation(code, message),
            null,
            null,
            null,
            null);

    private static string BuildErrorObservation(string code, string details) =>
        JsonSerializer.Serialize(new { error = details, code, details });

    private static string NewClientCallId(string _) =>
        $"cur_{Guid.NewGuid():N}";

    private static Dictionary<string, JsonElement> ParseArgs(string argumentsJson)
    {
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return result;
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                result[property.Name] = property.Value.Clone();
            }
        }
        catch (JsonException)
        {
            // ignore
        }

        return result;
    }

    private static bool TryGetString(Dictionary<string, JsonElement> args, string name, out string? value)
    {
        value = null;
        if (!args.TryGetValue(name, out var element) || element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = element.GetString();
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryGetInt(Dictionary<string, JsonElement> args, string name, out int value)
    {
        value = 0;
        if (!args.TryGetValue(name, out var element))
        {
            return false;
        }

        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out value))
        {
            return true;
        }

        if (element.ValueKind == JsonValueKind.String &&
            int.TryParse(element.GetString(), out value))
        {
            return true;
        }

        return false;
    }
}
