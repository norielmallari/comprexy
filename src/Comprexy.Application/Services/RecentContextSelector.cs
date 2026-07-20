using Comprexy.Application.Configuration;
using Comprexy.Domain.Entities;
using Comprexy.Domain.Enums;
using Microsoft.Extensions.Options;

namespace Comprexy.Application.Services;

/// <summary>
/// Selects which unfolded messages to keep raw when compression runs (message-count and
/// token-budget retain window). Assistant + following tool results stay atomic so chat
/// templates never see orphaned tool messages. Not used when assembling chat requests after
/// compression — those include all remaining unfolded messages.
/// </summary>
public class RecentContextSelector
{
    private readonly ContextPolicyOptions _policy;

    public RecentContextSelector(IOptions<ContextPolicyOptions> policy)
    {
        _policy = policy.Value;
    }

    public IReadOnlyList<ConversationMessage> Select(
        IReadOnlyList<ConversationMessage> unfoldedExcludingCurrent,
        bool emergency = false,
        int? maxMessagesOverride = null)
    {
        var maxMessages = maxMessagesOverride
            ?? (emergency ? _policy.EmergencyRecentMessageCount : _policy.CompressionRetainMessageCount);
        var maxTokens = Math.Max(0, _policy.MaxRecentRawTokens);

        if (maxMessages <= 0 || unfoldedExcludingCurrent.Count == 0)
        {
            return [];
        }

        var ordered = unfoldedExcludingCurrent.OrderBy(m => m.Sequence).ToList();
        var groups = BuildAtomicGroups(ordered);
        if (groups.Count == 0)
        {
            return [];
        }

        var selectedGroups = new List<IReadOnlyList<ConversationMessage>>();
        var tokens = 0;
        var messageCount = 0;

        for (var i = groups.Count - 1; i >= 0; i--)
        {
            var group = groups[i];
            var groupTokens = group.Sum(m => Math.Max(0, m.TokenCount));
            var groupCount = group.Count;

            if (selectedGroups.Count > 0 &&
                (messageCount + groupCount > maxMessages || tokens + groupTokens > maxTokens))
            {
                break;
            }

            selectedGroups.Add(group);
            tokens += groupTokens;
            messageCount += groupCount;
        }

        selectedGroups.Reverse();
        var selected = selectedGroups.SelectMany(g => g).ToList();

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
