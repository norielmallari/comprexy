using System.Text.Json;
using System.Text.RegularExpressions;
using Comprexy.Domain.Entities;
using Comprexy.Domain.Enums;

namespace Comprexy.Application.Services;

/// <summary>
/// Extracts a single file path from a tool result or client-injected file-read user message.
/// Fail-closed: returns null when no confident path is found.
/// </summary>
public static class FileReadPathExtractor
{
    private static readonly Regex PathTag = new(
        @"<path>\s*(?<path>[^<]+?)\s*</path>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex CalledReadToolFilePath = new(
        @"Called the Read tool with the following input:\s*\{[^}]*[""']filePath[""']\s*:\s*[""'](?<path>[^""']+)[""']",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly string[] PathPropertyNames =
    [
        "filePath",
        "file_path",
        "target_file",
        "path"
    ];

    public static string? TryExtract(ConversationMessage message)
    {
        if (message.Role == MessageRole.Tool)
        {
            return TryExtractFromToolOrUserPayload(message);
        }

        // Kilo / Cursor often inject Read results as user multimodal turns, not role=tool.
        if (message.Role == MessageRole.User && LooksLikeInjectedFileRead(message))
        {
            return TryExtractFromToolOrUserPayload(message);
        }

        return null;
    }

    public static string? TryExtractToolCallId(ConversationMessage message)
    {
        if (message.Role != MessageRole.Tool || string.IsNullOrWhiteSpace(message.RawWireJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(message.RawWireJson);
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("tool_call_id", out var id) &&
                id.ValueKind == JsonValueKind.String)
            {
                var value = id.GetString();
                return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            }
        }
        catch (JsonException)
        {
            // ignore
        }

        return null;
    }

    /// <summary>
    /// Returns tool_call ids on an assistant message. Empty when none / unparseable.
    /// </summary>
    public static IReadOnlyList<string> GetAssistantToolCallIds(ConversationMessage message)
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

            var ids = new List<string>();
            foreach (var call in toolCalls.EnumerateArray())
            {
                if (call.ValueKind == JsonValueKind.Object &&
                    call.TryGetProperty("id", out var id) &&
                    id.ValueKind == JsonValueKind.String)
                {
                    var value = id.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        ids.Add(value.Trim());
                    }
                }
            }

            return ids;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    internal static string Normalize(string path)
    {
        var trimmed = path.Trim().Replace('\\', '/');
        while (trimmed.Contains("//", StringComparison.Ordinal))
        {
            trimmed = trimmed.Replace("//", "/", StringComparison.Ordinal);
        }

        return trimmed;
    }

    private static bool LooksLikeInjectedFileRead(ConversationMessage message)
    {
        var haystack = $"{message.Content}\n{message.RawWireJson}";
        if (string.IsNullOrWhiteSpace(haystack))
        {
            return false;
        }

        return PathTag.IsMatch(haystack) ||
               haystack.Contains("Called the Read tool", StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryExtractFromToolOrUserPayload(ConversationMessage message)
    {
        if (!string.IsNullOrWhiteSpace(message.RawWireJson))
        {
            var fromWire = TryExtractFromJsonText(message.RawWireJson);
            if (fromWire is not null)
            {
                return Normalize(fromWire);
            }
        }

        if (!string.IsNullOrWhiteSpace(message.Content))
        {
            var fromContent = TryExtractFromText(message.Content);
            if (fromContent is not null)
            {
                return Normalize(fromContent);
            }
        }

        return null;
    }

    private static string? TryExtractFromJsonText(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return TryExtractPathFromElement(document.RootElement);
        }
        catch (JsonException)
        {
            return TryExtractFromText(json);
        }
    }

    private static string? TryExtractPathFromElement(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            return TryExtractFromText(element.GetString() ?? string.Empty);
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var fromItem = TryExtractPathFromElement(item);
                if (fromItem is not null)
                {
                    return fromItem;
                }
            }

            return null;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var name in PathPropertyNames)
        {
            if (element.TryGetProperty(name, out var property) &&
                property.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(property.GetString()))
            {
                // Prefer <path> body when present; filePath on a short "Called the Read tool" part is also fine.
                return property.GetString();
            }
        }

        if (element.TryGetProperty("text", out var textPart))
        {
            var fromText = TryExtractPathFromElement(textPart);
            if (fromText is not null)
            {
                return fromText;
            }
        }

        if (element.TryGetProperty("content", out var content))
        {
            var fromContent = TryExtractPathFromElement(content);
            if (fromContent is not null)
            {
                return fromContent;
            }
        }

        if (element.TryGetProperty("arguments", out var arguments))
        {
            var fromArgs = TryExtractPathFromArguments(arguments);
            if (fromArgs is not null)
            {
                return fromArgs;
            }
        }

        return null;
    }

    private static string? TryExtractPathFromArguments(JsonElement arguments)
    {
        if (arguments.ValueKind == JsonValueKind.String)
        {
            var raw = arguments.GetString() ?? string.Empty;
            try
            {
                using var document = JsonDocument.Parse(raw);
                return TryExtractPathFromElement(document.RootElement) ?? TryExtractFromText(raw);
            }
            catch (JsonException)
            {
                return TryExtractFromText(raw);
            }
        }

        return TryExtractPathFromElement(arguments);
    }

    private static string? TryExtractFromText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        // Prefer explicit result path tags over the call preamble filePath.
        var match = PathTag.Match(text);
        if (match.Success)
        {
            var path = match.Groups["path"].Value.Trim();
            return string.IsNullOrWhiteSpace(path) ? null : path;
        }

        var called = CalledReadToolFilePath.Match(text);
        if (called.Success)
        {
            var path = called.Groups["path"].Value.Trim();
            return string.IsNullOrWhiteSpace(path) ? null : path;
        }

        return null;
    }
}
