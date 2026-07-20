using System.Text;
using System.Text.Json;

namespace Comprexy.Application.OpenAi;

/// <summary>
/// Extracts plain text from OpenAI message <c>content</c>, which may be a string or a
/// multimodal content-part array.
/// </summary>
public static class OpenAiMessageContent
{
    public static string ExtractText(JsonElement content)
    {
        return content.ValueKind switch
        {
            JsonValueKind.Null => string.Empty,
            JsonValueKind.Undefined => string.Empty,
            JsonValueKind.String => content.GetString() ?? string.Empty,
            JsonValueKind.Array => ExtractFromArray(content),
            _ => content.GetRawText()
        };
    }

    private static string ExtractFromArray(JsonElement array)
    {
        var builder = new StringBuilder();
        foreach (var part in array.EnumerateArray())
        {
            AppendPart(builder, ExtractPartText(part));
        }

        return builder.ToString();
    }

    private static string? ExtractPartText(JsonElement part)
    {
        if (part.ValueKind == JsonValueKind.String)
        {
            return part.GetString();
        }

        if (part.ValueKind != JsonValueKind.Object)
        {
            return part.ToString();
        }

        var type = part.TryGetProperty("type", out var typeElement) && typeElement.ValueKind == JsonValueKind.String
            ? typeElement.GetString()
            : null;

        return type switch
        {
            "text" when part.TryGetProperty("text", out var text) => text.GetString(),
            "input_text" when part.TryGetProperty("text", out var inputText) => inputText.GetString(),
            "output_text" when part.TryGetProperty("text", out var outputText) => outputText.GetString(),
            "image_url" when part.TryGetProperty("image_url", out var imageUrl) => FormatImageUrl(imageUrl),
            "image_url" => "[image]",
            _ when part.TryGetProperty("text", out var fallbackText) => fallbackText.GetString(),
            _ => part.GetRawText()
        };
    }

    private static string FormatImageUrl(JsonElement imageUrl)
    {
        if (imageUrl.ValueKind == JsonValueKind.String)
        {
            return $"[image: {imageUrl.GetString()}]";
        }

        if (imageUrl.ValueKind == JsonValueKind.Object &&
            imageUrl.TryGetProperty("url", out var url) &&
            url.ValueKind == JsonValueKind.String)
        {
            return $"[image: {url.GetString()}]";
        }

        return "[image]";
    }

    private static void AppendPart(StringBuilder builder, string? part)
    {
        if (string.IsNullOrEmpty(part))
        {
            return;
        }

        if (builder.Length > 0)
        {
            builder.Append('\n');
        }

        builder.Append(part);
    }
}
