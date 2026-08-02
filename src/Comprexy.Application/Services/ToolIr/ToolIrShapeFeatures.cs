using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Comprexy.Application.Services.ToolIr;

public enum ToolIrOuterKind
{
    JsonObject,
    Text
}

public enum ToolIrTagToken
{
    Path,
    Type,
    Notice,
    File,
    Error,
    Warning,
    Lines,
    Truncated,
    Content,
    Unknown
}

public readonly record struct ToolIrJsonStringProperty(
    ToolIrJsonFieldToken Token,
    int ValueStartOffset,
    int ValueEndOffset,
    int DecodedLength);

public readonly record struct ToolIrTagOccurrence(
    ToolIrTagToken Token,
    bool IsClose,
    int StartOffset,
    int EndOffset);

/// <summary>
/// Sanitized structural features of a tool result. No payload characters retained
/// except offsets and closed-vocabulary tokens.
/// </summary>
public sealed record ToolIrShapeFeatures(
    int PayloadLength,
    ToolIrOuterKind OuterKind,
    IReadOnlyList<ToolIrJsonStringProperty> JsonStringProperties,
    int UnknownJsonStringPropertyCount,
    IReadOnlyList<ToolIrTagOccurrence> Tags,
    int LineCount,
    int[] LineLengths,
    int MaxLineLength,
    int ColonPrefixedLineCount,
    int PipePrefixedLineCount,
    int UnprefixedNonEmptyLineCount,
    int? FirstPrefixLineNumber,
    bool FooterPresentOnLastLine,
    int? FooterTotalLineCount,
    string? ExtensionToken,
    ToolIrShapeBodySpan? ObservedBody);

public static class ToolIrShapeSanitizer
{
    private static readonly HashSet<string> AllowlistedTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "path", "type", "notice", "file", "error", "warning", "lines", "truncated", "content"
    };

    private static readonly Regex ExtensionRegex = new(
        "^[a-z0-9]{1,8}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex FooterTotalRegex = new(
        @"of\s+(\d+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static ToolIrShapeFeatures? Build(
        string payload,
        ToolIrShapeConfidence classification,
        ToolIrResultDistiller.ExtractedFileBody? heuristicBody,
        int maxSampleLines)
    {
        if (payload.Any(char.IsSurrogate) && HasUnpairedSurrogate(payload))
        {
            return null;
        }

        var outerKind = payload.TrimStart().StartsWith('{')
            ? ToolIrOuterKind.JsonObject
            : ToolIrOuterKind.Text;

        var (jsonProps, unknownJsonCount, byteOffsets) = ScanJson(payload);
        var charOffsets = ConvertByteOffsetsToChar(payload, byteOffsets);
        var mappedJsonProps = MapJsonPropsToCharOffsets(jsonProps, charOffsets);

        var tags = ScanTags(payload);
        var lineStats = MeasureLines(payload, maxSampleLines);
        var footer = DetectFooter(payload);
        var extension = ExtractExtensionToken(payload);

        ToolIrShapeBodySpan? observedBody = null;
        if (classification == ToolIrShapeConfidence.Unambiguous && heuristicBody is not null)
        {
            observedBody = BuildObservedBody(payload, heuristicBody.Value, mappedJsonProps, tags);
        }

        return new ToolIrShapeFeatures(
            payload.Length,
            outerKind,
            mappedJsonProps,
            unknownJsonCount,
            tags,
            lineStats.LineCount,
            lineStats.LineLengths,
            lineStats.MaxLineLength,
            lineStats.Colon,
            lineStats.Pipe,
            lineStats.Unprefixed,
            lineStats.FirstPrefixLine,
            footer.Present,
            footer.Total,
            extension,
            observedBody);
    }

    private static bool HasUnpairedSurrogate(string payload)
    {
        for (var i = 0; i < payload.Length; i++)
        {
            if (char.IsHighSurrogate(payload[i]))
            {
                if (i + 1 >= payload.Length || !char.IsLowSurrogate(payload[i + 1]))
                {
                    return true;
                }

                i++;
            }
            else if (char.IsLowSurrogate(payload[i]))
            {
                return true;
            }
        }

        return false;
    }

    private readonly record struct RawJsonProp(ToolIrJsonFieldToken? Token, int ByteStart, int ByteEnd, int DecodedLength, bool Known);

    private static (List<RawJsonProp> Props, int UnknownCount, List<int> ByteOffsets) ScanJson(string payload)
    {
        var props = new List<RawJsonProp>();
        var unknown = 0;
        var offsets = new List<int>();
        var trimmed = payload.TrimStart();
        if (!trimmed.StartsWith('{'))
        {
            return (props, 0, offsets);
        }

        try
        {
            var bytes = Encoding.UTF8.GetBytes(payload);
            var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { AllowTrailingCommas = true });
            var depth = 0;
            string? pendingName = null;
            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.StartObject:
                        depth++;
                        break;
                    case JsonTokenType.EndObject:
                        depth--;
                        break;
                    case JsonTokenType.PropertyName when depth == 1:
                        pendingName = reader.GetString();
                        break;
                    case JsonTokenType.String when depth == 1 && pendingName is not null:
                    {
                        var valueStartByte = (int)reader.TokenStartIndex + 1; // past opening quote
                        var decoded = reader.GetString() ?? string.Empty;
                        // TokenStartIndex points at opening quote; BytesConsumed after the token includes closing quote.
                        var valueEndByte = (int)reader.BytesConsumed - 1;
                        offsets.Add(valueStartByte);
                        offsets.Add(valueEndByte);
                        if (ToolIrResultShapeProbe.TryParseJsonFieldToken(pendingName, out var token))
                        {
                            props.Add(new RawJsonProp(token, valueStartByte, valueEndByte, decoded.Length, Known: true));
                        }
                        else
                        {
                            unknown++;
                            props.Add(new RawJsonProp(null, valueStartByte, valueEndByte, decoded.Length, Known: false));
                        }

                        pendingName = null;
                        break;
                    }
                    default:
                        if (reader.TokenType is not JsonTokenType.PropertyName)
                        {
                            pendingName = null;
                        }

                        break;
                }
            }
        }
        catch (JsonException)
        {
            return ([], 0, []);
        }

        return (props, unknown, offsets);
    }

    private static Dictionary<int, int> ConvertByteOffsetsToChar(string payload, List<int> byteOffsets)
    {
        var map = new Dictionary<int, int>();
        if (byteOffsets.Count == 0)
        {
            return map;
        }

        var sorted = byteOffsets.Distinct().OrderBy(x => x).ToList();
        var targetIdx = 0;
        var charIndex = 0;
        var byteIndex = 0;
        var utf8 = Encoding.UTF8;

        while (targetIdx < sorted.Count && charIndex < payload.Length)
        {
            while (targetIdx < sorted.Count && byteIndex == sorted[targetIdx])
            {
                map[sorted[targetIdx]] = charIndex;
                targetIdx++;
            }

            if (targetIdx >= sorted.Count)
            {
                break;
            }

            if (char.IsSurrogatePair(payload, charIndex))
            {
                byteIndex += 4;
                charIndex += 2;
            }
            else
            {
                byteIndex += utf8.GetByteCount(payload.AsSpan(charIndex, 1));
                charIndex++;
            }
        }

        while (targetIdx < sorted.Count && byteIndex == sorted[targetIdx])
        {
            map[sorted[targetIdx]] = charIndex;
            targetIdx++;
        }

        return map;
    }

    private static List<ToolIrJsonStringProperty> MapJsonPropsToCharOffsets(
        List<RawJsonProp> props,
        Dictionary<int, int> charOffsets)
    {
        var result = new List<ToolIrJsonStringProperty>();
        foreach (var prop in props)
        {
            if (!prop.Known || prop.Token is null)
            {
                continue;
            }

            if (!charOffsets.TryGetValue(prop.ByteStart, out var start) ||
                !charOffsets.TryGetValue(prop.ByteEnd, out var end))
            {
                continue;
            }

            result.Add(new ToolIrJsonStringProperty(prop.Token.Value, start, end, prop.DecodedLength));
        }

        return result;
    }

    private static List<ToolIrTagOccurrence> ScanTags(string payload)
    {
        var tags = new List<ToolIrTagOccurrence>();
        var i = 0;
        while (i < payload.Length)
        {
            if (payload[i] != '<')
            {
                i++;
                continue;
            }

            var start = i;
            i++;
            var isClose = false;
            if (i < payload.Length && payload[i] == '/')
            {
                isClose = true;
                i++;
            }

            var nameStart = i;
            while (i < payload.Length && (char.IsLetterOrDigit(payload[i]) || payload[i] is '_' or '-'))
            {
                i++;
            }

            if (i == nameStart)
            {
                continue;
            }

            var name = payload[nameStart..i];
            while (i < payload.Length && payload[i] != '>')
            {
                i++;
            }

            if (i >= payload.Length)
            {
                break;
            }

            i++; // past '>'
            var token = MapTagToken(name);
            tags.Add(new ToolIrTagOccurrence(token, isClose, start, i));
        }

        return tags;
    }

    private static ToolIrTagToken MapTagToken(string name)
    {
        if (!AllowlistedTags.Contains(name))
        {
            return ToolIrTagToken.Unknown;
        }

        return name.ToLowerInvariant() switch
        {
            "path" => ToolIrTagToken.Path,
            "type" => ToolIrTagToken.Type,
            "notice" => ToolIrTagToken.Notice,
            "file" => ToolIrTagToken.File,
            "error" => ToolIrTagToken.Error,
            "warning" => ToolIrTagToken.Warning,
            "lines" => ToolIrTagToken.Lines,
            "truncated" => ToolIrTagToken.Truncated,
            "content" => ToolIrTagToken.Content,
            _ => ToolIrTagToken.Unknown
        };
    }

    private readonly record struct LineStats(
        int LineCount,
        int[] LineLengths,
        int MaxLineLength,
        int Colon,
        int Pipe,
        int Unprefixed,
        int? FirstPrefixLine);

    private static LineStats MeasureLines(string payload, int maxSampleLines)
    {
        var normalized = payload.Replace("\r\n", "\n", StringComparison.Ordinal);
        var lines = normalized.Split('\n');
        var lengths = new List<int>(Math.Min(lines.Length, maxSampleLines));
        var maxLen = 0;
        var colon = 0;
        var pipe = 0;
        var unprefixed = 0;
        int? firstPrefix = null;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (lengths.Count < maxSampleLines)
            {
                lengths.Add(line.Length);
            }

            if (line.Length > maxLen)
            {
                maxLen = line.Length;
            }

            if (line.Length == 0)
            {
                continue;
            }

            if (TryPrefix(line, out var style, out var lineNumber))
            {
                firstPrefix ??= lineNumber;
                if (style == ToolIrLinePrefixStyle.Colon)
                {
                    colon++;
                }
                else
                {
                    pipe++;
                }
            }
            else
            {
                unprefixed++;
            }
        }

        return new LineStats(lines.Length, lengths.ToArray(), maxLen, colon, pipe, unprefixed, firstPrefix);
    }

    private static bool TryPrefix(string line, out ToolIrLinePrefixStyle style, out int lineNumber)
    {
        style = ToolIrLinePrefixStyle.None;
        lineNumber = 0;
        var i = 0;
        while (i < line.Length && char.IsAsciiDigit(line[i]))
        {
            i++;
        }

        if (i == 0 || i >= line.Length || !int.TryParse(line.AsSpan(0, i), out lineNumber))
        {
            return false;
        }

        if (line[i] == ':')
        {
            style = ToolIrLinePrefixStyle.Colon;
            return true;
        }

        if (line[i] == '|')
        {
            style = ToolIrLinePrefixStyle.Pipe;
            return true;
        }

        return false;
    }

    private static (bool Present, int? Total) DetectFooter(string payload)
    {
        var normalized = payload.Replace("\r\n", "\n", StringComparison.Ordinal);
        var lines = normalized.Split('\n');
        var end = lines.Length;
        while (end > 0 && lines[end - 1].Length == 0)
        {
            end--;
        }

        if (end == 0)
        {
            return (false, null);
        }

        var last = lines[end - 1].Trim();
        if (!last.Contains("Showing lines", StringComparison.OrdinalIgnoreCase))
        {
            return (false, null);
        }

        int? total = null;
        var match = FooterTotalRegex.Match(last);
        if (match.Success && int.TryParse(match.Groups[1].Value, out var n))
        {
            total = n;
        }

        return (true, total);
    }

    private static string? ExtractExtensionToken(string payload)
    {
        // Look for a path-like allowlisted <path>…</path> and take its extension only.
        var open = payload.IndexOf("<path>", StringComparison.OrdinalIgnoreCase);
        if (open < 0)
        {
            return null;
        }

        var start = open + 6;
        var close = payload.IndexOf("</path>", start, StringComparison.OrdinalIgnoreCase);
        if (close < 0)
        {
            return null;
        }

        var path = payload[start..close].Trim();
        var ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        return ExtensionRegex.IsMatch(ext) ? ext : null;
    }

    private static ToolIrShapeBodySpan? BuildObservedBody(
        string payload,
        ToolIrResultDistiller.ExtractedFileBody heuristic,
        IReadOnlyList<ToolIrJsonStringProperty> jsonProps,
        IReadOnlyList<ToolIrTagOccurrence> tags)
    {
        // Prefer tagged content span when present.
        ToolIrTagOccurrence? open = null;
        ToolIrTagOccurrence? close = null;
        foreach (var tag in tags)
        {
            if (tag.Token != ToolIrTagToken.Content)
            {
                continue;
            }

            if (!tag.IsClose)
            {
                open ??= tag;
            }
            else
            {
                close = tag;
            }
        }

        ToolIrLinePrefixStyle prefix;
        if (heuristic.HadLinePrefixes)
        {
            // Derive colon vs pipe from counts on the payload.
            var colon = 0;
            var pipe = 0;
            foreach (var line in payload.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
            {
                if (TryPrefix(line, out var style, out _))
                {
                    if (style == ToolIrLinePrefixStyle.Colon)
                    {
                        colon++;
                    }
                    else
                    {
                        pipe++;
                    }
                }
            }

            prefix = colon >= pipe ? ToolIrLinePrefixStyle.Colon : ToolIrLinePrefixStyle.Pipe;
        }
        else
        {
            prefix = ToolIrLinePrefixStyle.None;
        }

        if (open is not null && close is not null && close.Value.StartOffset >= open.Value.EndOffset)
        {
            return new ToolIrShapeBodySpan(
                open.Value.EndOffset,
                close.Value.StartOffset - open.Value.EndOffset,
                heuristic.FirstLineNumber,
                prefix);
        }

        if (jsonProps.Count == 1)
        {
            var prop = jsonProps[0];
            return new ToolIrShapeBodySpan(
                prop.ValueStartOffset,
                prop.ValueEndOffset - prop.ValueStartOffset,
                heuristic.FirstLineNumber,
                prefix);
        }

        return new ToolIrShapeBodySpan(0, payload.Length, heuristic.FirstLineNumber, prefix);
    }
}
