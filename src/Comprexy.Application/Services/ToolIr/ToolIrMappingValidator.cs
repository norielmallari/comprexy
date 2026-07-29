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

    public sealed record ValidationResult(bool IsValid, ToolIrMappingDocument? Document, string? Error);

    public static ValidationResult Validate(
        string mappingJson,
        IReadOnlySet<string> catalogToolNames,
        string expectedSchemaHash,
        IReadOnlyDictionary<string, string>? fullDefinitionsByName = null)
    {
        if (string.IsNullOrWhiteSpace(mappingJson))
        {
            return new ValidationResult(false, null, "MappingJson is empty.");
        }

        ToolIrMappingDocument document;
        try
        {
            document = JsonSerializer.Deserialize<ToolIrMappingDocument>(mappingJson, JsonOptions)
                ?? throw new JsonException("null document");
        }
        catch (JsonException ex)
        {
            return new ValidationResult(false, null, $"MappingJson is not valid JSON: {ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(document.SchemaHash))
        {
            return new ValidationResult(false, null, "schema_hash is required.");
        }

        if (!string.Equals(document.SchemaHash, expectedSchemaHash, StringComparison.Ordinal))
        {
            return new ValidationResult(
                false,
                null,
                $"schema_hash mismatch: mapping={document.SchemaHash}, expected={expectedSchemaHash}.");
        }

        if (document.ClientCapabilities.Count == 0)
        {
            return new ValidationResult(false, null, "client_capabilities must be non-empty.");
        }

        var capabilityTools = new HashSet<string>(StringComparer.Ordinal);
        foreach (var capability in document.ClientCapabilities)
        {
            if (string.IsNullOrWhiteSpace(capability.ClientTool))
            {
                return new ValidationResult(false, null, "client_capabilities[].client_tool is required.");
            }

            if (!catalogToolNames.Contains(capability.ClientTool))
            {
                return new ValidationResult(
                    false,
                    null,
                    $"Unknown client_tool '{capability.ClientTool}' not in inbound catalog.");
            }

            if (!capabilityTools.Add(capability.ClientTool))
            {
                return new ValidationResult(
                    false,
                    null,
                    $"Duplicate client_capabilities entry for '{capability.ClientTool}'.");
            }

            if (string.IsNullOrWhiteSpace(capability.Capability) ||
                !ToolIrCapabilities.Allowed.Contains(capability.Capability))
            {
                return new ValidationResult(
                    false,
                    null,
                    $"Unknown or missing capability '{capability.Capability}' for '{capability.ClientTool}'.");
            }
        }

        foreach (var catalogTool in catalogToolNames)
        {
            if (!capabilityTools.Contains(catalogTool))
            {
                return new ValidationResult(
                    false,
                    null,
                    $"Missing client_capabilities entry for catalog tool '{catalogTool}'.");
            }
        }

        var boundComprexy = new HashSet<string>(StringComparer.Ordinal);
        foreach (var binding in document.Bindings)
        {
            if (string.IsNullOrWhiteSpace(binding.ComprexyTool) ||
                !ToolSchemaConstants.IsVirtualTool(binding.ComprexyTool))
            {
                return new ValidationResult(
                    false,
                    null,
                    $"Unknown comprexy_tool '{binding.ComprexyTool}'.");
            }

            if (!boundComprexy.Add(binding.ComprexyTool))
            {
                return new ValidationResult(
                    false,
                    null,
                    $"Duplicate binding for '{binding.ComprexyTool}'.");
            }

            if (string.IsNullOrWhiteSpace(binding.PrimaryClientTool) ||
                !catalogToolNames.Contains(binding.PrimaryClientTool))
            {
                return new ValidationResult(
                    false,
                    null,
                    $"Binding '{binding.ComprexyTool}' references unknown primary_client_tool '{binding.PrimaryClientTool}'.");
            }

            if (string.IsNullOrWhiteSpace(binding.Strategy) ||
                !ToolIrStrategies.Allowed.Contains(binding.Strategy))
            {
                return new ValidationResult(
                    false,
                    null,
                    $"Unknown strategy '{binding.Strategy}' for '{binding.ComprexyTool}'.");
            }

            if (VirtualToolRegistry.TryGet(binding.ComprexyTool, out var virtualSpec) &&
                string.Equals(virtualSpec.Family, VirtualToolFamilies.Shell, StringComparison.Ordinal) &&
                !string.Equals(binding.Strategy, ToolIrStrategies.Direct, StringComparison.Ordinal))
            {
                return new ValidationResult(
                    false,
                    null,
                    $"Binding '{binding.ComprexyTool}' requires strategy 'direct' (got '{binding.Strategy}').");
            }

            var primaryCapability = document.ClientCapabilities.FirstOrDefault(c =>
                string.Equals(c.ClientTool, binding.PrimaryClientTool, StringComparison.Ordinal));
            var allowedCaps = ToolIrCapabilities.AllowedForVirtualTool(binding.ComprexyTool);
            if (allowedCaps is not null &&
                (primaryCapability is null || !allowedCaps.Contains(primaryCapability.Capability)))
            {
                return new ValidationResult(
                    false,
                    null,
                    $"Binding '{binding.ComprexyTool}' primary_client_tool '{binding.PrimaryClientTool}' " +
                    $"has capability '{primaryCapability?.Capability ?? "(missing)"}'; expected one of: {string.Join(", ", allowedCaps)}.");
            }

            if (fullDefinitionsByName is not null)
            {
                if (!fullDefinitionsByName.TryGetValue(binding.PrimaryClientTool, out var definitionJson) ||
                    string.IsNullOrWhiteSpace(definitionJson))
                {
                    return new ValidationResult(
                        false,
                        null,
                        $"Binding '{binding.ComprexyTool}' primary_client_tool '{binding.PrimaryClientTool}' " +
                        "has no catalog definition for schema-required coverage validation.");
                }

                var coverageError = ValidateSchemaRequiredCoverage(binding, definitionJson);
                if (coverageError is not null)
                {
                    return new ValidationResult(false, null, coverageError);
                }
            }
        }

        return new ValidationResult(true, document, null);
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
