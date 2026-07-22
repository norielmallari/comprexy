using System.Text;
using System.Text.Json;
using Comprexy.Application.Abstractions;
using Comprexy.Application.Configuration;
using Comprexy.Application.Models;
using Comprexy.Application.Services;
using Microsoft.Extensions.Options;
using Microsoft.ML.Tokenizers;

namespace Comprexy.Infrastructure.Tokenization;

/// <summary>
/// Approximates token counts using a Tiktoken BPE encoding. Counts full wire message JSON when
/// available, plus request-level prompt fields (tools, etc.), not just extracted text.
/// Image payloads use OpenAI-style vision tile estimates — base64 is never BPE-tokenized.
/// Individual string and per-message counts are memoized via <see cref="ITokenEstimateCache"/>.
/// </summary>
public class TiktokenTokenEstimator : ITokenEstimator
{
    /// <summary>Approximate per-message formatting overhead when only plain text is available.</summary>
    private const int PerMessageOverheadTokens = 4;

    private static readonly string[] PromptSideProperties =
    [
        "tools",
        "functions",
        "tool_choice",
        "response_format"
    ];

    private readonly Tokenizer _tokenizer;
    private readonly ITokenEstimateCache _tokenEstimateCache;

    public TiktokenTokenEstimator(
        IOptions<ContextPolicyOptions> policy,
        ITokenEstimateCache tokenEstimateCache)
    {
        _tokenizer = TiktokenTokenizer.CreateForEncoding(policy.Value.TokenizerEncoding);
        _tokenEstimateCache = tokenEstimateCache;
    }

    public int CountTokens(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var key = TokenEstimateCache.ComputeStringKey(text);
        return _tokenEstimateCache.GetOrCompute(key, () => CountStringTokensUncached(text));
    }

    public int CountTokens(IEnumerable<ChatMessage> messages) =>
        messages.Sum(CountMessageTokens);

    public int CountPromptTokens(IEnumerable<ChatMessage> messages, JsonElement? requestRoot = null)
    {
        var total = CountTokens(messages);
        if (requestRoot is not { ValueKind: JsonValueKind.Object } root)
        {
            return total;
        }

        foreach (var propertyName in PromptSideProperties)
        {
            if (root.TryGetProperty(propertyName, out var value) &&
                value.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
            {
                total += CountTokens(value.GetRawText());
            }
        }

        return total;
    }

    private int CountMessageTokens(ChatMessage message)
    {
        var key = TokenEstimateCache.ComputeMessageKey(message);
        return _tokenEstimateCache.GetOrCompute(key, () => CountMessageTokensUncached(message));
    }

    private int CountMessageTokensUncached(ChatMessage message)
    {
        if (message.RawWireMessage is { ValueKind: JsonValueKind.Object } raw)
        {
            return CountWireMessageTokens(raw);
        }

        var contentTokens = string.IsNullOrEmpty(message.Content)
            ? 0
            : CountStringTokensUncached(message.Content);
        return contentTokens + PerMessageOverheadTokens;
    }

    private int CountStringTokensUncached(string text)
    {
        if (VisionImageTokenEstimator.IsDataImageUrl(text) ||
            text.Contains("data:image/", StringComparison.OrdinalIgnoreCase))
        {
            return CountTextWithEmbeddedDataImages(text);
        }

        return _tokenizer.CountTokens(text);
    }

    private int CountWireMessageTokens(JsonElement raw)
    {
        if (!raw.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
        {
            var wire = raw.GetRawText();
            return string.IsNullOrEmpty(wire) ? 0 : _tokenizer.CountTokens(wire);
        }

        var imageTokens = 0;
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var property in raw.EnumerateObject())
            {
                if (property.NameEquals("content"))
                {
                    writer.WritePropertyName("content");
                    WriteSanitizedContentArray(property.Value, writer, ref imageTokens);
                }
                else
                {
                    property.WriteTo(writer);
                }
            }

            writer.WriteEndObject();
        }

        var sanitized = Encoding.UTF8.GetString(stream.ToArray());
        return _tokenizer.CountTokens(sanitized) + imageTokens;
    }

    private static void WriteSanitizedContentArray(
        JsonElement contentArray,
        Utf8JsonWriter writer,
        ref int imageTokens)
    {
        writer.WriteStartArray();
        foreach (var part in contentArray.EnumerateArray())
        {
            if (part.ValueKind == JsonValueKind.Object &&
                part.TryGetProperty("type", out var type) &&
                type.ValueKind == JsonValueKind.String &&
                string.Equals(type.GetString(), "image_url", StringComparison.Ordinal) &&
                part.TryGetProperty("image_url", out var imageUrl))
            {
                var (url, detail) = ReadImageUrlFields(imageUrl);
                imageTokens += VisionImageTokenEstimator.Estimate(url, detail);
                writer.WriteStartObject();
                writer.WriteString("type", "image_url");
                writer.WritePropertyName("image_url");
                writer.WriteStartObject();
                writer.WriteString("url", VisionImageTokenEstimator.RedactImageUrlForText(url));
                if (detail is not null)
                {
                    writer.WriteString("detail", detail);
                }

                writer.WriteEndObject();
                writer.WriteEndObject();
                continue;
            }

            part.WriteTo(writer);
        }

        writer.WriteEndArray();
    }

    private static (string? Url, string? Detail) ReadImageUrlFields(JsonElement imageUrl)
    {
        if (imageUrl.ValueKind == JsonValueKind.String)
        {
            return (imageUrl.GetString(), null);
        }

        if (imageUrl.ValueKind != JsonValueKind.Object)
        {
            return (null, null);
        }

        string? url = null;
        string? detail = null;
        if (imageUrl.TryGetProperty("url", out var urlElement) && urlElement.ValueKind == JsonValueKind.String)
        {
            url = urlElement.GetString();
        }

        if (imageUrl.TryGetProperty("detail", out var detailElement) &&
            detailElement.ValueKind == JsonValueKind.String)
        {
            detail = detailElement.GetString();
        }

        return (url, detail);
    }

    private int CountTextWithEmbeddedDataImages(string text)
    {
        var total = 0;
        var span = text.AsSpan();
        var start = 0;
        while (start < span.Length)
        {
            var relative = span[start..].IndexOf("data:image/", StringComparison.OrdinalIgnoreCase);
            if (relative < 0)
            {
                var remainder = span[start..].ToString();
                if (remainder.Length > 0)
                {
                    total += _tokenizer.CountTokens(remainder);
                }

                break;
            }

            var dataStart = start + relative;
            if (dataStart > start)
            {
                total += _tokenizer.CountTokens(span[start..dataStart].ToString());
            }

            var fromData = span[dataStart..];
            var endRel = IndexOfDataUrlEnd(fromData);
            var dataUrl = fromData[..endRel].ToString();
            total += VisionImageTokenEstimator.Estimate(dataUrl, detail: null);
            total += _tokenizer.CountTokens("[image]");
            start = dataStart + endRel;
        }

        return total;
    }

    private static int IndexOfDataUrlEnd(ReadOnlySpan<char> fromData)
    {
        // data URLs in extracted text usually run to whitespace, quote, or end.
        for (var i = 0; i < fromData.Length; i++)
        {
            var c = fromData[i];
            if (char.IsWhiteSpace(c) || c is '"' or '\'' or ')' or ']' or '>')
            {
                return i;
            }
        }

        return fromData.Length;
    }
}
