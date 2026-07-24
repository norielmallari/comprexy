using System.Text.Json;
using Comprexy.Application.Models;

namespace Comprexy.Application.Services;

public sealed record ParsedToolCall(
    string Id,
    string Name,
    string ArgumentsJson);

/// <summary>
/// Parses OpenAI assistant tool_calls from wire JSON.
/// </summary>
public static class ToolCallWireHelper
{
    public static IReadOnlyList<ParsedToolCall> ParseAssistantToolCalls(string? assistantMessageJson)
    {
        if (string.IsNullOrWhiteSpace(assistantMessageJson))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(assistantMessageJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("tool_calls", out var toolCalls) ||
                toolCalls.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var parsed = new List<ParsedToolCall>();
            foreach (var call in toolCalls.EnumerateArray())
            {
                if (call.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (!call.TryGetProperty("id", out var idElement) ||
                    idElement.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var id = idElement.GetString();
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                string? name = null;
                string arguments = "{}";

                if (call.TryGetProperty("function", out var function) && function.ValueKind == JsonValueKind.Object)
                {
                    if (function.TryGetProperty("name", out var nameElement) &&
                        nameElement.ValueKind == JsonValueKind.String)
                    {
                        name = nameElement.GetString();
                    }

                    if (function.TryGetProperty("arguments", out var argumentsElement))
                    {
                        arguments = argumentsElement.ValueKind == JsonValueKind.String
                            ? argumentsElement.GetString() ?? "{}"
                            : argumentsElement.GetRawText();
                    }
                }

                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                parsed.Add(new ParsedToolCall(id.Trim(), name.Trim(), arguments));
            }

            return parsed;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static bool HasToolCalls(string? assistantMessageJson) =>
        ParseAssistantToolCalls(assistantMessageJson).Count > 0;

    public static ChatMessage BuildAssistantMessage(string assistantMessageJson, string contentFallback = "")
    {
        JsonElement? raw = null;
        if (!string.IsNullOrWhiteSpace(assistantMessageJson))
        {
            using var document = JsonDocument.Parse(assistantMessageJson);
            raw = document.RootElement.Clone();
        }

        return new ChatMessage(Domain.Enums.MessageRole.Assistant, contentFallback, raw);
    }

    public static ChatMessage BuildToolResultMessage(string toolCallId, string contentJson)
    {
        var wire = $$"""
            {
              "role": "tool",
              "tool_call_id": "{{toolCallId}}",
              "content": {{JsonSerializer.Serialize(contentJson)}}
            }
            """;

        using var document = JsonDocument.Parse(wire);
        return new ChatMessage(
            Domain.Enums.MessageRole.Tool,
            contentJson,
            document.RootElement.Clone());
    }
}
