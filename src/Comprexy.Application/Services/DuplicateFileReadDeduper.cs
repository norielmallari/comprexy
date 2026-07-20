using Comprexy.Domain.Entities;
using Comprexy.Domain.Enums;

namespace Comprexy.Application.Services;

/// <summary>
/// Deterministically filters a message corpus by dropping older duplicate file tool reads
/// (path-keyed last-wins). Used before the compression LLM call.
/// See <c>internal/deterministic-duplicate-file-read.md</c>.
/// </summary>
public static class DuplicateFileReadDeduper
{
    /// <summary>
    /// Returns <paramref name="messages"/> with older same-path file reads removed.
    /// </summary>
    public static DuplicateFileReadDedupeResult Apply(
        IReadOnlyList<ConversationMessage> messages,
        IReadOnlyList<ConversationMessage> conversationSlice,
        int? forcedTipSequence)
    {
        _ = conversationSlice;
        if (messages.Count == 0)
        {
            return new DuplicateFileReadDedupeResult([], []);
        }

        var dropSequences = new HashSet<int>();
        var pathGroups = new Dictionary<string, List<ConversationMessage>>(StringComparer.Ordinal);

        foreach (var message in messages.OrderBy(m => m.Sequence))
        {
            var path = FileReadPathExtractor.TryExtract(message);
            if (path is null)
            {
                continue;
            }

            if (!pathGroups.TryGetValue(path, out var group))
            {
                group = [];
                pathGroups[path] = group;
            }

            group.Add(message);
        }

        var keptPaths = new List<string>();
        foreach (var (path, group) in pathGroups)
        {
            if (group.Count <= 1)
            {
                keptPaths.Add(path);
                continue;
            }

            var keepSequences = new HashSet<int> { group.Max(m => m.Sequence) };
            if (forcedTipSequence is int tip && group.Any(m => m.Sequence == tip))
            {
                keepSequences.Add(tip);
            }

            keptPaths.Add(path);
            foreach (var message in group)
            {
                if (keepSequences.Contains(message.Sequence))
                {
                    continue;
                }

                dropSequences.Add(message.Sequence);
            }
        }

        // Drop assistant tool_call turns whose results were all removed (including multi-call
        // persona bootstraps where every path has a newer read elsewhere).
        var keptToolCallIds = messages
            .Where(m => m.Role == MessageRole.Tool && !dropSequences.Contains(m.Sequence))
            .Select(FileReadPathExtractor.TryExtractToolCallId)
            .Where(id => id is not null)
            .Select(id => id!)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var message in messages)
        {
            if (message.Role != MessageRole.Assistant ||
                dropSequences.Contains(message.Sequence))
            {
                continue;
            }

            if (forcedTipSequence is int tip && message.Sequence == tip)
            {
                continue;
            }

            var parentIds = FileReadPathExtractor.GetAssistantToolCallIds(message);
            if (parentIds.Count == 0)
            {
                continue;
            }

            if (parentIds.All(id => !keptToolCallIds.Contains(id)))
            {
                dropSequences.Add(message.Sequence);
            }
        }

        var filtered = messages
            .Where(m => !dropSequences.Contains(m.Sequence))
            .OrderBy(m => m.Sequence)
            .ToList();

        return new DuplicateFileReadDedupeResult(
            filtered,
            dropSequences.OrderBy(s => s).ToList(),
            keptPaths.OrderBy(p => p, StringComparer.Ordinal).ToList());
    }
}

public readonly record struct DuplicateFileReadDedupeResult(
    IReadOnlyList<ConversationMessage> Retain,
    IReadOnlyList<int> DroppedSequences,
    IReadOnlyList<string> KeptPaths)
{
    /// <summary>Messages kept in the filtered corpus (newest path wins).</summary>
    public IReadOnlyList<ConversationMessage> Messages => Retain;

    public DuplicateFileReadDedupeResult(
        IReadOnlyList<ConversationMessage> retain,
        IReadOnlyList<int> droppedSequences)
        : this(retain, droppedSequences, [])
    {
    }

    public bool DroppedAny => DroppedSequences.Count > 0;
}
