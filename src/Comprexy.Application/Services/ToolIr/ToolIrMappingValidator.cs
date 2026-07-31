using System.Text.Json;
using System.Text.Json.Nodes;
using Comprexy.Application.Services.ToolIr;

namespace Comprexy.Application.Services;

/// <summary>
/// Validates closed MappingJson shape. Rejects unknown strategies, unknown client tools,
/// bindings for tools not in the inbound catalog, and bindings that leave client-required
/// parameters uncovered by <c>arg_map</c> / <c>defaults</c>.
/// </summary>
public static class ToolIrMappingValidator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public sealed record ValidationResult(
        bool IsValid,
        ToolIrMappingDocument? Document,
        string? Error,
        IReadOnlyList<string>? DroppedBindings = null);

    /// <summary>One rejected binding, kept with its index so salvage can drop exactly that entry.</summary>
    private sealed record BindingIssue(int Index, string ComprexyTool, string Message);

    /// <summary>
    /// Document errors reject the whole mapping; binding issues reject only their own entry.
    /// <c>Document</c> is null only when the payload could not be parsed.
    /// </summary>
    private sealed record Analysis(
        ToolIrMappingDocument? Document,
        List<string> DocumentErrors,
        List<BindingIssue> BindingIssues);

    public static ValidationResult Validate(
        string mappingJson,
        IReadOnlySet<string> catalogToolNames,
        string expectedSchemaHash,
        IReadOnlyDictionary<string, string>? fullDefinitionsByName = null)
    {
        var analysis = Analyze(mappingJson, catalogToolNames, expectedSchemaHash, fullDefinitionsByName);
        var errors = analysis.DocumentErrors
            .Concat(analysis.BindingIssues.Select(issue => issue.Message))
            .ToList();

        if (errors.Count > 0 || analysis.Document is null)
        {
            return new ValidationResult(false, null, string.Join("\n", errors));
        }

        return new ValidationResult(true, analysis.Document, null);
    }

    /// <summary>
    /// Last-resort recovery once mapper retries are exhausted: keep the bindings that validate and
    /// drop the ones that do not, so a single unbindable Virtual tool does not disable Tool IR for
    /// the conversation. Refuses to salvage document-level failures, an empty binding set, or a drop
    /// that would leave replaced client tools hidden with no Virtual replacement.
    /// </summary>
    public static ValidationResult TrySalvage(
        string mappingJson,
        IReadOnlySet<string> catalogToolNames,
        string expectedSchemaHash,
        IReadOnlyDictionary<string, string>? fullDefinitionsByName = null)
    {
        var analysis = Analyze(mappingJson, catalogToolNames, expectedSchemaHash, fullDefinitionsByName);
        if (analysis.Document is null || analysis.DocumentErrors.Count > 0)
        {
            var errors = analysis.DocumentErrors
                .Concat(analysis.BindingIssues.Select(issue => issue.Message))
                .ToList();
            return new ValidationResult(false, null, string.Join("\n", errors));
        }

        if (analysis.BindingIssues.Count == 0)
        {
            return new ValidationResult(true, analysis.Document, null);
        }

        var document = analysis.Document;
        var droppedIndexes = analysis.BindingIssues.Select(issue => issue.Index).ToHashSet();
        var kept = document.Bindings
            .Where((_, index) => !droppedIndexes.Contains(index))
            .ToList();
        var dropped = analysis.BindingIssues
            .Select(issue => string.IsNullOrWhiteSpace(issue.ComprexyTool) ? "(unnamed)" : issue.ComprexyTool)
            .ToList();

        if (kept.Count == 0)
        {
            return new ValidationResult(
                false,
                null,
                $"No binding survived validation (dropped: {string.Join(", ", dropped)}).");
        }

        var uncovered = FindUncoveredReplacedCapabilities(document, kept);
        if (uncovered.Count > 0)
        {
            return new ValidationResult(
                false,
                null,
                $"Dropping invalid bindings ({string.Join(", ", dropped)}) would hide client tools with " +
                $"capability {string.Join(", ", uncovered)} and no Virtual replacement.");
        }

        document.Bindings = kept;
        return new ValidationResult(true, document, null, dropped);
    }

    private static Analysis Analyze(
        string mappingJson,
        IReadOnlySet<string> catalogToolNames,
        string expectedSchemaHash,
        IReadOnlyDictionary<string, string>? fullDefinitionsByName)
    {
        var documentErrors = new List<string>();
        var bindingIssues = new List<BindingIssue>();

        if (string.IsNullOrWhiteSpace(mappingJson))
        {
            documentErrors.Add("MappingJson is empty.");
            return new Analysis(null, documentErrors, bindingIssues);
        }

        ToolIrMappingDocument document;
        try
        {
            document = JsonSerializer.Deserialize<ToolIrMappingDocument>(mappingJson, JsonOptions)
                ?? throw new JsonException("null document");
        }
        catch (JsonException ex)
        {
            documentErrors.Add($"MappingJson is not valid JSON: {ex.Message}");
            return new Analysis(null, documentErrors, bindingIssues);
        }

        if (string.IsNullOrWhiteSpace(document.SchemaHash))
        {
            documentErrors.Add("schema_hash is required.");
        }
        else if (!string.Equals(document.SchemaHash, expectedSchemaHash, StringComparison.Ordinal))
        {
            documentErrors.Add(
                $"schema_hash mismatch: mapping={document.SchemaHash}, expected={expectedSchemaHash}.");
        }

        if (document.ClientCapabilities.Count == 0)
        {
            documentErrors.Add("client_capabilities must be non-empty.");
        }

        var capabilityTools = new HashSet<string>(StringComparer.Ordinal);
        foreach (var capability in document.ClientCapabilities)
        {
            if (string.IsNullOrWhiteSpace(capability.ClientTool))
            {
                documentErrors.Add("client_capabilities[].client_tool is required.");
                continue;
            }

            if (!catalogToolNames.Contains(capability.ClientTool))
            {
                documentErrors.Add($"Unknown client_tool '{capability.ClientTool}' not in inbound catalog.");
                continue;
            }

            if (!capabilityTools.Add(capability.ClientTool))
            {
                documentErrors.Add($"Duplicate client_capabilities entry for '{capability.ClientTool}'.");
            }

            if (string.IsNullOrWhiteSpace(capability.Capability) ||
                !ToolIrCapabilities.Allowed.Contains(capability.Capability))
            {
                documentErrors.Add(
                    $"Unknown or missing capability '{capability.Capability}' for '{capability.ClientTool}'.");
            }
        }

        foreach (var catalogTool in catalogToolNames.OrderBy(name => name, StringComparer.Ordinal))
        {
            if (!capabilityTools.Contains(catalogTool))
            {
                documentErrors.Add($"Missing client_capabilities entry for catalog tool '{catalogTool}'.");
            }
        }

        var boundComprexy = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < document.Bindings.Count; index++)
        {
            var binding = document.Bindings[index];
            var issue = FindBindingIssue(
                binding,
                document,
                catalogToolNames,
                fullDefinitionsByName,
                boundComprexy);
            if (issue is not null)
            {
                bindingIssues.Add(new BindingIssue(index, binding.ComprexyTool, issue));
            }
        }

        return new Analysis(document, documentErrors, bindingIssues);
    }

    private static string? FindBindingIssue(
        ToolIrBinding binding,
        ToolIrMappingDocument document,
        IReadOnlySet<string> catalogToolNames,
        IReadOnlyDictionary<string, string>? fullDefinitionsByName,
        HashSet<string> boundComprexy)
    {
        if (string.IsNullOrWhiteSpace(binding.ComprexyTool) ||
            !ToolSchemaConstants.IsVirtualTool(binding.ComprexyTool))
        {
            return $"Unknown comprexy_tool '{binding.ComprexyTool}'.";
        }

        if (!boundComprexy.Add(binding.ComprexyTool))
        {
            return $"Duplicate binding for '{binding.ComprexyTool}'.";
        }

        if (string.IsNullOrWhiteSpace(binding.PrimaryClientTool) ||
            !catalogToolNames.Contains(binding.PrimaryClientTool))
        {
            return $"Binding '{binding.ComprexyTool}' references unknown primary_client_tool '{binding.PrimaryClientTool}'.";
        }

        if (string.IsNullOrWhiteSpace(binding.Strategy) ||
            !ToolIrStrategies.Allowed.Contains(binding.Strategy))
        {
            return $"Unknown strategy '{binding.Strategy}' for '{binding.ComprexyTool}'.";
        }

        if (VirtualToolRegistry.TryGet(binding.ComprexyTool, out var virtualSpec) &&
            string.Equals(virtualSpec.Family, VirtualToolFamilies.Shell, StringComparison.Ordinal) &&
            !string.Equals(binding.Strategy, ToolIrStrategies.Direct, StringComparison.Ordinal))
        {
            return $"Binding '{binding.ComprexyTool}' requires strategy 'direct' (got '{binding.Strategy}').";
        }

        var primaryCapability = FindCapability(document, binding.PrimaryClientTool);
        var allowedCaps = ToolIrCapabilities.AllowedForVirtualTool(binding.ComprexyTool);
        if (allowedCaps is not null &&
            (primaryCapability is null || !allowedCaps.Contains(primaryCapability.Capability)))
        {
            return $"Binding '{binding.ComprexyTool}' primary_client_tool '{binding.PrimaryClientTool}' " +
                   $"has capability '{primaryCapability?.Capability ?? "(missing)"}'; " +
                   $"expected one of: {string.Join(", ", allowedCaps)}. " +
                   DescribeRebindCandidates(document, allowedCaps);
        }

        if (fullDefinitionsByName is null)
        {
            return null;
        }

        if (!fullDefinitionsByName.TryGetValue(binding.PrimaryClientTool, out var definitionJson) ||
            string.IsNullOrWhiteSpace(definitionJson))
        {
            return $"Binding '{binding.ComprexyTool}' primary_client_tool '{binding.PrimaryClientTool}' " +
                   "has no catalog definition for schema-required coverage validation.";
        }

        return ValidateSchemaRequiredCoverage(binding, definitionJson);
    }

    /// <summary>
    /// Names the client tools that would satisfy the binding so a retry is a lookup, not a guess.
    /// </summary>
    private static string DescribeRebindCandidates(
        ToolIrMappingDocument document,
        IReadOnlySet<string> allowedCaps)
    {
        var candidates = document.ClientCapabilities
            .Where(capability => allowedCaps.Contains(capability.Capability))
            .Select(capability => $"{capability.ClientTool} ({capability.Capability})")
            .ToList();

        return candidates.Count > 0
            ? $"Rebind to one of: {string.Join(", ", candidates)}."
            : "No client tool in client_capabilities has a compatible capability — omit this binding.";
    }

    /// <summary>
    /// Replaced capabilities present in the catalog that no surviving binding covers. Their client
    /// tools are hidden from the model, so leaving one uncovered strands that capability.
    /// </summary>
    private static List<string> FindUncoveredReplacedCapabilities(
        ToolIrMappingDocument document,
        IReadOnlyList<ToolIrBinding> keptBindings)
    {
        var covered = keptBindings
            .Select(binding => FindCapability(document, binding.PrimaryClientTool)?.Capability)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

        return document.ClientCapabilities
            .Select(capability => capability.Capability)
            .Where(capability =>
                ToolIrCapabilities.ReplacedByVirtualTools.Contains(capability) &&
                !covered.Contains(capability))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(capability => capability, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Ensures every client-schema <c>required</c> property is covered by <c>arg_map</c> values
    /// or <c>defaults</c> keys. Rejects unknown keys when <c>additionalProperties: false</c>.
    /// </summary>
    public static string? ValidateSchemaRequiredCoverage(ToolIrBinding binding, string fullDefinitionJson)
    {
        var parametersJson = ExtractParametersSchemaJson(fullDefinitionJson);
        if (string.IsNullOrWhiteSpace(parametersJson))
        {
            return null;
        }

        JsonObject? schema;
        try
        {
            schema = JsonNode.Parse(parametersJson) as JsonObject;
        }
        catch (JsonException ex)
        {
            return $"Binding '{binding.ComprexyTool}' primary_client_tool '{binding.PrimaryClientTool}' " +
                   $"has unparseable parameters schema: {ex.Message}";
        }

        if (schema is null)
        {
            return null;
        }

        var propertyNames = new HashSet<string>(StringComparer.Ordinal);
        if (schema.TryGetPropertyValue("properties", out var propertiesNode) &&
            propertiesNode is JsonObject properties)
        {
            foreach (var property in properties)
            {
                propertyNames.Add(property.Key);
            }
        }

        var covered = new HashSet<string>(StringComparer.Ordinal);
        if (binding.ArgMap is not null)
        {
            foreach (var clientName in binding.ArgMap.Values)
            {
                if (!string.IsNullOrWhiteSpace(clientName))
                {
                    covered.Add(clientName);
                }
            }
        }

        if (binding.Defaults is not null)
        {
            foreach (var key in binding.Defaults.Keys)
            {
                if (!string.IsNullOrWhiteSpace(key))
                {
                    covered.Add(key);
                }
            }
        }

        var missing = new List<string>();
        if (schema.TryGetPropertyValue("required", out var requiredNode) &&
            requiredNode is JsonArray requiredArray)
        {
            foreach (var entry in requiredArray)
            {
                if (entry is not JsonValue value ||
                    !value.TryGetValue<string>(out var requiredName) ||
                    string.IsNullOrWhiteSpace(requiredName))
                {
                    continue;
                }

                if (!covered.Contains(requiredName))
                {
                    missing.Add(requiredName);
                }
            }
        }

        if (missing.Count > 0)
        {
            return $"Binding '{binding.ComprexyTool}' primary_client_tool '{binding.PrimaryClientTool}' " +
                   $"leaves required client parameters uncovered by arg_map/defaults: {string.Join(", ", missing)}. " +
                   $"Add arg_map entries or defaults for those client fields. Schema snippet: {Truncate(parametersJson, 400)}";
        }

        var forbidsAdditional = schema.TryGetPropertyValue("additionalProperties", out var additionalNode) &&
                                additionalNode is JsonValue additionalValue &&
                                additionalValue.TryGetValue<bool>(out var allowAdditional) &&
                                !allowAdditional;

        if (forbidsAdditional && propertyNames.Count > 0)
        {
            var unknown = new List<string>();
            if (binding.ArgMap is not null)
            {
                foreach (var clientName in binding.ArgMap.Values)
                {
                    if (!string.IsNullOrWhiteSpace(clientName) && !propertyNames.Contains(clientName))
                    {
                        unknown.Add($"arg_map→{clientName}");
                    }
                }
            }

            if (binding.Defaults is not null)
            {
                foreach (var key in binding.Defaults.Keys)
                {
                    if (!string.IsNullOrWhiteSpace(key) && !propertyNames.Contains(key))
                    {
                        unknown.Add($"defaults.{key}");
                    }
                }
            }

            if (unknown.Count > 0)
            {
                return $"Binding '{binding.ComprexyTool}' primary_client_tool '{binding.PrimaryClientTool}' " +
                       $"references unknown client parameters (additionalProperties=false): {string.Join(", ", unknown)}.";
            }
        }

        return null;
    }

    public static string? ExtractParametersSchemaJson(string fullDefinitionJson)
    {
        if (string.IsNullOrWhiteSpace(fullDefinitionJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(fullDefinitionJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (root.TryGetProperty("function", out var function) &&
                function.ValueKind == JsonValueKind.Object &&
                function.TryGetProperty("parameters", out var nestedParameters))
            {
                return nestedParameters.GetRawText();
            }

            if (root.TryGetProperty("parameters", out var parameters))
            {
                return parameters.GetRawText();
            }
        }
        catch (JsonException)
        {
            // ignore
        }

        return null;
    }

    /// <summary>
    /// Client tool names replaced by Virtual IR (capabilities in
    /// <see cref="ToolIrCapabilities.ReplacedByVirtualTools"/> plus binding primaries).
    /// Hidden from the model-facing catalog.
    /// </summary>
    public static IReadOnlySet<string> GetReplacedClientToolNames(ToolIrMappingDocument document)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var capability in document.ClientCapabilities)
        {
            if (ToolIrCapabilities.ReplacedByVirtualTools.Contains(capability.Capability))
            {
                names.Add(capability.ClientTool);
            }
        }

        foreach (var binding in document.Bindings)
        {
            names.Add(binding.PrimaryClientTool);
        }

        return names;
    }

    /// <summary>Obsolete name — use <see cref="GetReplacedClientToolNames"/>.</summary>
    public static IReadOnlySet<string> GetFileClientToolNames(ToolIrMappingDocument document) =>
        GetReplacedClientToolNames(document);

    public static ToolIrBinding? FindBinding(ToolIrMappingDocument document, string comprexyTool) =>
        document.Bindings.FirstOrDefault(b =>
            string.Equals(b.ComprexyTool, comprexyTool, StringComparison.Ordinal));

    public static ToolIrClientCapability? FindCapability(ToolIrMappingDocument document, string clientTool) =>
        document.ClientCapabilities.FirstOrDefault(c =>
            string.Equals(c.ClientTool, clientTool, StringComparison.Ordinal));

    private static string Truncate(string value, int maxChars) =>
        value.Length <= maxChars ? value : value[..maxChars] + "…";
}
