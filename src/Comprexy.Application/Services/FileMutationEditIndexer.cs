using Comprexy.Domain.Entities;
using Comprexy.Domain.Enums;

namespace Comprexy.Application.Services;

/// <summary>
/// Classifies tool results against their announcing assistant mutation calls.
/// </summary>
public static class FileMutationEditIndexer
{
    public sealed record MutationToolResult(
        ConversationMessage ToolMessage,
        ConversationMessage? AssistantMessage,
        string ToolCallId,
        string ToolName,
        string Path,
        string OldStringKey,
        bool IsSuccess,
        bool IsFailure);

    public static IReadOnlyList<MutationToolResult> Index(IReadOnlyList<ConversationMessage> messages)
    {
        if (messages.Count == 0)
        {
            return [];
        }

        var ordered = messages.OrderBy(m => m.Sequence).ToList();
        var announced = new Dictionary<string, (ConversationMessage Assistant, ParsedToolCall Call)>(
            StringComparer.Ordinal);

        foreach (var message in ordered)
        {
            if (message.Role != MessageRole.Assistant || string.IsNullOrWhiteSpace(message.RawWireJson))
            {
                continue;
            }

            foreach (var call in ToolCallWireHelper.ParseAssistantToolCalls(message.RawWireJson))
            {
                if (!FileMutationClassifier.IsMutatingFileTool(call.Name))
                {
                    continue;
                }

                announced[call.Id] = (message, call);
            }
        }

        var results = new List<MutationToolResult>();
        foreach (var message in ordered)
        {
            if (message.Role != MessageRole.Tool)
            {
                continue;
            }

            var toolCallId = ToolCallWireHelper.TryExtractToolCallId(message);
            if (toolCallId is null || !announced.TryGetValue(toolCallId, out var announcedCall))
            {
                continue;
            }

            var path = FileMutationClassifier.TryExtractPathFromToolArguments(announcedCall.Call.ArgumentsJson);
            if (path is null)
            {
                continue;
            }

            var content = message.Content ?? string.Empty;
            var isSuccess = FileMutationClassifier.LooksLikeSuccessfulFileMutation(content);
            var isFailure = !isSuccess && FileMutationClassifier.LooksLikeFailedFileMutation(content);
            if (!isSuccess && !isFailure)
            {
                continue;
            }

            results.Add(new MutationToolResult(
                message,
                announcedCall.Assistant,
                toolCallId,
                announcedCall.Call.Name,
                FileMutationClassifier.NormalizePath(path),
                FileMutationClassifier.NormalizeOldStringKey(
                    FileMutationClassifier.TryExtractOldStringFromToolArguments(announcedCall.Call.ArgumentsJson)),
                isSuccess,
                isFailure));
        }

        return results;
    }
}
