using System.Text.Json.Serialization;
using Comprexy.Api.Serialization;

namespace Comprexy.Api.Contracts;

public sealed class ChatMessageDto
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    /// <summary>
    /// Plain string or OpenAI multimodal content-part array; both are accepted and normalized to text.
    /// </summary>
    [JsonPropertyName("content")]
    [JsonConverter(typeof(ChatMessageContentJsonConverter))]
    public string Content { get; set; } = string.Empty;
}
