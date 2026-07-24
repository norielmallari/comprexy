using System.Text.Json.Serialization;

namespace Comprexy.Infrastructure.Hosting;

public sealed class ErrorResponseDto
{
    [JsonPropertyName("error")]
    public ErrorDetailDto Error { get; set; } = new();
}

public sealed class ErrorDetailDto
{
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = "invalid_request_error";
}
