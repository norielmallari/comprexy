using Comprexy.Application.Models;

namespace Comprexy.Application.Abstractions;

/// <summary>
/// Derives a stable conversation identity for an incoming request, since the OpenAI chat
/// completions API itself is stateless.
/// </summary>
public interface IConversationIdentityResolver
{
    /// <summary>
    /// Returns the client-supplied header value when present, otherwise a deterministic
    /// fingerprint derived from the system prompt and first two user messages.
    /// </summary>
    string Resolve(string? conversationIdHeader, IReadOnlyList<ChatMessage> messages);
}
