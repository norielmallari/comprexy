using System.Text.Json;
using System.Text.Json.Serialization;

namespace Comprexy.Api.Serialization;

/// <summary>
/// OpenAI allows <c>stop</c> to be either a single string or an array of strings.
/// </summary>
public sealed class StopSequencesJsonConverter : JsonConverter<List<string>?>
{
    public override List<string>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.Null => null,
            JsonTokenType.String =>
            [
                reader.GetString() ?? string.Empty
            ],
            JsonTokenType.StartArray => JsonSerializer.Deserialize<List<string>>(ref reader, options),
            _ => throw new JsonException($"Unexpected token type for stop sequences: {reader.TokenType}.")
        };
    }

    public override void Write(Utf8JsonWriter writer, List<string>? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        JsonSerializer.Serialize(writer, value, options);
    }
}
