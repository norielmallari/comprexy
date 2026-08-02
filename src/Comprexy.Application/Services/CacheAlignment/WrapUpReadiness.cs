using Comprexy.Domain.Entities;
using Comprexy.Domain.Enums;

namespace Comprexy.Application.Services.CacheAlignment;

/// <summary>
/// Ensures a candidate message frontier is wrap-up ready (R1–R6 companion checks on
/// ConversationMessage rows). Reuses <see cref="ChatTemplateMessageOrder"/> and
/// <see cref="ToolCallChainState"/> — does not fork orphan/chain logic.
/// </summary>
public static class WrapUpReadiness
{
    /// <summary>
    /// True when the frontier has a closed tool chain and no orphan tool turns.
    /// System/WM shape (R1) is asserted on the ChatMessage Prefix after
    /// <see cref="ContextBuilder.BuildLivePrefix"/>.
    /// </summary>
    public static bool IsWrapUpReady(IReadOnlyList<ConversationMessage> frontier)
    {
        if (frontier.Count == 0)
        {
            return true;
        }

        var (withoutOrphans, _) = ChatTemplateMessageOrder.RemoveOrphanToolMessages(frontier);
        if (withoutOrphans.Count != frontier.Count)
        {
            return false;
        }

        return !ToolCallChainState.Assess(withoutOrphans).IsOpen;
    }

    /// <summary>
    /// Normalize orphans and open chains for Prefix store. Open assistants (+ all their tools)
    /// move to <paramref name="excludedFromPrefix"/>; remaining frontier must be closed.
    /// Returns false when the remaining frontier is still illegal (fail closed).
    /// </summary>
    public static bool TryEnsureWrapUpReady(
        IReadOnlyList<ConversationMessage> candidate,
        out IReadOnlyList<ConversationMessage> prefixFrontier,
        out IReadOnlyList<ConversationMessage> excludedFromPrefix)
    {
        excludedFromPrefix = Array.Empty<ConversationMessage>();
        prefixFrontier = Array.Empty<ConversationMessage>();

        if (candidate.Count == 0)
        {
            return true;
        }

        var (withoutOrphans, _) = ChatTemplateMessageOrder.RemoveOrphanToolMessages(candidate);
        var assessment = ToolCallChainState.Assess(withoutOrphans);
        if (!assessment.IsOpen)
        {
            prefixFrontier = withoutOrphans as IReadOnlyList<ConversationMessage>
                ?? withoutOrphans.ToList();
            return true;
        }

        var repaired = RepairOpenByExclusion(withoutOrphans, assessment, out var excluded);
        if (!IsWrapUpReady(repaired))
        {
            return false;
        }

        prefixFrontier = repaired;
        excludedFromPrefix = excluded;
        return true;
    }

    /// <summary>
    /// Moves open tool-call assistants and all results for their announced ids out of the frontier.
    /// </summary>
    public static IReadOnlyList<ConversationMessage> RepairOpenByExclusion(
        IReadOnlyList<ConversationMessage> frontier,
        ToolCallChainOpenAssessment assessment,
        out IReadOnlyList<ConversationMessage> excluded)
    {
        if (!assessment.IsOpen)
        {
            excluded = Array.Empty<ConversationMessage>();
            return frontier;
        }

        var openIds = new HashSet<string>(assessment.OpenToolCallIds, StringComparer.Ordinal);
        var ordered = frontier.OrderBy(m => m.Sequence).ToList();
        var excludeIds = new HashSet<Guid>();
        var excludedAssistantToolCallIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var message in ordered)
        {
            if (message.Role == MessageRole.Assistant)
            {
                var ids = ToolCallWireHelper.GetAssistantToolCallIds(message);
                if (ids.Count == 0 && assessment.UnmatchedCount > assessment.OpenToolCallIds.Count)
                {
                    // Unparseable tool_calls assistant — fail-closed exclusion.
                    excludeIds.Add(message.Id);
                    continue;
                }

                if (ids.Any(id => openIds.Contains(id)))
                {
                    excludeIds.Add(message.Id);
                    excludedAssistantToolCallIds.UnionWith(ids);
                }

                continue;
            }
        }

        foreach (var message in ordered)
        {
            if (message.Role == MessageRole.Tool)
            {
                var toolCallId = ToolCallWireHelper.TryExtractToolCallId(message);
                if (toolCallId is not null
                    && (openIds.Contains(toolCallId)
                        || excludedAssistantToolCallIds.Contains(toolCallId)))
                {
                    excludeIds.Add(message.Id);
                }
            }
        }

        if (excludeIds.Count == 0)
        {
            // Unparseable-only open: drop trailing assistants with non-empty tool_calls.
            foreach (var message in ordered.Where(m => m.Role == MessageRole.Assistant).Reverse())
            {
                if (ToolCallWireHelper.GetAssistantToolCallIds(message).Count == 0
                    && !string.IsNullOrWhiteSpace(message.RawWireJson)
                    && message.RawWireJson.Contains("tool_calls", StringComparison.Ordinal))
                {
                    excludeIds.Add(message.Id);
                    break;
                }
            }
        }

        var kept = new List<ConversationMessage>(ordered.Count);
        var removed = new List<ConversationMessage>();
        foreach (var message in ordered)
        {
            if (excludeIds.Contains(message.Id))
            {
                removed.Add(message);
            }
            else
            {
                kept.Add(message);
            }
        }

        excluded = removed;
        return kept;
    }
}
