using System.Security.Cryptography;
using System.Text;
using Comprexy.Application.Abstractions;
using Comprexy.Application.Models;
using Comprexy.Domain.Enums;

namespace Comprexy.Application.Services;

/// <summary>
/// Resolves conversation identity from an optional client header, falling back to a
/// deterministic fingerprint of the system prompt and first two user messages so that clients
/// which resend full history (the OpenAI-standard behavior) are still recognized as the same
/// conversation across turns.
/// </summary>
public class ConversationIdentityResolver : IConversationIdentityResolver
{
    public string Resolve(string? conversationIdHeader, IReadOnlyList<ChatMessage> messages)
    {
        if (!string.IsNullOrWhiteSpace(conversationIdHeader))
        {
            return $"header:{conversationIdHeader.Trim()}";
        }

        var systemMessage = messages.FirstOrDefault(m => m.Role == MessageRole.System)?.Content ?? string.Empty;
        var userMessages = messages
            .Where(m => m.Role == MessageRole.User)
            .Select(m => m.Content ?? string.Empty)
            .Take(2)
            .ToList();
        var firstUserMessage = userMessages.ElementAtOrDefault(0) ?? string.Empty;
        var secondUserMessage = userMessages.ElementAtOrDefault(1) ?? string.Empty;

        var fingerprintSource = $"{systemMessage}\u241f{firstUserMessage}\u241f{secondUserMessage}";
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(fingerprintSource));
        var hash = Convert.ToHexString(hashBytes);

        return $"fingerprint:{hash}";
    }
}
