using System.Text.Json;
using Json.Schema;

namespace Comprexy.Application.Services;

public sealed record ToolArgumentValidationResult(bool IsValid, string? ErrorCode, string? Details);

/// <summary>
/// Validates tool call arguments against a tool's parameters JSON Schema. Fail closed.
/// </summary>
public class ToolArgumentValidator
{
    public ToolArgumentValidationResult Validate(string? parametersSchemaJson, string argumentsJson)
    {
        if (string.IsNullOrWhiteSpace(parametersSchemaJson))
        {
            return new ToolArgumentValidationResult(true, null, null);
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
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
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
            return new ToolArgumentValidationResult(true, null, null);
        }

        var details = CollectErrorMessages(evaluation);
        return new ToolArgumentValidationResult(
            false,
            "schema_invalid",
            details.Count > 0
                ? string.Join("; ", details)
                : "Arguments failed JSON Schema validation.");
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
