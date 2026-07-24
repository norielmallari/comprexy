using System.Text.Json;

namespace Comprexy.Api.Streaming;

/// <summary>
/// Writes an OpenAI-compatible SSE error payload after a streaming response has already started,
/// so clients are not left with a silently truncated stream.
/// </summary>
public static class SseStreamErrorWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public static string FormatErrorData(string message, string type = "upstream_error")
    {
        var payload = new
        {
            error = new
            {
                message,
                type
            }
        };

        return JsonSerializer.Serialize(payload, SerializerOptions);
    }

    public static async Task TryWriteAsync(
        HttpResponse response,
        string message,
        CancellationToken cancellationToken,
        string type = "upstream_error")
    {
        try
        {
            var data = FormatErrorData(message, type);
            await response.WriteAsync($"data: {data}\n\n", cancellationToken);
            await response.WriteAsync("data: [DONE]\n\n", cancellationToken);
            await response.Body.FlushAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Client disconnected or response stream unusable; nothing more we can send.
        }
    }
}
