using Comprexy.Domain.Entities;
using Comprexy.Domain.Enums;

namespace Comprexy.Application.Services;

/// <summary>
/// Wire-only filter: drop older identical failed file-edit tool results (path + old_string
/// last-wins) so StrReplace failure loops do not stack in model context.
/// </summary>
public static class DuplicateFailedEditDeduper
{
    public static DuplicateFailedEditDedupeResult Apply(
        IReadOnlyList<ConversationMessage> messages,
        int? forcedTipSequence)
    {
        if (messages.Count == 0)
        {
            return new DuplicateFailedEditDedupeResult([], [], []);
        }

        var indexed = FileMutationEditIndexer.Index(messages)
            .Where(r => r.IsFailure)
            .ToList();
        if (indexed.Count == 0)
        {
            return new DuplicateFailedEditDedupeResult(messages, [], []);
        }

        var dropSequences = new HashSet<int>();
        var groups = indexed
            .GroupBy(r => (r.Path, r.OldStringKey), PathOldStringComparer.Instance)
            .ToList();

        var keptKeys = new List<string>();
        foreach (var group in groups)
        {
            var items = group.OrderBy(r => r.ToolMessage.Sequence).ToList();
            if (items.Count <= 1)
            {
                keptKeys.Add(FormatKey(group.Key.Path, group.Key.OldStringKey));
                continue;
            }

            var keepSequences = new HashSet<int> { items[^1].ToolMessage.Sequence };
            if (forcedTipSequence is int tip && items.Any(r => r.ToolMessage.Sequence == tip))
            {
                keepSequences.Add(tip);
            }

            keptKeys.Add(FormatKey(group.Key.Path, group.Key.OldStringKey));
            foreach (var item in items)
            {
                if (keepSequences.Contains(item.ToolMessage.Sequence))
                {
                    continue;
                }

                dropSequences.Add(item.ToolMessage.Sequence);
            }
        }

        if (dropSequences.Count == 0)
        {
            return new DuplicateFailedEditDedupeResult(messages, [], keptKeys);
        }

        var keptToolCallIds = messages
            .Where(m => m.Role == MessageRole.Tool && !dropSequences.Contains(m.Sequence))
            .Select(ToolCallWireHelper.TryExtractToolCallId)
            .Where(id => id is not null)
            .Select(id => id!)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var message in messages)
        {
            if (message.Role != MessageRole.Assistant || dropSequences.Contains(message.Sequence))
            {
                continue;
            }

            if (forcedTipSequence is int tip && message.Sequence == tip)
            {
                continue;
            }

            var parentIds = ToolCallWireHelper.GetAssistantToolCallIds(message);
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

        return new DuplicateFailedEditDedupeResult(
            filtered,
            dropSequences.OrderBy(s => s).ToList(),
            keptKeys.OrderBy(k => k, StringComparer.Ordinal).ToList());
    }

    private static string FormatKey(string path, string oldStringKey)
    {
        var preview = oldStringKey.Length <= 48 ? oldStringKey : oldStringKey[..48];
        return $"{path}#{preview}";
    }

    private sealed class PathOldStringComparer : IEqualityComparer<(string Path, string OldStringKey)>
    {
        public static readonly PathOldStringComparer Instance = new();

        public bool Equals((string Path, string OldStringKey) x, (string Path, string OldStringKey) y) =>
            string.Equals(x.Path, y.Path, StringComparison.Ordinal) &&
            string.Equals(x.OldStringKey, y.OldStringKey, StringComparison.Ordinal);

        public int GetHashCode((string Path, string OldStringKey) obj) =>
            HashCode.Combine(
                StringComparer.Ordinal.GetHashCode(obj.Path),
                StringComparer.Ordinal.GetHashCode(obj.OldStringKey));
    }
}

public readonly record struct DuplicateFailedEditDedupeResult(
    IReadOnlyList<ConversationMessage> Retain,
    IReadOnlyList<int> DroppedSequences,
    IReadOnlyList<string> KeptKeys)
{
    public bool DroppedAny => DroppedSequences.Count > 0;
}
