using Comprexy.Application.Configuration;
using Comprexy.Domain.Entities;
using Comprexy.Domain.Enums;
using Microsoft.Extensions.Options;

namespace Comprexy.Application.Services;

/// <summary>
/// Selects which unfolded messages to keep raw when Inline wrap-up folds, newest-first up to
/// <see cref="ContextPolicyOptions.CompressionRetainMessageCount"/>. Assistant + following tool
/// results stay atomic so chat templates never see orphaned tool messages, which is why the
/// newest group is admitted whole even when it exceeds the count. Not used when assembling chat
/// requests after compression — those include all remaining unfolded messages.
/// </summary>
public class RecentContextSelector
{
    private readonly ContextPolicyOptions _policy;

    public RecentContextSelector(IOptions<ContextPolicyOptions> policy)
    {
        _policy = policy.Value;
    }

    public IReadOnlyList<ConversationMessage> Select(
        IReadOnlyList<ConversationMessage> unfoldedExcludingCurrent)
    {
        return Select(unfoldedExcludingCurrent, _policy.CompressionRetainMessageCount);
    }

    /// <summary>
    /// Retain-count overload for sticky effective settings (singleton cannot inject scoped accessor).
    /// </summary>
    public IReadOnlyList<ConversationMessage> Select(
        IReadOnlyList<ConversationMessage> unfoldedExcludingCurrent,
        int retainMessageCount)
    {
        var maxMessages = retainMessageCount;

        if (maxMessages <= 0 || unfoldedExcludingCurrent.Count == 0)
        {
            return [];
        }

        var ordered = unfoldedExcludingCurrent.OrderBy(m => m.Sequence).ToList();

        // Unpinned may be empty or yield no retainable groups.
        var groups = BuildAtomicGroups(ordered);
        var selectedGroups = new List<IReadOnlyList<ConversationMessage>>();
        var messageCount = 0;

        for (var i = groups.Count - 1; i >= 0; i--)
        {
            var group = groups[i];

            if (selectedGroups.Count > 0 && messageCount + group.Count > maxMessages)
            {
                break;
            }

            selectedGroups.Add(group);
            messageCount += group.Count;
        }

        selectedGroups.Reverse();
        var selected = selectedGroups.SelectMany(g => g).OrderBy(m => m.Sequence).ToList();

        // Drop leading orphan tool messages if an incomplete chain somehow remains.
        while (selected.Count > 0 && selected[0].Role == MessageRole.Tool)
        {
            selected.RemoveAt(0);
        }

        return selected;
    }

    /// <summary>
    /// Groups messages so each assistant tool-call turn stays with its tool results.
    /// Leading orphan tool messages (no preceding assistant in this list) form their own group
    /// and are filtered out of the final selection when they would start the window.
    /// </summary>
    internal static List<IReadOnlyList<ConversationMessage>> BuildAtomicGroups(
        IReadOnlyList<ConversationMessage> orderedOldestFirst)
    {
        var groups = new List<IReadOnlyList<ConversationMessage>>();
        var index = 0;

        while (index < orderedOldestFirst.Count)
        {
            var current = orderedOldestFirst[index];

            if (current.Role == MessageRole.Tool)
            {
                var orphanTools = new List<ConversationMessage>();
                while (index < orderedOldestFirst.Count && orderedOldestFirst[index].Role == MessageRole.Tool)
                {
                    orphanTools.Add(orderedOldestFirst[index]);
                    index++;
                }

                groups.Add(orphanTools);
                continue;
            }

            if (current.Role == MessageRole.Assistant &&
                index + 1 < orderedOldestFirst.Count &&
                orderedOldestFirst[index + 1].Role == MessageRole.Tool)
            {
                var toolCallGroup = new List<ConversationMessage> { current };
                index++;
                while (index < orderedOldestFirst.Count && orderedOldestFirst[index].Role == MessageRole.Tool)
                {
                    toolCallGroup.Add(orderedOldestFirst[index]);
                    index++;
                }

                groups.Add(toolCallGroup);
                continue;
            }

            groups.Add([current]);
            index++;
        }

        return groups;
    }
}
