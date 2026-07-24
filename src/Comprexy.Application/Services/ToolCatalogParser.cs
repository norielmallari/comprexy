using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Comprexy.Application.Services;

public sealed record CompactToolEntry(string Name, string Description, IReadOnlyList<string> Required);

public sealed record ParsedToolCatalog(
    IReadOnlyList<CompactToolEntry> CompactEntries,
    IReadOnlyDictionary<string, string> FullDefinitionsByName,
    string CatalogHash,
    string NormalizedToolsJson,
    bool HasMetaToolNameCollision);

/// <summary>
/// Parses OpenAI-compatible tools/functions, derives compact index rows, and computes catalog hash.
/// </summary>
public class ToolCatalogParser
{
    private static readonly JsonSerializerOptions CanonicalOptions = new()
    {
        WriteIndented = false
    };

    public ParsedToolCatalog? TryParse(JsonElement? requestRoot)
    {
        if (requestRoot is not { ValueKind: JsonValueKind.Object } root)
        {
            return null;
        }

        var toolsArray = ExtractToolsArray(root);
        if (toolsArray is null || toolsArray.Count == 0)
        {
            return null;
        }

        var compactEntries = new List<CompactToolEntry>();
        var definitions = new Dictionary<string, string>(StringComparer.Ordinal);
        var hasCollision = false;

        foreach (var toolNode in toolsArray)
        {
            if (toolNode is null || !TryParseTool(toolNode, out var name, out var description, out var required, out var fullDefinitionJson))
            {
                continue;
            }

            if (string.Equals(name, ToolSchemaConstants.MetaToolName, StringComparison.Ordinal))
            {
                hasCollision = true;
            }

            compactEntries.Add(new CompactToolEntry(name, description, required));
            definitions[name] = fullDefinitionJson;
        }

        if (compactEntries.Count == 0)
        {
            return null;
        }

        var normalizedJson = toolsArray.ToJsonString(CanonicalOptions);
        var hash = ComputeSha256Hex(normalizedJson);

        return new ParsedToolCatalog(
            compactEntries,
            definitions,
            hash,
            normalizedJson,
            hasCollision);
    }

    public string BuildCompactIndexJson(IReadOnlyList<CompactToolEntry> entries)
    {
        var array = new JsonArray();
        foreach (var entry in entries.OrderBy(e => e.Name, StringComparer.Ordinal))
        {
            var required = new JsonArray();
            foreach (var field in entry.Required)
            {
                required.Add(field);
            }

            array.Add(new JsonObject
            {
                ["name"] = entry.Name,
                ["description"] = entry.Description,
                ["required"] = required
            });
        }

        return array.ToJsonString(CanonicalOptions);
    }

    internal static string ComputeSha256Hex(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static JsonArray? ExtractToolsArray(JsonElement root)
    {
        if (root.TryGetProperty("tools", out var tools) && tools.ValueKind == JsonValueKind.Array)
        {
            return JsonNode.Parse(tools.GetRawText()) as JsonArray;
        }

        if (root.TryGetProperty("functions", out var functions) && functions.ValueKind == JsonValueKind.Array)
        {
            var converted = new JsonArray();
            foreach (var function in functions.EnumerateArray())
            {
                converted.Add(new JsonObject
                {
                    ["type"] = "function",
                    ["function"] = JsonNode.Parse(function.GetRawText())
                });
            }

            return converted;
        }

        return null;
    }

    private static bool TryParseTool(
        JsonNode toolNode,
        out string name,
        out string description,
        out IReadOnlyList<string> required,
        out string fullDefinitionJson)
    {
        name = string.Empty;
        description = string.Empty;
        required = [];
        fullDefinitionJson = toolNode.ToJsonString(CanonicalOptions);

        if (toolNode is not JsonObject toolObject)
        {
            return false;
        }

        JsonObject? functionObject = null;
        if (toolObject.TryGetPropertyValue("function", out var functionNode) && functionNode is JsonObject fn)
        {
            functionObject = fn;
        }
        else if (toolObject.TryGetPropertyValue("name", out _))
        {
            functionObject = toolObject;
        }

        if (functionObject is null ||
            !functionObject.TryGetPropertyValue("name", out var nameNode) ||
            nameNode is not JsonValue nameValue ||
            !nameValue.TryGetValue<string>(out var parsedName) ||
            string.IsNullOrWhiteSpace(parsedName))
        {
            return false;
        }

        name = parsedName.Trim();
        description = functionObject.TryGetPropertyValue("description", out var descriptionNode) &&
                      descriptionNode is JsonValue descriptionValue &&
                      descriptionValue.TryGetValue<string>(out var parsedDescription) &&
                      !string.IsNullOrWhiteSpace(parsedDescription)
            ? parsedDescription.Trim()
            : $"Tool {name}.";

        required = ExtractRequired(functionObject);
        return true;
    }

    private static IReadOnlyList<string> ExtractRequired(JsonObject functionObject)
    {
        if (!functionObject.TryGetPropertyValue("parameters", out var parametersNode) ||
            parametersNode is not JsonObject parameters ||
            !parameters.TryGetPropertyValue("required", out var requiredNode) ||
            requiredNode is not JsonArray requiredArray)
        {
            return [];
        }

        var required = new List<string>();
        foreach (var item in requiredArray)
        {
            if (item is JsonValue value &&
                value.TryGetValue<string>(out var field) &&
                !string.IsNullOrWhiteSpace(field))
            {
                required.Add(field.Trim());
            }
        }

        return required;
    }
}
