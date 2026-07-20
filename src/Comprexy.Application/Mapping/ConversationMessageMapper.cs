using System.Text.Json;
using Comprexy.Application.Models;
using Comprexy.Domain.Entities;

namespace Comprexy.Application.Mapping;

/// <summary>
/// Maps persisted conversation messages back to chat messages, restoring raw wire JSON when stored.
/// </summary>
public static class ConversationMessageMapper
{
    public static ChatMessage ToChatMessage(ConversationMessage message)
    {
        JsonElement? rawWire = null;
        if (!string.IsNullOrWhiteSpace(message.RawWireJson))
        {
            using var document = JsonDocument.Parse(message.RawWireJson);
            rawWire = document.RootElement.Clone();
        }

        return new ChatMessage(message.Role, message.Content, rawWire);
    }
}
