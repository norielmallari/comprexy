using Comprexy.Domain.Entities;
using Comprexy.Domain.Enums;

namespace Comprexy.Application.Services;

/// <summary>
/// Validates and clamps Smart retainSequences against real messages.
/// </summary>
public static class SmartRetainResolver
{
    public static IReadOnlyList<ConversationMessage> Resolve(
        IReadOnlyList<int>? nominatedSequences,
        IReadOnlyList<ConversationMessage> candidates,
        ConversationMessage? forcedTip,
        int maxMessages,
        int maxTokens)
    {
        var bySequence = candidates
            .GroupBy(m => m.Sequence)
            .ToDictionary(g => g.Key, g => g.First());

        var accepted = new List<ConversationMessage>();
        var seen = new HashSet<int>();

        if (nominatedSequences is not null)
        {
            foreach (var sequence in nominatedSequences.OrderBy(s => s))
            {
                if (!seen.Add(sequence))
                {
                    continue;
                }

                if (bySequence.TryGetValue(sequence, out var message))
                {
                    accepted.Add(message);
                }
            }
        }

        if (forcedTip is not null)
        {
            var tip = bySequence.TryGetValue(forcedTip.Sequence, out var fromCandidates)
                ? fromCandidates
                : forcedTip;
            if (seen.Add(tip.Sequence))
            {
                accepted.Add(tip);
            }
        }

        return Clamp(
            accepted.OrderBy(m => m.Sequence).ToList(),
            forcedTip?.Sequence,
            maxMessages,
            maxTokens);
    }

    public static ConversationMessage? FindForcedTip(IReadOnlyList<ConversationMessage> messages)
    {
        for (var i = messages.Count - 1; i >= 0; i--)
        {
            if (messages[i].Role != MessageRole.System)
            {
                return messages[i];
            }
        }

        return messages.Count > 0 ? messages[^1] : null;
    }

    private static IReadOnlyList<ConversationMessage> Clamp(
        List<ConversationMessage> acceptedOldestFirst,
        int? forcedTipSequence,
        int maxMessages,
        int maxTokens)
    {
        maxMessages = Math.Max(1, maxMessages);
        maxTokens = Math.Max(0, maxTokens);

        while (acceptedOldestFirst.Count > 0)
        {
            var tokens = acceptedOldestFirst.Sum(m => Math.Max(0, m.TokenCount));
            var overCount = acceptedOldestFirst.Count > maxMessages;
            var overTokens = maxTokens > 0 && tokens > maxTokens;
            if (!overCount && !overTokens)
            {
                break;
            }

            var dropIndex = 0;
            while (dropIndex < acceptedOldestFirst.Count &&
                   forcedTipSequence is int tip &&
                   acceptedOldestFirst[dropIndex].Sequence == tip)
            {
                dropIndex++;
            }

            if (dropIndex >= acceptedOldestFirst.Count)
            {
                break;
            }

            acceptedOldestFirst.RemoveAt(dropIndex);
        }

        return acceptedOldestFirst;
    }
}
