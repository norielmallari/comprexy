using Comprexy.Domain.Entities;
using Comprexy.Domain.Enums;

namespace Comprexy.Application.Services;

/// <summary>
/// Validates and clamps Smart retainSequences against real messages.
/// Assistant tool-call turns stay atomic with their following tool results so chat templates
/// never see a tool message after a system/user/working-memory turn.
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
        var orderedCandidates = candidates.OrderBy(m => m.Sequence).ToList();
        var bySequence = orderedCandidates
            .GroupBy(m => m.Sequence)
            .ToDictionary(g => g.Key, g => g.First());

        var selectedSequences = new HashSet<int>();

        if (nominatedSequences is not null)
        {
            foreach (var sequence in nominatedSequences)
            {
                if (bySequence.ContainsKey(sequence))
                {
                    selectedSequences.Add(sequence);
                }
            }
        }

        if (forcedTip is not null)
        {
            var tip = bySequence.TryGetValue(forcedTip.Sequence, out var fromCandidates)
                ? fromCandidates
                : forcedTip;
            selectedSequences.Add(tip.Sequence);
            bySequence[tip.Sequence] = tip;
        }

        var expanded = ExpandToAtomicGroups(orderedCandidates, selectedSequences);
        return Clamp(
            expanded,
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

    /// <summary>
    /// Expands nominated sequences so each assistant tool-call turn keeps its tool results
    /// (and nominated tool results pull in their parent assistant group).
    /// Orphan tool groups with no parent assistant in <paramref name="orderedCandidates"/> are dropped.
    /// </summary>
    internal static IReadOnlyList<ConversationMessage> ExpandToAtomicGroups(
        IReadOnlyList<ConversationMessage> orderedCandidates,
        IReadOnlySet<int> selectedSequences)
    {
        if (selectedSequences.Count == 0 || orderedCandidates.Count == 0)
        {
            return [];
        }

        var groups = RecentContextSelector.BuildAtomicGroups(orderedCandidates);
        var sequenceToGroup = new Dictionary<int, int>();
        for (var i = 0; i < groups.Count; i++)
        {
            foreach (var message in groups[i])
            {
                sequenceToGroup[message.Sequence] = i;
            }
        }

        var retainGroupIndexes = new HashSet<int>();
        foreach (var sequence in selectedSequences)
        {
            if (sequenceToGroup.TryGetValue(sequence, out var groupIndex))
            {
                retainGroupIndexes.Add(groupIndex);
            }
        }

        // Orphan tool groups cannot start a chat template; attach parent assistant group or drop.
        foreach (var groupIndex in retainGroupIndexes.ToList())
        {
            var group = groups[groupIndex];
            if (group.Count == 0 || group[0].Role != MessageRole.Tool)
            {
                continue;
            }

            var parentIndex = FindPrecedingAssistantGroupIndex(groups, groupIndex);
            if (parentIndex is int parent)
            {
                retainGroupIndexes.Add(parent);
            }
            else
            {
                retainGroupIndexes.Remove(groupIndex);
            }
        }

        return retainGroupIndexes
            .OrderBy(i => i)
            .SelectMany(i => groups[i])
            .OrderBy(m => m.Sequence)
            .ToList();
    }

    private static int? FindPrecedingAssistantGroupIndex(
        IReadOnlyList<IReadOnlyList<ConversationMessage>> groups,
        int orphanToolGroupIndex)
    {
        for (var i = orphanToolGroupIndex - 1; i >= 0; i--)
        {
            var group = groups[i];
            if (group.Count > 0 && group[0].Role == MessageRole.Assistant)
            {
                return i;
            }
        }

        return null;
    }

    private static IReadOnlyList<ConversationMessage> Clamp(
        IReadOnlyList<ConversationMessage> acceptedOldestFirst,
        int? forcedTipSequence,
        int maxMessages,
        int maxTokens)
    {
        maxMessages = Math.Max(1, maxMessages);
        maxTokens = Math.Max(0, maxTokens);

        var groups = RecentContextSelector.BuildAtomicGroups(acceptedOldestFirst).ToList();
        if (groups.Count == 0)
        {
            return [];
        }

        while (groups.Count > 0)
        {
            var messages = groups.SelectMany(g => g).ToList();
            var tokens = messages.Sum(m => Math.Max(0, m.TokenCount));
            var overCount = messages.Count > maxMessages;
            var overTokens = maxTokens > 0 && tokens > maxTokens;
            if (!overCount && !overTokens)
            {
                break;
            }

            var dropIndex = 0;
            while (dropIndex < groups.Count &&
                   forcedTipSequence is int tip &&
                   groups[dropIndex].Any(m => m.Sequence == tip))
            {
                dropIndex++;
            }

            if (dropIndex >= groups.Count)
            {
                break;
            }

            groups.RemoveAt(dropIndex);
        }

        return groups.SelectMany(g => g).OrderBy(m => m.Sequence).ToList();
    }
}
