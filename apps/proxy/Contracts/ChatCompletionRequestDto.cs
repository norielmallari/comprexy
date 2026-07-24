using System.Text.Json.Serialization;
using Comprexy.Api.Serialization;

namespace Comprexy.Api.Contracts;

/// <summary>
/// OpenAI-compatible <c>/v1/chat/completions</c> request body. When <c>Provider:Model</c> is
/// set, Comprexy replaces <see cref="Model"/> with that value; when it is null, the client's
/// model is forwarded upstream.
/// </summary>
public sealed class ChatCompletionRequestDto
{
    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("messages")]
    public List<ChatMessageDto> Messages { get; set; } = [];

    [JsonPropertyName("stream")]
    public bool Stream { get; set; }

    [JsonPropertyName("temperature")]
    public double? Temperature { get; set; }

    [JsonPropertyName("top_p")]
    public double? TopP { get; set; }

    [JsonPropertyName("max_tokens")]
    public int? MaxTokens { get; set; }

    /// <summary>Single string or string array, per the OpenAI wire format.</summary>
    [JsonPropertyName("stop")]
    [JsonConverter(typeof(StopSequencesJsonConverter))]
    public List<string>? Stop { get; set; }
}
