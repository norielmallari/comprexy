using System.Text.Json;
using Comprexy.Application.Models;
using Comprexy.Domain.Entities;
using Comprexy.Domain.Enums;

namespace Comprexy.Application.Services;

/// <summary>
/// Enforces chat-template message order constraints required by many local OpenAI-compatible
/// servers (a tool turn must follow an assistant or tool turn).
/// </summary>
public static class ChatTemplateMessageOrder
{
    /// <summary>
    /// Removes tool messages that would follow a non-assistant, non-tool predecessor in
    /// <paramref name="messages"/> (oldest-first). Returns the filtered list and how many
    /// orphan tool messages were dropped.
    /// </summary>
    public static (IReadOnlyList<ConversationMessage> Messages, int DroppedOrphanTools) RemoveOrphanToolMessages(
        IReadOnlyList<ConversationMessage> messages)
    {
        if (messages.Count == 0)
        {
            return (messages, 0);
        }

        var kept = new List<ConversationMessage>(messages.Count);
        var dropped = 0;
        MessageRole? previousRole = null;

        foreach (var message in messages.OrderBy(m => m.Sequence))
        {
            if (message.Role == MessageRole.Tool &&
                previousRole != MessageRole.Assistant &&
                previousRole != MessageRole.Tool)
            {
                dropped++;
                continue;
            }

            kept.Add(message);
            previousRole = message.Role;
        }

        return (kept, dropped);
    }

    /// <summary>
    /// When the live tip is a tool result but <paramref name="recentRaw"/> does not end with an
    /// assistant/tool turn (e.g. parent assistant was folded), re-includes the parent assistant
    /// (and intervening tools) from <paramref name="allMessages"/> for wire repair only.
    /// </summary>
    public static (IReadOnlyList<ConversationMessage> Messages, int RestoredParentMessages) EnsureToolTipHasParent(
        IReadOnlyList<ConversationMessage> recentRaw,
        ChatMessage tip,
        IReadOnlyList<ConversationMessage> allMessages,
        int tipSequence)
    {
        if (tip.Role != MessageRole.Tool)
        {
            return (recentRaw, 0);
        }

        var orderedRecent = recentRaw.OrderBy(m => m.Sequence).ToList();
        var previousRole = orderedRecent.Count > 0 ? orderedRecent[^1].Role : (MessageRole?)null;
        if (previousRole is MessageRole.Assistant or MessageRole.Tool)
        {
            return (orderedRecent, 0);
        }

        var toolCallId = TryExtractToolCallId(tip);
        if (toolCallId is null)
        {
            return (orderedRecent, 0);
        }

        var parent = allMessages
            .Where(m => m.Sequence < tipSequence && m.Role == MessageRole.Assistant)
            .OrderByDescending(m => m.Sequence)
            .FirstOrDefault(m => FileReadPathExtractor.GetAssistantToolCallIds(m).Contains(toolCallId));

        if (parent is null)
        {
            return (orderedRecent, 0);
        }

        var present = orderedRecent.Select(m => m.Sequence).ToHashSet();
        var restored = allMessages
            .Where(m => m.Sequence >= parent.Sequence && m.Sequence < tipSequence)
            .OrderBy(m => m.Sequence)
            .Where(m => present.Add(m.Sequence))
            .ToList();

        if (restored.Count == 0)
        {
            return (orderedRecent, 0);
        }

        var merged = orderedRecent
            .Concat(restored)
            .OrderBy(m => m.Sequence)
            .ToList();
        return (merged, restored.Count);
    }

    private static string? TryExtractToolCallId(ChatMessage tip)
    {
        if (tip.RawWireMessage is { } wire)
        {
            try
            {
                if (wire.ValueKind == JsonValueKind.Object &&
                    wire.TryGetProperty("tool_call_id", out var id) &&
                    id.ValueKind == JsonValueKind.String)
                {
                    var value = id.GetString();
                    return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
                }
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        return null;
    }
}
