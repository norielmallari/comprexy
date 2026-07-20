using System.Text.Json;
using Comprexy.Domain.Entities;
using Comprexy.Domain.Enums;

namespace Comprexy.Application.Services;

/// <summary>
/// Detects incomplete assistant tool_call chains in unfolded conversation history.
/// Compression must not run while any announced tool_call id lacks a matching tool result.
/// </summary>
public static class ToolCallChainState
{
    /// <summary>
    /// True when any assistant <c>tool_calls</c> id has no matching tool <c>tool_call_id</c>,
    /// or when an assistant has a non-empty <c>tool_calls</c> array with no parseable ids
    /// (fail closed).
    /// </summary>
    public static bool HasOpenToolCalls(IReadOnlyList<ConversationMessage> unfolded) =>
        Assess(unfolded).IsOpen;

    public static ToolCallChainOpenAssessment Assess(IReadOnlyList<ConversationMessage> unfolded)
    {
        if (unfolded.Count == 0)
        {
            return ToolCallChainOpenAssessment.Closed;
        }

        var announced = new HashSet<string>(StringComparer.Ordinal);
        var closed = new HashSet<string>(StringComparer.Ordinal);
        var unparseableToolCallAssistants = 0;

        foreach (var message in unfolded.OrderBy(m => m.Sequence))
        {
            if (message.Role == MessageRole.Assistant)
            {
                if (!HasNonEmptyToolCallsArray(message))
                {
                    continue;
                }

                var ids = FileReadPathExtractor.GetAssistantToolCallIds(message);
                if (ids.Count == 0)
                {
                    unparseableToolCallAssistants++;
                    continue;
                }

                foreach (var id in ids)
                {
                    announced.Add(id);
                }

                continue;
            }

            if (message.Role == MessageRole.Tool)
            {
                var toolCallId = FileReadPathExtractor.TryExtractToolCallId(message);
                if (toolCallId is not null)
                {
                    closed.Add(toolCallId);
                }
            }
        }

        var unmatched = announced.Count(id => !closed.Contains(id));
        if (unparseableToolCallAssistants > 0)
        {
            return new ToolCallChainOpenAssessment(
                IsOpen: true,
                UnmatchedCount: unmatched + unparseableToolCallAssistants);
        }

        return unmatched > 0
            ? new ToolCallChainOpenAssessment(IsOpen: true, UnmatchedCount: unmatched)
            : ToolCallChainOpenAssessment.Closed;
    }

    private static bool HasNonEmptyToolCallsArray(ConversationMessage message)
    {
        if (message.Role != MessageRole.Assistant || string.IsNullOrWhiteSpace(message.RawWireJson))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(message.RawWireJson);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                   document.RootElement.TryGetProperty("tool_calls", out var toolCalls) &&
                   toolCalls.ValueKind == JsonValueKind.Array &&
                   toolCalls.GetArrayLength() > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

public readonly record struct ToolCallChainOpenAssessment(bool IsOpen, int UnmatchedCount)
{
    public static ToolCallChainOpenAssessment Closed { get; } = new(IsOpen: false, UnmatchedCount: 0);
}
