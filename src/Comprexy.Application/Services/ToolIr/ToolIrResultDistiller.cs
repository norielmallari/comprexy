using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Comprexy.Application.Configuration;
using Microsoft.Extensions.Options;

namespace Comprexy.Application.Services.ToolIr;

/// <summary>
/// Distills native client tool results into compact IR observations for the model.
/// </summary>
public class ToolIrResultDistiller
{
    private readonly ToolSchemaOptions _options;
    private readonly ToolIrFileBodyCache _fileCache;

    public ToolIrResultDistiller(IOptions<ToolSchemaOptions> options, ToolIrFileBodyCache fileCache)
    {
        _options = options.Value;
        _fileCache = fileCache;
    }

    /// <summary>
    /// Drops cached file bodies after a successful client edit/write so the next IR read refreshes.
    /// </summary>
    public int InvalidateCachedFile(Guid conversationId, string path) =>
        _fileCache.Invalidate(conversationId, path);

    public string Distill(
        Guid conversationId,
        ToolIrCallMapping mapping,
        string nativeContent)
    {
        return mapping.ComprexyToolName switch
        {
            ToolSchemaConstants.FileRangeToolName => DistillFileRange(conversationId, mapping, nativeContent),
            ToolSchemaConstants.FileManifestToolName => DistillFileManifest(conversationId, mapping, nativeContent),
            ToolSchemaConstants.FileSearchToolName => DistillFileSearch(mapping, nativeContent),
            ToolSchemaConstants.DirListToolName => DistillDirList(mapping, nativeContent),
            ToolSchemaConstants.ShellToolName => DistillShell(mapping, nativeContent),
            _ => JsonSerializer.Serialize(new
            {
                type = "passthrough",
                tool = mapping.ComprexyToolName,
                content = Truncate(nativeContent, 4000)
            })
        };
    }

    private string DistillShell(ToolIrCallMapping mapping, string nativeContent)
    {
        var truncatedContent = Truncate(nativeContent, _options.MaxShellObservationChars);
        var truncated = !string.Equals(truncatedContent, nativeContent, StringComparison.Ordinal);
        return JsonSerializer.Serialize(new
        {
            type = "shell",
            command = TryReadArg(mapping.IrArgumentsJson, "command"),
            truncated,
            content = truncatedContent
        });
    }

    private string DistillFileRange(Guid conversationId, ToolIrCallMapping mapping, string nativeContent)
    {
        var path = mapping.Path ?? "unknown";
        var extracted = ExtractFileBody(nativeContent);
        var start = mapping.StartLine ?? 1;
        var end = mapping.EndLine ?? start;
        var absoluteStart = extracted.FirstLineNumber ?? start;
        var isPartialWindow = absoluteStart > 1;

        ToolIrCachedFileBody? cached = null;
        string text;
        bool truncated;

        if (isPartialWindow)
        {
            // Windowed native Read: body is already the requested slice (line prefixes stripped).
            // Do not store it as a full-file cache entry — that would poison later absolute ranges.
            text = CapWindowLines(extracted.Body, _options.MaxRangeLines, out truncated);
        }
        else
        {
            cached = _fileCache.SetIfRicher(conversationId, path, extracted.Body);
            if (!ToolIrFileBodyCache.TrySliceLines(
                    cached,
                    start,
                    end,
                    _options.MaxRangeLines,
                    out text,
                    out truncated))
            {
                // Cache richer but still short of this range — surface the native window as-is.
                text = CapWindowLines(extracted.Body, _options.MaxRangeLines, out truncated);
            }
        }

        var hash = cached?.ContentHash
                   ?? ToolIrFileBodyCache.BuildEntry(path, extracted.Body).ContentHash;
        var pathOut = cached?.Path ?? ToolIrFileBodyCache.NormalizePath(path);

        return JsonSerializer.Serialize(new
        {
            type = "file_range",
            path = pathOut,
            start_line = start,
            end_line = Math.Min(end, start + _options.MaxRangeLines - 1),
            truncated,
            content_hash = hash,
            content = text
        });
    }

    private string DistillFileManifest(Guid conversationId, ToolIrCallMapping mapping, string nativeContent)
    {
        var path = mapping.Path ?? "unknown";
        var extracted = ExtractFileBody(nativeContent);
        var absoluteStart = extracted.FirstLineNumber ?? 1;
        if (absoluteStart > 1)
        {
            // Manifest needs a full-ish body; refuse to cache/poison from a windowed read.
            var ephemeral = ToolIrFileBodyCache.BuildEntry(path, extracted.Body);
            return BuildManifestFromCache(ephemeral);
        }

        var cached = _fileCache.SetIfRicher(conversationId, path, extracted.Body);
        return BuildManifestFromCache(cached);
    }

    private static string CapWindowLines(string body, int maxLines, out bool truncated)
    {
        truncated = false;
        if (string.IsNullOrEmpty(body) || maxLines < 1)
        {
            return body;
        }

        var lines = body.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        // Preserve trailing empty from final newline as Split does.
        var contentCount = body.EndsWith('\n') ? lines.Length - 1 : lines.Length;
        if (contentCount <= maxLines)
        {
            return body.Replace("\r\n", "\n", StringComparison.Ordinal);
        }

        truncated = true;
        return string.Join('\n', lines.Take(maxLines));
    }

    public static string BuildManifestFromCache(ToolIrCachedFileBody cached)
    {
        var language = GuessLanguage(cached.Path);
        var imports = ExtractImportHints(cached.Body, max: 20);
        var symbols = ExtractSymbolHints(cached.Body, max: 30);
        return JsonSerializer.Serialize(new
        {
            type = "file_manifest",
            path = cached.Path,
            language,
            line_count = cached.LineStartOffsets.Count,
            size_bytes = Encoding.UTF8.GetByteCount(cached.Body),
            content_hash = cached.ContentHash,
            imports,
            symbols
        });
    }

    private string DistillFileSearch(ToolIrCallMapping mapping, string nativeContent)
    {
        var matches = ExtractSearchMatches(nativeContent, _options.MaxSearchMatches, out var truncated);
        return JsonSerializer.Serialize(new
        {
            type = "file_search",
            query = TryReadArg(mapping.IrArgumentsJson, "query"),
            path = mapping.Path,
            truncated,
            match_count = matches.Count,
            matches
        });
    }

    private string DistillDirList(ToolIrCallMapping mapping, string nativeContent)
    {
        var entries = ExtractDirEntries(nativeContent, _options.MaxDirListEntries, out var truncated);
        return JsonSerializer.Serialize(new
        {
            type = "dir_list",
            path = mapping.Path,
            truncated,
            entry_count = entries.Count,
            entries
        });
    }

    private static ExtractedFileBody ExtractFileBody(string nativeContent)
    {
        if (string.IsNullOrEmpty(nativeContent))
        {
            return new ExtractedFileBody(string.Empty, null, false);
        }

        var trimmed = nativeContent.TrimStart();
        if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
        {
            try
            {
                using var document = JsonDocument.Parse(nativeContent);
                if (document.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var name in new[] { "contents", "content", "text", "data", "result" })
                    {
                        if (document.RootElement.TryGetProperty(name, out var value) &&
                            value.ValueKind == JsonValueKind.String)
                        {
                            return StripReadLinePrefixes(value.GetString() ?? string.Empty);
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // fall through — treat as tagged / raw text
            }
        }

        if (TryExtractTaggedContent(nativeContent, out var tagged))
        {
            return StripReadLinePrefixes(tagged);
        }

        return StripReadLinePrefixes(nativeContent);
    }

    /// <summary>
    /// Cursor / Kilo Read tools wrap bodies as <c>&lt;path&gt;…&lt;/path&gt;&lt;content&gt;…&lt;/content&gt;</c>.
    /// </summary>
    internal static bool TryExtractTaggedContent(string nativeContent, out string content)
    {
        content = string.Empty;
        const string open = "<content>";
        const string close = "</content>";
        var start = nativeContent.IndexOf(open, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return false;
        }

        start += open.Length;
        var end = nativeContent.IndexOf(close, start, StringComparison.OrdinalIgnoreCase);
        if (end < 0)
        {
            return false;
        }

        content = nativeContent[start..end];
        if (content.StartsWith('\n'))
        {
            content = content[1..];
        }

        if (content.EndsWith('\n') && content.Length > 0)
        {
            // Keep trailing newline semantics from the file when present before </content>.
        }

        return true;
    }

    /// <summary>
    /// Strips Read-tool line prefixes (<c>12: </c> / <c>12|</c>). Leaves plain text unchanged.
    /// When prefixes are present, <see cref="ExtractedFileBody.FirstLineNumber"/> is the first absolute line.
    /// </summary>
    internal static ExtractedFileBody StripReadLinePrefixes(string body)
    {
        if (string.IsNullOrEmpty(body))
        {
            return new ExtractedFileBody(string.Empty, null, false);
        }

        var normalized = body.Replace("\r\n", "\n", StringComparison.Ordinal);
        var lines = normalized.Split('\n');
        var prefixed = 0;
        var stripped = new string[lines.Length];
        int? firstLineNumber = null;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (TryStripLinePrefix(line, out var rest, out var lineNumber))
            {
                prefixed++;
                stripped[i] = rest;
                firstLineNumber ??= lineNumber;
            }
            else
            {
                stripped[i] = line;
            }
        }

        // Only treat as numbered Read output when most non-empty lines carry prefixes.
        var nonEmpty = lines.Count(static l => l.Length > 0);
        if (nonEmpty == 0 || prefixed * 2 < nonEmpty)
        {
            return new ExtractedFileBody(StripReadPaginationFooter(normalized), null, false);
        }

        return new ExtractedFileBody(
            StripReadPaginationFooter(string.Join('\n', stripped)),
            firstLineNumber,
            true);
    }

    /// <summary>
    /// Removes Cursor/Kilo Read pagination trailers such as
    /// <c>(Showing lines 1-80 of 267. Use offset=81 to continue.)</c>
    /// so they are not cached as file body lines.
    /// </summary>
    internal static string StripReadPaginationFooter(string body)
    {
        if (string.IsNullOrEmpty(body))
        {
            return body;
        }

        var normalized = body.Replace("\r\n", "\n", StringComparison.Ordinal);
        var lines = normalized.Split('\n');
        var end = lines.Length;
        while (end > 0 && lines[end - 1].Length == 0)
        {
            end--;
        }

        if (end == 0)
        {
            return normalized;
        }

        var last = lines[end - 1].Trim();
        if (!IsReadPaginationFooter(last))
        {
            return normalized;
        }

        end--;
        while (end > 0 && lines[end - 1].Length == 0)
        {
            end--;
        }

        return end == 0 ? string.Empty : string.Join('\n', lines.Take(end));
    }

    private static bool IsReadPaginationFooter(string line) =>
        line.Contains("Showing lines", StringComparison.OrdinalIgnoreCase) &&
        (line.Contains("Use offset=", StringComparison.OrdinalIgnoreCase) ||
         line.Contains("of ", StringComparison.OrdinalIgnoreCase));

    private static bool TryStripLinePrefix(string line, out string rest, out int lineNumber)
    {
        rest = line;
        lineNumber = 0;
        var i = 0;
        while (i < line.Length && char.IsAsciiDigit(line[i]))
        {
            i++;
        }

        if (i == 0 || i >= line.Length)
        {
            return false;
        }

        if (!int.TryParse(line.AsSpan(0, i), out lineNumber))
        {
            return false;
        }

        if (line[i] == ':')
        {
            i++;
            if (i < line.Length && line[i] == ' ')
            {
                i++;
            }

            rest = line[i..];
            return true;
        }

        if (line[i] == '|')
        {
            i++;
            rest = line[i..];
            return true;
        }

        return false;
    }

    internal readonly record struct ExtractedFileBody(string Body, int? FirstLineNumber, bool HadLinePrefixes);

    private static List<object> ExtractSearchMatches(string nativeContent, int max, out bool truncated)
    {
        truncated = false;
        var matches = new List<object>();
        try
        {
            using var document = JsonDocument.Parse(nativeContent);
            if (TryEnumerateMatches(document.RootElement, out var elements))
            {
                foreach (var element in elements)
                {
                    if (matches.Count >= max)
                    {
                        truncated = true;
                        break;
                    }

                    matches.Add(new
                    {
                        path = TryGetString(element, "path", "file", "filename") ?? string.Empty,
                        line = TryGetInt(element, "line", "line_number", "lineNumber") ?? 0,
                        preview = Truncate(
                            TryGetString(element, "preview", "content", "text", "snippet") ?? element.GetRawText(),
                            200)
                    });
                }

                return matches;
            }
        }
        catch (JsonException)
        {
            // plain text fallback
        }

        foreach (var line in nativeContent.Split('\n'))
        {
            if (matches.Count >= max)
            {
                truncated = true;
                break;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            matches.Add(new { path = "", line = 0, preview = Truncate(line.TrimEnd(), 200) });
        }

        return matches;
    }

    private static List<object> ExtractDirEntries(string nativeContent, int max, out bool truncated)
    {
        truncated = false;
        var entries = new List<object>();
        try
        {
            using var document = JsonDocument.Parse(nativeContent);
            var root = document.RootElement;
            JsonElement array = default;
            if (root.ValueKind == JsonValueKind.Array)
            {
                array = root;
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                foreach (var name in new[] { "entries", "files", "items", "children", "result" })
                {
                    if (root.TryGetProperty(name, out var candidate) && candidate.ValueKind == JsonValueKind.Array)
                    {
                        array = candidate;
                        break;
                    }
                }
            }

            if (array.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in array.EnumerateArray())
                {
                    if (entries.Count >= max)
                    {
                        truncated = true;
                        break;
                    }

                    if (element.ValueKind == JsonValueKind.String)
                    {
                        entries.Add(new { name = element.GetString(), kind = "unknown" });
                        continue;
                    }

                    entries.Add(new
                    {
                        name = TryGetString(element, "name", "path", "filename") ?? element.GetRawText(),
                        kind = TryGetString(element, "kind", "type", "entry_type") ?? "unknown"
                    });
                }

                return entries;
            }
        }
        catch (JsonException)
        {
            // plain text fallback
        }

        foreach (var line in nativeContent.Split('\n'))
        {
            if (entries.Count >= max)
            {
                truncated = true;
                break;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            entries.Add(new { name = line.Trim(), kind = "unknown" });
        }

        return entries;
    }

    private static bool TryEnumerateMatches(JsonElement root, out IEnumerable<JsonElement> elements)
    {
        elements = [];
        if (root.ValueKind == JsonValueKind.Array)
        {
            elements = root.EnumerateArray();
            return true;
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var name in new[] { "matches", "results", "hits", "items" })
        {
            if (root.TryGetProperty(name, out var array) && array.ValueKind == JsonValueKind.Array)
            {
                elements = array.EnumerateArray();
                return true;
            }
        }

        return false;
    }

    private static List<string> ExtractImportHints(string body, int max)
    {
        var hints = new List<string>();
        foreach (var line in body.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("using ", StringComparison.Ordinal) ||
                trimmed.StartsWith("import ", StringComparison.Ordinal) ||
                trimmed.StartsWith("from ", StringComparison.Ordinal) ||
                trimmed.StartsWith("#include ", StringComparison.Ordinal))
            {
                hints.Add(Truncate(trimmed, 160));
                if (hints.Count >= max)
                {
                    break;
                }
            }
        }

        return hints;
    }

    private static List<object> ExtractSymbolHints(string body, int max)
    {
        var symbols = new List<object>();
        var lineNumber = 0;
        foreach (var line in body.Split('\n'))
        {
            lineNumber++;
            var trimmed = line.TrimStart();
            if (LooksLikeSymbol(trimmed, out var name, out var kind))
            {
                symbols.Add(new { name, kind, line = lineNumber });
                if (symbols.Count >= max)
                {
                    break;
                }
            }
        }

        return symbols;
    }

    private static bool LooksLikeSymbol(string trimmed, out string name, out string kind)
    {
        name = string.Empty;
        kind = "symbol";
        if (trimmed.StartsWith("class ", StringComparison.Ordinal) ||
            trimmed.StartsWith("public class ", StringComparison.Ordinal) ||
            trimmed.StartsWith("internal class ", StringComparison.Ordinal))
        {
            kind = "class";
            name = ExtractIdentifierAfter(trimmed, "class");
            return !string.IsNullOrWhiteSpace(name);
        }

        if (trimmed.StartsWith("interface ", StringComparison.Ordinal) ||
            trimmed.StartsWith("public interface ", StringComparison.Ordinal))
        {
            kind = "interface";
            name = ExtractIdentifierAfter(trimmed, "interface");
            return !string.IsNullOrWhiteSpace(name);
        }

        if (trimmed.StartsWith("function ", StringComparison.Ordinal) ||
            trimmed.StartsWith("def ", StringComparison.Ordinal) ||
            trimmed.StartsWith("fn ", StringComparison.Ordinal))
        {
            kind = "function";
            name = ExtractIdentifierAfter(trimmed, trimmed.StartsWith("function ", StringComparison.Ordinal) ? "function" : trimmed.StartsWith("def ", StringComparison.Ordinal) ? "def" : "fn");
            return !string.IsNullOrWhiteSpace(name);
        }

        if (trimmed.Contains("(", StringComparison.Ordinal) &&
            (trimmed.StartsWith("public ", StringComparison.Ordinal) ||
             trimmed.StartsWith("private ", StringComparison.Ordinal) ||
             trimmed.StartsWith("internal ", StringComparison.Ordinal) ||
             trimmed.StartsWith("protected ", StringComparison.Ordinal) ||
             trimmed.StartsWith("export ", StringComparison.Ordinal) ||
             trimmed.StartsWith("async ", StringComparison.Ordinal)))
        {
            kind = "function";
            var paren = trimmed.IndexOf('(');
            var before = trimmed[..paren].Trim();
            var parts = before.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            name = parts.Length > 0 ? parts[^1] : string.Empty;
            return !string.IsNullOrWhiteSpace(name);
        }

        return false;
    }

    private static string ExtractIdentifierAfter(string line, string keyword)
    {
        var idx = line.IndexOf(keyword, StringComparison.Ordinal);
        if (idx < 0)
        {
            return string.Empty;
        }

        var rest = line[(idx + keyword.Length)..].TrimStart();
        var end = 0;
        while (end < rest.Length && (char.IsLetterOrDigit(rest[end]) || rest[end] is '_' or '$'))
        {
            end++;
        }

        return rest[..end];
    }

    private static string GuessLanguage(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".cs" => "csharp",
            ".ts" => "typescript",
            ".tsx" => "tsx",
            ".js" => "javascript",
            ".jsx" => "jsx",
            ".py" => "python",
            ".go" => "go",
            ".rs" => "rust",
            ".java" => "java",
            ".md" => "markdown",
            ".json" => "json",
            ".yml" or ".yaml" => "yaml",
            _ => string.IsNullOrEmpty(ext) ? "unknown" : ext.TrimStart('.')
        };
    }

    private static string? TryReadArg(string argumentsJson, string name)
    {
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
            if (document.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
        }
        catch (JsonException)
        {
            // ignore
        }

        return null;
    }

    private static string? TryGetString(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
        }

        return null;
    }

    private static int? TryGetInt(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var n))
            {
                return n;
            }

            if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out n))
            {
                return n;
            }
        }

        return null;
    }

    private static string Truncate(string text, int maxChars)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxChars)
        {
            return text;
        }

        return text[..maxChars] + "…";
    }
}
