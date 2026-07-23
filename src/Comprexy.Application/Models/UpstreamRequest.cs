using System.Text.Json;

namespace Comprexy.Application.Models;

public enum UpstreamRequestPurpose
{
    Chat,
    Compression
}

/// <summary>
/// Upstream chat-completions call. When <see cref="OriginalClientRequest"/> is present, its
/// fields are preserved; stream/(optionally) messages are always applied, and model is overridden
/// only when the endpoint has a configured model.
/// </summary>
public sealed record UpstreamRequest(
    IReadOnlyList<ChatMessage> Messages,
    bool Stream,
    JsonElement? OriginalClientRequest = null,
    ChatCompletionCallOptions? CallOptions = null,
    bool ReplaceMessages = true,
    UpstreamRequestPurpose Purpose = UpstreamRequestPurpose.Chat);
