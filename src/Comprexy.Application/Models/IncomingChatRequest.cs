using System.Text.Json;

namespace Comprexy.Application.Models;

/// <summary>
/// Client request to the OpenAI-compatible proxy endpoint.
/// <see cref="RawRequest"/> is the original JSON body; all unknown fields are preserved when
/// forwarding upstream.
/// </summary>
public sealed record IncomingChatRequest(
    IReadOnlyList<ChatMessage> Messages,
    string? ConversationIdHeader,
    bool Stream,
    JsonElement RawRequest,
    ChatCompletionCallOptions CallOptions);
