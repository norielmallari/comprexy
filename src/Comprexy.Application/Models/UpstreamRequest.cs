using System.Text.Json;

namespace Comprexy.Application.Models;

public enum UpstreamRequestPurpose
{
    Chat,
    Compression
}

/// <summary>
/// Upstream chat-completions call. When <see cref="OriginalClientRequest"/> is present, its
/// fields are preserved and only model/stream/(optionally) messages are overridden.
/// </summary>
public sealed record UpstreamRequest(
    IReadOnlyList<ChatMessage> Messages,
    bool Stream,
    JsonElement? OriginalClientRequest = null,
    ChatCompletionCallOptions? CallOptions = null,
    bool ReplaceMessages = true,
    UpstreamRequestPurpose Purpose = UpstreamRequestPurpose.Chat);
