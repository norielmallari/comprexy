using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
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
    ];

    private static readonly Regex UserQueryPattern = new(
        @"<user_query>([\s\S]*?)</user_query>",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public string Resolve(string? conversationIdHeader, IReadOnlyList<ChatMessage> messages)
    {
        if (!string.IsNullOrWhiteSpace(conversationIdHeader))
        {
            return $"header:{conversationIdHeader.Trim()}";
        }

        var systemMessage = messages.FirstOrDefault(m => m.Role == MessageRole.System)?.Content ?? string.Empty;
        var userMessages = messages
            .Where(m => m.Role == MessageRole.User)
            .Select(m => NormalizeForFingerprint(m.Content ?? string.Empty))
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
    /// Normalizes user message text for fingerprinting: when Cursor wraps the turn in
    /// <c>user_query</c>, use only that inner text; otherwise strip dynamic metadata blocks.
    /// </summary>
    private static string NormalizeForFingerprint(string content)
    {
        var queryMatches = UserQueryPattern.Matches(content);
        if (queryMatches.Count > 0)
        {
            return string.Join(
                "\n",
                queryMatches.Select(match => match.Groups[1].Value.Trim()));
        }

        var result = content;
        foreach (var pattern in DynamicMetadataPatterns)
        {
            result = Regex.Replace(result, pattern, string.Empty);
        }

        return result.Trim();
    }
}
