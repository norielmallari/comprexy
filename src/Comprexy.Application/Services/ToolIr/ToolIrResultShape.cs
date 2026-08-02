using System.Text.Json;
using System.Text.Json.Serialization;

namespace Comprexy.Application.Services.ToolIr;

public enum ToolIrEnvelopeKind
{
    [JsonStringEnumMemberName("tagged_content")]
    TaggedContent,
    [JsonStringEnumMemberName("json_field")]
    JsonField,
    [JsonStringEnumMemberName("plain")]
    Plain
}

public enum ToolIrJsonFieldToken
{
    [JsonStringEnumMemberName("contents")]
    Contents,
    [JsonStringEnumMemberName("content")]
    Content,
    [JsonStringEnumMemberName("text")]
    Text,
    [JsonStringEnumMemberName("data")]
    Data,
    [JsonStringEnumMemberName("result")]
    Result
}

public enum ToolIrLinePrefixStyle
{
    [JsonStringEnumMemberName("colon")]
    Colon,
    [JsonStringEnumMemberName("pipe")]
    Pipe,
    [JsonStringEnumMemberName("none")]
    None
}

public enum ToolIrShapeConfidence
{
    Unambiguous,
    Ambiguous
}

public enum ToolIrShapeSource
{
    [JsonStringEnumMemberName("probe")]
    Probe,
    [JsonStringEnumMemberName("learner")]
    Learner
}

/// <summary>Closed descriptor for how a client tool result encodes a file body.</summary>
public sealed class ToolIrResultShape
{
    [JsonPropertyName("envelope")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ToolIrEnvelopeKind Envelope { get; set; }

    [JsonPropertyName("json_field")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ToolIrJsonFieldToken? JsonField { get; set; }

    [JsonPropertyName("line_prefix")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ToolIrLinePrefixStyle LinePrefix { get; set; }

    [JsonPropertyName("source")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ToolIrShapeSource Source { get; set; }

    [JsonPropertyName("samples")]
    public int Samples { get; set; } = 1;

    [JsonPropertyName("observed_at")]
    public DateTimeOffset ObservedAt { get; set; }

    public static bool TryExtractBody(
        string payload,
        ToolIrResultShape descriptor,
        out string body,
        out int? firstLineNumber)
    {
        body = string.Empty;
        firstLineNumber = null;

        switch (descriptor.Envelope)
        {
            case ToolIrEnvelopeKind.TaggedContent:
            {
                if (!ToolIrResultDistiller.TryExtractTaggedContent(payload, out body))
                {
                    return false;
                }

                break;
            }
            case ToolIrEnvelopeKind.JsonField:
            {
                if (descriptor.JsonField is null)
                {
                    return false;
                }

                var fieldName = ToolIrResultShapeProbe.JsonFieldName(descriptor.JsonField.Value);
                try
                {
                    using var document = JsonDocument.Parse(payload);
                    if (document.RootElement.ValueKind != JsonValueKind.Object ||
                        !document.RootElement.TryGetProperty(fieldName, out var value) ||
                        value.ValueKind != JsonValueKind.String)
                    {
                        return false;
                    }

                    body = value.GetString() ?? string.Empty;
                }
                catch (JsonException)
                {
                    return false;
                }

                break;
            }
            case ToolIrEnvelopeKind.Plain:
                body = payload;
                break;
            default:
                return false;
        }

        var stripped = ToolIrResultDistiller.StripReadLinePrefixes(body);
        body = stripped.Body;
        firstLineNumber = stripped.FirstLineNumber;

        // Prefix style attestation must match TryReplaySpan: strict equality on LinePrefix
        // (including None → derived must be None). Soft acceptance of prefixed bodies under a
        // None descriptor would apply shapes that later fail the promote replay gate.
        var derived = DeriveLivePrefixStyle(payload, descriptor.Envelope, descriptor.JsonField);
        if (descriptor.LinePrefix != derived)
        {
            return false;
        }

        return true;
    }

    private static ToolIrLinePrefixStyle DeriveLivePrefixStyle(
        string payload,
        ToolIrEnvelopeKind envelope,
        ToolIrJsonFieldToken? jsonField)
    {
        var text = payload;
        if (envelope == ToolIrEnvelopeKind.TaggedContent &&
            ToolIrResultDistiller.TryExtractTaggedContent(payload, out var tagged))
        {
            text = tagged;
        }
        else if (envelope == ToolIrEnvelopeKind.JsonField && jsonField is not null)
        {
            try
            {
                using var document = JsonDocument.Parse(payload);
                var name = ToolIrResultShapeProbe.JsonFieldName(jsonField.Value);
                if (document.RootElement.TryGetProperty(name, out var value) &&
                    value.ValueKind == JsonValueKind.String)
                {
                    text = value.GetString() ?? string.Empty;
                }
            }
            catch (JsonException)
            {
                // keep payload
            }
        }

        var extracted = ToolIrResultDistiller.StripReadLinePrefixes(text);
        if (!extracted.HadLinePrefixes)
        {
            return ToolIrLinePrefixStyle.None;
        }

        // Distinguish colon vs pipe from the first prefixed line in the raw text.
        foreach (var line in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var i = 0;
            while (i < line.Length && char.IsAsciiDigit(line[i]))
            {
                i++;
            }

            if (i == 0 || i >= line.Length)
            {
                continue;
            }

            if (line[i] == ':')
            {
                return ToolIrLinePrefixStyle.Colon;
            }

            if (line[i] == '|')
            {
                return ToolIrLinePrefixStyle.Pipe;
            }
        }

        return ToolIrLinePrefixStyle.None;
    }

    public static bool TryReplaySpan(
        ToolIrShapeFeatures features,
        ToolIrResultShape descriptor,
        out ToolIrShapeBodySpan span,
        out string reason)
    {
        span = default;
        reason = string.Empty;

        int start;
        int length;
        switch (descriptor.Envelope)
        {
            case ToolIrEnvelopeKind.TaggedContent:
            {
                ToolIrTagOccurrence? firstOpen = null;
                ToolIrTagOccurrence? lastClose = null;
                foreach (var tag in features.Tags)
                {
                    if (tag.Token != ToolIrTagToken.Content)
                    {
                        continue;
                    }

                    if (!tag.IsClose)
                    {
                        firstOpen ??= tag;
                    }
                    else
                    {
                        lastClose = tag;
                    }
                }

                if (firstOpen is null || lastClose is null)
                {
                    reason = "not_attested";
                    return false;
                }

                start = firstOpen.Value.EndOffset;
                var end = lastClose.Value.StartOffset;
                if (end < start)
                {
                    reason = "not_attested";
                    return false;
                }

                length = end - start;
                break;
            }
            case ToolIrEnvelopeKind.JsonField:
            {
                if (descriptor.JsonField is null)
                {
                    reason = "not_attested";
                    return false;
                }

                ToolIrJsonStringProperty? match = null;
                var count = 0;
                foreach (var prop in features.JsonStringProperties)
                {
                    if (prop.Token == descriptor.JsonField.Value)
                    {
                        match = prop;
                        count++;
                    }
                }

                if (count != 1 || match is null)
                {
                    reason = "not_attested";
                    return false;
                }

                start = match.Value.ValueStartOffset;
                length = match.Value.ValueEndOffset - match.Value.ValueStartOffset;
                break;
            }
            case ToolIrEnvelopeKind.Plain:
                start = 0;
                length = features.PayloadLength;
                break;
            default:
                reason = "not_closed_set";
                return false;
        }

        var derivedPrefix = DerivePrefixFromCounts(features);
        if (descriptor.LinePrefix != derivedPrefix)
        {
            reason = "prefix_disagrees_with_features";
            return false;
        }

        var firstLine = derivedPrefix == ToolIrLinePrefixStyle.None
            ? null
            : features.FirstPrefixLineNumber;
        span = new ToolIrShapeBodySpan(start, length, firstLine, derivedPrefix);
        return true;
    }

    private static ToolIrLinePrefixStyle DerivePrefixFromCounts(ToolIrShapeFeatures features)
    {
        var prefixed = features.ColonPrefixedLineCount + features.PipePrefixedLineCount;
        var nonEmpty = prefixed + features.UnprefixedNonEmptyLineCount;
        if (nonEmpty == 0 || prefixed * 2 < nonEmpty)
        {
            return ToolIrLinePrefixStyle.None;
        }

        return features.ColonPrefixedLineCount >= features.PipePrefixedLineCount
            ? ToolIrLinePrefixStyle.Colon
            : ToolIrLinePrefixStyle.Pipe;
    }
}

public readonly record struct ToolIrShapeBodySpan(
    int Start,
    int Length,
    int? FirstLineNumber,
    ToolIrLinePrefixStyle Prefix);

/// <summary>Pure classification helper for first-result shape probing.</summary>
public static class ToolIrResultShapeProbe
{
    private static readonly string[] JsonFieldNames = ["contents", "content", "text", "data", "result"];

    public static (ToolIrResultShape Descriptor, ToolIrShapeConfidence Confidence) Classify(string payload)
    {
        var now = DateTimeOffset.UtcNow;
        var hasTaggedAttested = ToolIrResultDistiller.TryExtractTaggedContent(payload, out _);
        var hasContentTag = payload.Contains("<content>", StringComparison.OrdinalIgnoreCase);
        var jsonFields = CollectJsonStringFields(payload);
        var prefixStats = MeasureLinePrefixes(payload);

        var ambiguous =
            jsonFields.Count > 1 ||
            (hasTaggedAttested && jsonFields.Count >= 1) ||
            (hasContentTag && !hasTaggedAttested) ||
            IsBorderlinePrefixMajority(prefixStats);

        ToolIrEnvelopeKind envelope;
        ToolIrJsonFieldToken? jsonField = null;
        if (hasTaggedAttested && jsonFields.Count == 0)
        {
            envelope = ToolIrEnvelopeKind.TaggedContent;
        }
        else if (jsonFields.Count == 1 && !hasTaggedAttested)
        {
            envelope = ToolIrEnvelopeKind.JsonField;
            jsonField = jsonFields[0];
        }
        else if (hasTaggedAttested)
        {
            envelope = ToolIrEnvelopeKind.TaggedContent;
        }
        else if (jsonFields.Count >= 1)
        {
            envelope = ToolIrEnvelopeKind.JsonField;
            jsonField = jsonFields[0];
        }
        else
        {
            envelope = ToolIrEnvelopeKind.Plain;
        }

        var descriptor = new ToolIrResultShape
        {
            Envelope = envelope,
            JsonField = jsonField,
            LinePrefix = DerivePrefixStyle(prefixStats),
            Source = ToolIrShapeSource.Probe,
            Samples = 1,
            ObservedAt = now
        };

        return (descriptor, ambiguous ? ToolIrShapeConfidence.Ambiguous : ToolIrShapeConfidence.Unambiguous);
    }

    private static List<ToolIrJsonFieldToken> CollectJsonStringFields(string payload)
    {
        var found = new List<ToolIrJsonFieldToken>();
        var trimmed = payload.TrimStart();
        if (!trimmed.StartsWith('{'))
        {
            return found;
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return found;
            }

            foreach (var name in JsonFieldNames)
            {
                if (document.RootElement.TryGetProperty(name, out var value) &&
                    value.ValueKind == JsonValueKind.String &&
                    TryParseJsonFieldToken(name, out var token))
                {
                    found.Add(token);
                }
            }
        }
        catch (JsonException)
        {
            // not JSON
        }

        return found;
    }

    internal static bool TryParseJsonFieldToken(string name, out ToolIrJsonFieldToken token)
    {
        token = default;
        if (string.Equals(name, "contents", StringComparison.OrdinalIgnoreCase))
        {
            token = ToolIrJsonFieldToken.Contents;
            return true;
        }

        if (string.Equals(name, "content", StringComparison.OrdinalIgnoreCase))
        {
            token = ToolIrJsonFieldToken.Content;
            return true;
        }

        if (string.Equals(name, "text", StringComparison.OrdinalIgnoreCase))
        {
            token = ToolIrJsonFieldToken.Text;
            return true;
        }

        if (string.Equals(name, "data", StringComparison.OrdinalIgnoreCase))
        {
            token = ToolIrJsonFieldToken.Data;
            return true;
        }

        if (string.Equals(name, "result", StringComparison.OrdinalIgnoreCase))
        {
            token = ToolIrJsonFieldToken.Result;
            return true;
        }

        return false;
    }

    internal static string JsonFieldName(ToolIrJsonFieldToken token) => token switch
    {
        ToolIrJsonFieldToken.Contents => "contents",
        ToolIrJsonFieldToken.Content => "content",
        ToolIrJsonFieldToken.Text => "text",
        ToolIrJsonFieldToken.Data => "data",
        ToolIrJsonFieldToken.Result => "result",
        _ => "content"
    };

    private readonly record struct PrefixStats(int Prefixed, int NonEmpty, int Colon, int Pipe, int? FirstLine);

    private static PrefixStats MeasureLinePrefixes(string payload)
    {
        var normalized = payload.Replace("\r\n", "\n", StringComparison.Ordinal);
        var prefixed = 0;
        var nonEmpty = 0;
        var colon = 0;
        var pipe = 0;
        int? firstLine = null;
        foreach (var line in normalized.Split('\n'))
        {
            if (line.Length == 0)
            {
                continue;
            }

            nonEmpty++;
            if (TryDetectPrefix(line, out var style, out var lineNumber))
            {
                prefixed++;
                firstLine ??= lineNumber;
                if (style == ToolIrLinePrefixStyle.Colon)
                {
                    colon++;
                }
                else if (style == ToolIrLinePrefixStyle.Pipe)
                {
                    pipe++;
                }
            }
        }

        return new PrefixStats(prefixed, nonEmpty, colon, pipe, firstLine);
    }

    private static bool IsBorderlinePrefixMajority(PrefixStats stats)
    {
        // Only borderline when some lines carry prefixes and the majority vote is within one of flipping.
        if (stats.NonEmpty == 0 || stats.Prefixed == 0)
        {
            return false;
        }

        return Math.Abs(2 * stats.Prefixed - stats.NonEmpty) <= 1;
    }

    private static ToolIrLinePrefixStyle DerivePrefixStyle(PrefixStats stats)
    {
        if (stats.NonEmpty == 0 || stats.Prefixed * 2 < stats.NonEmpty)
        {
            return ToolIrLinePrefixStyle.None;
        }

        return stats.Colon >= stats.Pipe ? ToolIrLinePrefixStyle.Colon : ToolIrLinePrefixStyle.Pipe;
    }

    private static bool TryDetectPrefix(string line, out ToolIrLinePrefixStyle style, out int lineNumber)
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
}
