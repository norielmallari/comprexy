using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Comprexy.Api.Serialization;

/// <summary>
/// OpenAI allows <c>message.content</c> to be either a plain string or an array of content parts
/// (text, image_url, etc). Clients like Kilo Code send the array form even for text-only turns.
/// This converter normalizes both shapes into a single string for Comprexy's internal model.
/// </summary>
public sealed class ChatMessageContentJsonConverter : JsonConverter<string>
{
    public override bool HandleNull => true;

    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.Null => string.Empty,
            JsonTokenType.String => reader.GetString() ?? string.Empty,
            JsonTokenType.StartArray => ReadContentParts(ref reader),
            _ => throw new JsonException($"Unexpected token type for message content: {reader.TokenType}.")
        };
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value);
    }

    private static string ReadContentParts(ref Utf8JsonReader reader)
    {
        var builder = new StringBuilder();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray)
            {
                break;
            }

            if (reader.TokenType == JsonTokenType.String)
            {
                AppendPart(builder, reader.GetString());
                continue;
            }

            if (reader.TokenType != JsonTokenType.StartObject)
            {
                reader.Skip();
                continue;
            }

            using var part = JsonDocument.ParseValue(ref reader);
            AppendPart(builder, ExtractPartText(part.RootElement));
        }

        return builder.ToString();
    }

    private static string? ExtractPartText(JsonElement part)
    {
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
