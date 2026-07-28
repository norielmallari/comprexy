using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

namespace Comprexy.Application.Services;

public sealed record ToolArgumentValidationResult(
    bool IsValid,
    string? ErrorCode,
    string? Details,
    string? NormalizedArgumentsJson = null);

/// <summary>
/// Validates tool call arguments against a tool's parameters JSON Schema. Fail closed.
/// Coerces JSON-stringified object/array property values when the schema expects object/array
/// (common model mistake for nested fields like CallMcpTool.arguments).
/// </summary>
public class ToolArgumentValidator
{
    public ToolArgumentValidationResult Validate(string? parametersSchemaJson, string argumentsJson)
    {
        JsonNode? argsNode;
        try
        {
            argsNode = JsonNode.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
        }
        catch (JsonException ex)
        {
            return new ToolArgumentValidationResult(
                false,
                "invalid_args",
                $"Arguments are not valid JSON: {ex.Message}");
        }

        if (argsNode is null)
        {
            argsNode = new JsonObject();
        }

        if (!string.IsNullOrWhiteSpace(parametersSchemaJson) && argsNode is JsonObject argsObject)
        {
            try
            {
                var schemaNode = JsonNode.Parse(parametersSchemaJson);
                CoerceStringifiedContainers(argsObject, schemaNode);
            }
            catch (JsonException)
            {
                // Keep raw args; schema parse failure is handled below.
            }
        }

        var normalizedJson = argsNode.ToJsonString();

        if (string.IsNullOrWhiteSpace(parametersSchemaJson))
        {
            return new ToolArgumentValidationResult(true, null, null, normalizedJson);
        }

        JsonSchema schema;
        try
        {
            schema = JsonSchema.FromText(parametersSchemaJson);
        }
        catch (Exception ex)
        {
            return new ToolArgumentValidationResult(
                false,
                "schema_invalid",
                $"Unable to parse parameters schema: {ex.Message}");
        }

        JsonElement argumentsElement;
        try
        {
            using var document = JsonDocument.Parse(normalizedJson);
            argumentsElement = document.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            return new ToolArgumentValidationResult(
                false,
                "invalid_args",
                $"Arguments are not valid JSON: {ex.Message}");
        }

        var evaluation = schema.Evaluate(argumentsElement, new EvaluationOptions
        {
            OutputFormat = OutputFormat.List
        });

        if (evaluation.IsValid)
        {
            return new ToolArgumentValidationResult(true, null, null, normalizedJson);
        }

        var details = CollectErrorMessages(evaluation);
        return new ToolArgumentValidationResult(
            false,
            "schema_invalid",
            details.Count > 0
                ? string.Join("; ", details)
                : "Arguments failed JSON Schema validation.",
            normalizedJson);
    }

    public string? ExtractParametersSchemaJson(string fullDefinitionJson)
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
    /// When a schema property expects object/array but the model passed a JSON string of that
    /// shape, replace the string with the parsed value so validation and clients see a real object.
    /// </summary>
    internal static void CoerceStringifiedContainers(JsonObject args, JsonNode? schemaNode)
    {
        if (schemaNode is not JsonObject schema)
        {
            return;
        }

        if (!schema.TryGetPropertyValue("properties", out var propertiesNode) ||
            propertiesNode is not JsonObject properties)
        {
            return;
        }

        foreach (var property in properties)
        {
            if (!args.TryGetPropertyValue(property.Key, out var value) || value is null)
            {
                continue;
            }

            var expected = ResolvePrimaryContainerType(property.Value);
            if (expected is null)
            {
                continue;
            }

            if (value is not JsonValue jsonValue ||
                !jsonValue.TryGetValue<string>(out var raw) ||
                string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            JsonNode? parsed;
            try
            {
                parsed = JsonNode.Parse(raw);
            }
            catch (JsonException)
            {
                continue;
            }

            if (expected == "object" && parsed is JsonObject)
            {
                args[property.Key] = parsed;
            }
            else if (expected == "array" && parsed is JsonArray)
            {
                args[property.Key] = parsed;
            }
        }
    }

    private static string? ResolvePrimaryContainerType(JsonNode? propertySchema)
    {
        if (propertySchema is not JsonObject schema)
        {
            return null;
        }

        if (!schema.TryGetPropertyValue("type", out var typeNode) || typeNode is null)
        {
            return null;
        }

        if (typeNode is JsonValue typeValue && typeValue.TryGetValue<string>(out var single))
        {
            return single is "object" or "array" ? single : null;
        }

        if (typeNode is JsonArray typeArray)
        {
            foreach (var entry in typeArray)
            {
                if (entry is JsonValue entryValue &&
                    entryValue.TryGetValue<string>(out var name) &&
                    name is "object" or "array")
                {
                    return name;
                }
            }
        }

        return null;
    }

    private static List<string> CollectErrorMessages(EvaluationResults node)
    {
        var errors = new List<string>();
        if (node.Errors is not null)
        {
            foreach (var pair in node.Errors)
            {
                if (!string.IsNullOrWhiteSpace(pair.Value))
                {
                    errors.Add(pair.Value);
                }
            }
        }

        if (node.Details is not null)
        {
            foreach (var child in node.Details)
            {
                errors.AddRange(CollectErrorMessages(child));
            }
        }

        return errors;
    }
}
