using System.Text;
using System.Text.Json;
using Comprexy.Domain.Entities;
using Comprexy.Domain.Enums;

namespace Comprexy.Application.Services;

/// <summary>
/// Builds a compact retain nomination index for Smart Cached compression: sequence numbers,
/// roles, tool/path stubs, and short non-tool skims — no full file or tool bodies.
/// </summary>
public static class RetainIndexBuilder
{
    public const int NonToolSkimMaxChars = 100;

    public static string Build(
        IReadOnlyList<ConversationMessage> candidates,
        int? tipSequence)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Retain Index");

        foreach (var message in candidates.OrderBy(m => m.Sequence))
        {
            sb.Append("seq=").Append(message.Sequence).Append(' ');
            sb.Append(RoleLabel(message.Role));
            if (tipSequence == message.Sequence)
            {
                sb.Append(" (tip)");
            }

            var path = FileReadPathExtractor.TryExtract(message);
            var toolNames = TryGetAssistantToolNames(message);

            if (toolNames.Count > 0)
            {
                sb.Append("  tool_calls=").Append(string.Join(',', toolNames));
                if (path is not null)
                {
                    sb.Append("  path=").Append(path);
                }
            }
            else if (path is not null)
            {
                var kind = message.Role == MessageRole.User ? "file_read" : "tool";
                sb.Append(' ').Append(kind).Append(" path=").Append(path);
                sb.Append("  ~").Append(EstimateBodyChars(message)).Append(" chars [body omitted]");
            }
            else
            {
                var skim = SkimText(message);
                if (!string.IsNullOrEmpty(skim))
                {
                    sb.Append("  \"").Append(skim).Append('"');
                }
            }

            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    private static string RoleLabel(MessageRole role) =>
        role switch
        {
            MessageRole.System => "system",
            MessageRole.User => "user",
            MessageRole.Assistant => "assistant",
            MessageRole.Tool => "tool",
            _ => role.ToString().ToLowerInvariant()
        };

    private static int EstimateBodyChars(ConversationMessage message)
    {
        if (!string.IsNullOrEmpty(message.Content))
        {
            return message.Content.Length;
        }

        return message.RawWireJson?.Length ?? 0;
    }

    private static string SkimText(ConversationMessage message)
    {
        var text = !string.IsNullOrWhiteSpace(message.Content)
            ? message.Content.Trim()
            : ExtractPlainTextFromWire(message.RawWireJson);

        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        text = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        while (text.Contains("  ", StringComparison.Ordinal))
        {
            text = text.Replace("  ", " ", StringComparison.Ordinal);
        }

        if (text.Length <= NonToolSkimMaxChars)
        {
            return text;
        }

        return text[..NonToolSkimMaxChars].TrimEnd() + "…";
    }

    private static string ExtractPlainTextFromWire(string? rawWireJson)
    {
        if (string.IsNullOrWhiteSpace(rawWireJson))
        {
            return string.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(rawWireJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return string.Empty;
            }

            if (!document.RootElement.TryGetProperty("content", out var content))
            {
                return string.Empty;
            }

            if (content.ValueKind == JsonValueKind.String)
            {
                return content.GetString() ?? string.Empty;
            }

            if (content.ValueKind == JsonValueKind.Array)
            {
                var parts = new List<string>();
                foreach (var part in content.EnumerateArray())
                {
                    if (part.ValueKind == JsonValueKind.Object &&
                        part.TryGetProperty("text", out var text) &&
                        text.ValueKind == JsonValueKind.String)
                    {
                        var value = text.GetString();
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            parts.Add(value);
                        }
                    }
                }

                return string.Join(' ', parts);
            }
        }
        catch (JsonException)
        {
            // ignore
        }

        return string.Empty;
    }

    private static IReadOnlyList<string> TryGetAssistantToolNames(ConversationMessage message)
    {
        if (message.Role != MessageRole.Assistant || string.IsNullOrWhiteSpace(message.RawWireJson))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(message.RawWireJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("tool_calls", out var toolCalls) ||
                toolCalls.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var names = new List<string>();
            foreach (var call in toolCalls.EnumerateArray())
            {
                if (call.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                string? name = null;
                if (call.TryGetProperty("function", out var function) &&
                    function.ValueKind == JsonValueKind.Object &&
                    function.TryGetProperty("name", out var nestedName) &&
                    nestedName.ValueKind == JsonValueKind.String)
                {
                    name = nestedName.GetString();
                }
                else if (call.TryGetProperty("name", out var flatName) &&
                         flatName.ValueKind == JsonValueKind.String)
                {
                    name = flatName.GetString();
                }

                if (!string.IsNullOrWhiteSpace(name))
                {
                    names.Add(name.Trim());
                }
            }

            return names;
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
