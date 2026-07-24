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
    /// <summary>
    /// Dynamic metadata blocks injected by Cursor into user message content that change
    /// between turns (e.g., timestamps, file lists) and should be excluded from the fingerprint.
    /// </summary>
    private static readonly string[] DynamicMetadataPatterns =
    [
        @"<timestamp>[\s\S]*?</timestamp>",
        @"<open_and_recently_viewed_files>[\s\S]*?</open_and_recently_viewed_files>",
        @"<attached_files>[\s\S]*?</attached_files>",
        @"<user_query>[\s\S]*?</user_query>",
    ];

    public string Resolve(string? conversationIdHeader, IReadOnlyList<ChatMessage> messages)
    {
        if (!string.IsNullOrWhiteSpace(conversationIdHeader))
        {
            return $"header:{conversationIdHeader.Trim()}";
        }

        var systemMessage = messages.FirstOrDefault(m => m.Role == MessageRole.System)?.Content ?? string.Empty;
        var userMessages = messages
            .Where(m => m.Role == MessageRole.User)
            .Select(m => StripDynamicMetadata(m.Content ?? string.Empty))
            .Take(2)
            .ToList();
        var firstUserMessage = userMessages.ElementAtOrDefault(0) ?? string.Empty;
        var secondUserMessage = userMessages.ElementAtOrDefault(1) ?? string.Empty;

        var fingerprintSource = $"{systemMessage}\u241f{firstUserMessage}\u241f{secondUserMessage}";
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(fingerprintSource));
        var hash = Convert.ToHexString(hashBytes);

        return $"fingerprint:{hash}";
    }

    /// <summary>
    /// Strips dynamic Cursor-injected metadata blocks from message content before fingerprinting.
    /// </summary>
    private static string StripDynamicMetadata(string content)
    {
        var result = content;
        foreach (var pattern in DynamicMetadataPatterns)
        {
            result = System.Text.RegularExpressions.Regex.Replace(result, pattern, string.Empty);
        }
        return result;
    }
}
