using System.Text.Json;
using System.Text.Json.Nodes;
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

    /// <summary>
    /// True when a streaming SSE chunk carries a non-empty <c>choices[].delta.tool_calls</c>
    /// array. Unparseable chunks count as non-tool so they can still reach the client.
    /// </summary>
    public static bool StreamChunkHasToolCalls(string data)
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

    /// <summary>
    /// Returns a copy of <paramref name="assistantMessageJson"/> with matching tool_call
    /// <c>function.arguments</c> replaced by the provided JSON object text (stored as a JSON string
    /// on the wire, OpenAI-style).
    /// </summary>
    public static string? ReplaceToolCallArguments(
        string? assistantMessageJson,
        IReadOnlyDictionary<string, string> argumentsByCallId)
    {
        if (string.IsNullOrWhiteSpace(assistantMessageJson) || argumentsByCallId.Count == 0)
        {
            return assistantMessageJson;
        }

        try
        {
            var root = JsonNode.Parse(assistantMessageJson) as JsonObject;
            if (root is null ||
                root["tool_calls"] is not JsonArray toolCalls)
            {
                return assistantMessageJson;
            }

            var changed = false;
            foreach (var item in toolCalls)
            {
                if (item is not JsonObject call)
                {
                    continue;
                }

                var id = call["id"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(id) ||
                    !argumentsByCallId.TryGetValue(id, out var argumentsJson))
                {
                    continue;
                }

                if (call["function"] is not JsonObject function)
                {
                    continue;
                }

                function["arguments"] = argumentsJson;
                changed = true;
            }

            return changed ? root.ToJsonString() : assistantMessageJson;
        }
        catch (JsonException)
        {
            return assistantMessageJson;
        }
    }
}
