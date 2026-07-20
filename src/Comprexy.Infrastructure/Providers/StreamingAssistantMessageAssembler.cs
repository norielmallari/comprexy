using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Comprexy.Infrastructure.Providers;

/// <summary>
/// Rebuilds an assistant message object from OpenAI-compatible streaming <c>delta</c> chunks,
/// preserving <c>content</c>, <c>tool_calls</c>, and any other extension fields (e.g.
/// <c>reasoning_content</c>) so persisted wire JSON matches what non-streaming responses keep.
/// </summary>
public sealed class StreamingAssistantMessageAssembler
{
    private readonly StringBuilder _content = new();
    private readonly SortedDictionary<int, ToolCallAccumulator> _toolCalls = new();
    private readonly Dictionary<string, StringBuilder> _stringExtensions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, JsonNode?> _otherExtensions = new(StringComparer.Ordinal);

    public string Content => _content.ToString();

    public void MergeDelta(JsonElement delta)
    {
        if (delta.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var property in delta.EnumerateObject())
        {
            switch (property.Name)
            {
                case "role":
                    break;
                case "content":
                    AppendString(_content, property.Value);
                    break;
                case "tool_calls":
                    MergeToolCalls(property.Value);
                    break;
                default:
                    MergeExtension(property.Name, property.Value);
                    break;
            }
        }
    }

    public string BuildMessageJson(JsonSerializerOptions serializerOptions)
    {
        var message = new JsonObject
        {
            ["role"] = "assistant",
            ["content"] = string.IsNullOrEmpty(Content) ? null : Content
        };

        foreach (var (name, value) in _stringExtensions)
        {
            if (value.Length > 0)
            {
                message[name] = value.ToString();
            }
        }

        foreach (var (name, value) in _otherExtensions)
        {
            message[name] = value?.DeepClone();
        }

        if (_toolCalls.Count > 0)
        {
            var toolCalls = new JsonArray();
            foreach (var (_, call) in _toolCalls)
            {
                toolCalls.Add(new JsonObject
                {
                    ["id"] = call.Id,
                    ["type"] = call.Type ?? "function",
                    ["function"] = new JsonObject
                    {
                        ["name"] = call.FunctionName,
                        ["arguments"] = call.Arguments.ToString()
                    }
                });
            }

            message["tool_calls"] = toolCalls;
        }

        return message.ToJsonString(serializerOptions);
    }

    private void MergeExtension(string name, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            if (!_stringExtensions.TryGetValue(name, out var builder))
            {
                builder = new StringBuilder();
                _stringExtensions[name] = builder;
            }

            AppendString(builder, value);
            return;
        }

        if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return;
        }

        // Last non-null wins for objects, arrays, numbers, and booleans.
        _otherExtensions[name] = JsonNode.Parse(value.GetRawText());
    }

    private void MergeToolCalls(JsonElement toolCalls)
    {
        if (toolCalls.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var toolCall in toolCalls.EnumerateArray())
        {
            if (toolCall.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var index = toolCall.TryGetProperty("index", out var indexElement) &&
                        indexElement.ValueKind == JsonValueKind.Number
                ? indexElement.GetInt32()
                : _toolCalls.Count;

            if (!_toolCalls.TryGetValue(index, out var accumulator))
            {
                accumulator = new ToolCallAccumulator();
                _toolCalls[index] = accumulator;
            }

            if (toolCall.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
            {
                accumulator.Id ??= id.GetString();
            }

            if (toolCall.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String)
            {
                accumulator.Type ??= type.GetString();
            }

            if (toolCall.TryGetProperty("function", out var function) && function.ValueKind == JsonValueKind.Object)
            {
                if (function.TryGetProperty("name", out var functionName) &&
                    functionName.ValueKind == JsonValueKind.String)
                {
                    accumulator.FunctionName ??= functionName.GetString();
                }

                if (function.TryGetProperty("arguments", out var arguments) &&
                    arguments.ValueKind == JsonValueKind.String)
                {
                    accumulator.Arguments.Append(arguments.GetString());
                }
            }
        }
    }

    private static void AppendString(StringBuilder builder, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            return;
        }

        var text = value.GetString();
        if (!string.IsNullOrEmpty(text))
        {
            builder.Append(text);
        }
    }

    private sealed class ToolCallAccumulator
    {
        public string? Id { get; set; }
        public string? Type { get; set; }
        public string? FunctionName { get; set; }
        public StringBuilder Arguments { get; } = new();
    }
}
