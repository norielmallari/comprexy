using Comprexy.Domain.Entities;

namespace Comprexy.Application.Services;

/// <summary>
/// Inline fold pin: when unfolded history still has failed file edits on path P, keep the
/// last successful mutation atomic group for P so WM fold does not erase the post-edit tip.
/// </summary>
public static class HotPathSuccessfulEditRetainer
{
    public static IReadOnlyList<ConversationMessage> SelectPinnedMessages(
        IReadOnlyList<ConversationMessage> foldUniverse)
    {
        if (foldUniverse.Count == 0)
        {
            return [];
        }

        var indexed = FileMutationEditIndexer.Index(foldUniverse);
        var hotPaths = indexed
            .Where(r => r.IsFailure)
            .Select(r => r.Path)
            .ToHashSet(StringComparer.Ordinal);
        if (hotPaths.Count == 0)
        {
            return [];
        }

        var ordered = foldUniverse.OrderBy(m => m.Sequence).ToList();
        var groups = RecentContextSelector.BuildAtomicGroups(ordered);
        var pinned = new List<ConversationMessage>();
        var pinnedIds = new HashSet<Guid>();

        foreach (var path in hotPaths.OrderBy(p => p, StringComparer.Ordinal))
        {
            var lastSuccess = indexed
                .Where(r => r.IsSuccess && string.Equals(r.Path, path, StringComparison.Ordinal))
                .OrderBy(r => r.ToolMessage.Sequence)
                .LastOrDefault();
            if (lastSuccess is null)
            {
                continue;
            }

            var group = groups.FirstOrDefault(g => g.Any(m => m.Id == lastSuccess.ToolMessage.Id));
            if (group is null)
            {
                // Fallback: pin tool + assistant only.
                if (lastSuccess.AssistantMessage is not null && pinnedIds.Add(lastSuccess.AssistantMessage.Id))
                {
                    pinned.Add(lastSuccess.AssistantMessage);
                }

                if (pinnedIds.Add(lastSuccess.ToolMessage.Id))
                {
                    pinned.Add(lastSuccess.ToolMessage);
                }

                continue;
            }

            foreach (var message in group)
            {
                if (pinnedIds.Add(message.Id))
                {
                    pinned.Add(message);
                }
            }
        }

        return pinned.OrderBy(m => m.Sequence).ToList();
    }
}
