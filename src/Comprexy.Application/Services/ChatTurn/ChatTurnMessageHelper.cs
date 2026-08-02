using System.Text.Json;
using Comprexy.Application.Abstractions;
using Comprexy.Application.Models;
using Comprexy.Domain.Entities;
using Comprexy.Domain.Enums;

namespace Comprexy.Application.Services.ChatTurn;

public sealed class ChatTurnMessageHelper
{
    private readonly IConversationMessageRepository _messageRepository;
    private readonly ITokenEstimator _tokenEstimator;

    public ChatTurnMessageHelper(
        IConversationMessageRepository messageRepository,
        ITokenEstimator tokenEstimator)
    {
        _messageRepository = messageRepository;
        _tokenEstimator = tokenEstimator;
    }

    public ConversationMessage PersistMessage(
        Guid conversationId,
        int sequence,
        ChatMessage message,
        DateTimeOffset now)
    {
        var tokenCount = _tokenEstimator.CountTokens([message]);
        var rawWireJson = message.RawWireMessage?.GetRawText();
        var entity = ConversationMessage.Create(
            conversationId,
            sequence,
            message.Role,
            message.Content,
            tokenCount,
            now,
            rawWireJson);

        _messageRepository.Add(entity);
        return entity;
    }

    public static string SummarizeAssistantContent(string? assistantMessageJson)
    {
        if (string.IsNullOrWhiteSpace(assistantMessageJson))
        {
            return string.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(assistantMessageJson);
            var root = document.RootElement;
            if (root.TryGetProperty("tool_calls", out var toolCalls) &&
                toolCalls.ValueKind == JsonValueKind.Array &&
                toolCalls.GetArrayLength() > 0)
            {
                var names = toolCalls.EnumerateArray()
                    .Select(call =>
                        call.TryGetProperty("function", out var function) &&
                        function.TryGetProperty("name", out var name) &&
                        name.ValueKind == JsonValueKind.String
                            ? name.GetString()
                            : null)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .ToList();

                return names.Count > 0
                    ? $"[tool_calls: {string.Join(", ", names)}]"
                    : "[tool_calls]";
            }
        }
        catch (JsonException)
        {
            // Fall through — leave content empty if wire is unreadable.
        }

        return string.Empty;
    }

    public static JsonElement? ParseOptionalWire(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static bool IsSameTip(ConversationMessage persisted, ChatMessage incoming)
    {
        if (persisted.Role != incoming.Role)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(persisted.RawWireJson) && incoming.RawWireMessage is { } raw)
        {
            return string.Equals(persisted.RawWireJson, raw.GetRawText(), StringComparison.Ordinal);
        }

        return string.Equals(persisted.Content, incoming.Content, StringComparison.Ordinal);
    }

    /// <summary>
    /// True when <paramref name="requestTip"/> was an input to Virtual Tools inbound rewrite.
    /// Distill/swallow already accounted for it; tip sync must not re-stage native client wire.
    /// </summary>
    public static bool IsVirtualToolsExpectedTipMismatch(
        ChatMessage requestTip,
        IReadOnlyList<ChatMessage> nonSystemNewMessages)
    {
        if (requestTip.Role is not (MessageRole.Tool or MessageRole.Assistant))
        {
            return false;
        }

        for (var i = 0; i < nonSystemNewMessages.Count; i++)
        {
            if (ReferenceEquals(nonSystemNewMessages[i], requestTip))
            {
                return true;
            }
        }

        return false;
    }
}
