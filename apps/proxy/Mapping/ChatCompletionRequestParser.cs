using System.Text.Json;
using Comprexy.Application.Models;
using Comprexy.Application.OpenAi;
using Comprexy.Domain.Enums;

namespace Comprexy.Api.Mapping;

/// <summary>
/// Parses an OpenAI-compatible chat completion request while retaining the original JSON so
/// unknown fields can be forwarded upstream unchanged.
/// </summary>
public static class ChatCompletionRequestParser
{
    public static IncomingChatRequest Parse(JsonElement rawRequest, string? conversationIdHeader)
    {
        if (rawRequest.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("Request body must be a JSON object.");
        }

        if (!rawRequest.TryGetProperty("messages", out var messagesElement) ||
            messagesElement.ValueKind != JsonValueKind.Array ||
            messagesElement.GetArrayLength() == 0)
        {
            throw new ArgumentException("The 'messages' field must contain at least one message.");
        }

        var messages = new List<ChatMessage>();
        foreach (var messageElement in messagesElement.EnumerateArray())
        {
            if (messageElement.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException("Each message must be a JSON object.");
            }

            if (!messageElement.TryGetProperty("role", out var roleElement) ||
                roleElement.ValueKind != JsonValueKind.String)
            {
                throw new ArgumentException("Each message must include a string 'role'.");
            }

            var role = ToDomainRole(roleElement.GetString()!);
            var contentElement = messageElement.TryGetProperty("content", out var content)
                ? content
                : default;
            var text = OpenAiMessageContent.ExtractText(contentElement);
            messages.Add(new ChatMessage(role, text, messageElement.Clone()));
        }

        var stream = rawRequest.TryGetProperty("stream", out var streamElement) &&
                     streamElement.ValueKind is JsonValueKind.True;

        return new IncomingChatRequest(
            messages,
            conversationIdHeader,
            stream,
            rawRequest.Clone(),
            ExtractCallOptions(rawRequest));
    }

    private static ChatCompletionCallOptions ExtractCallOptions(JsonElement rawRequest)
    {
        double? temperature = rawRequest.TryGetProperty("temperature", out var temperatureElement) &&
                              temperatureElement.ValueKind == JsonValueKind.Number
            ? temperatureElement.GetDouble()
            : null;

        double? topP = rawRequest.TryGetProperty("top_p", out var topPElement) &&
                       topPElement.ValueKind == JsonValueKind.Number
            ? topPElement.GetDouble()
            : null;

        int? maxTokens = rawRequest.TryGetProperty("max_tokens", out var maxTokensElement) &&
                         maxTokensElement.ValueKind == JsonValueKind.Number
            ? maxTokensElement.GetInt32()
            : null;

        List<string>? stop = null;
        if (rawRequest.TryGetProperty("stop", out var stopElement))
        {
            stop = stopElement.ValueKind switch
            {
                JsonValueKind.String => [stopElement.GetString() ?? string.Empty],
                JsonValueKind.Array => stopElement.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.String)
                    .Select(item => item.GetString() ?? string.Empty)
                    .ToList(),
                _ => null
            };
        }

        return new ChatCompletionCallOptions(temperature, topP, maxTokens, stop);
    }

    private static MessageRole ToDomainRole(string role) => role.ToLowerInvariant() switch
    {
        "system" => MessageRole.System,
        "user" => MessageRole.User,
        "assistant" => MessageRole.Assistant,
        "tool" => MessageRole.Tool,
        _ => throw new ArgumentException($"Unsupported message role '{role}'.")
    };
}
