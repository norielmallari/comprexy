using Comprexy.Application.Models;
using Comprexy.Application.Services;
using Comprexy.Domain.Entities;
using Comprexy.Domain.Enums;

namespace Comprexy.Application.Tests.Services;

public class ChatTemplateMessageOrderTests
{
    [Fact]
    public void RemoveOrphanToolMessages_DropsToolAfterUser()
    {
        var conversationId = Guid.NewGuid();
        var messages = new[]
        {
            ConversationMessage.Create(conversationId, 0, MessageRole.User, "hi", 1, DateTimeOffset.UtcNow),
            ConversationMessage.Create(conversationId, 1, MessageRole.Tool, "orphan", 1, DateTimeOffset.UtcNow),
            ConversationMessage.Create(conversationId, 2, MessageRole.User, "next", 1, DateTimeOffset.UtcNow)
        };

        var (sanitized, dropped) = ChatTemplateMessageOrder.RemoveOrphanToolMessages(messages);

        Assert.Equal(1, dropped);
        Assert.Equal([0, 2], sanitized.Select(m => m.Sequence).ToArray());
    }

    [Fact]
    public void RemoveOrphanToolMessages_KeepsAssistantToolChain()
    {
        var conversationId = Guid.NewGuid();
        var messages = new[]
        {
            ConversationMessage.Create(conversationId, 0, MessageRole.Assistant, string.Empty, 1, DateTimeOffset.UtcNow),
            ConversationMessage.Create(conversationId, 1, MessageRole.Tool, "a", 1, DateTimeOffset.UtcNow),
            ConversationMessage.Create(conversationId, 2, MessageRole.Tool, "b", 1, DateTimeOffset.UtcNow)
        };

        var (sanitized, dropped) = ChatTemplateMessageOrder.RemoveOrphanToolMessages(messages);

        Assert.Equal(0, dropped);
        Assert.Equal(3, sanitized.Count);
    }

    [Fact]
    public void RemoveOrphanToolMessages_DropsLeadingTool()
    {
        var conversationId = Guid.NewGuid();
        var messages = new[]
        {
            ConversationMessage.Create(conversationId, 0, MessageRole.Tool, "orphan", 1, DateTimeOffset.UtcNow),
            ConversationMessage.Create(conversationId, 1, MessageRole.User, "hi", 1, DateTimeOffset.UtcNow)
        };

        var (sanitized, dropped) = ChatTemplateMessageOrder.RemoveOrphanToolMessages(messages);

        Assert.Equal(1, dropped);
        Assert.Equal([1], sanitized.Select(m => m.Sequence).ToArray());
    }

    [Fact]
    public void EnsureToolTipHasParent_RestoresFoldedAssistant()
    {
        var conversationId = Guid.NewGuid();
        var assistant = ConversationMessage.Create(
            conversationId,
            0,
            MessageRole.Assistant,
            string.Empty,
            1,
            DateTimeOffset.UtcNow,
            """{"role":"assistant","tool_calls":[{"id":"c1","type":"function","function":{"name":"Read","arguments":"{}"}}]}""");
        assistant.MarkFoldedInto(1);
        var allMessages = new[]
        {
            assistant,
            ConversationMessage.Create(conversationId, 1, MessageRole.Tool, "ok", 1, DateTimeOffset.UtcNow)
        };
        var tip = new ChatMessage(
            MessageRole.Tool,
            "ok",
            System.Text.Json.JsonDocument.Parse("""{"role":"tool","tool_call_id":"c1","content":"ok"}""").RootElement.Clone());

        var (repaired, restored) = ChatTemplateMessageOrder.EnsureToolTipHasParent(
            [],
            tip,
            allMessages,
            tipSequence: 1);

        Assert.Equal(1, restored);
        Assert.Equal([0], repaired.Select(m => m.Sequence).ToArray());
        Assert.Equal(MessageRole.Assistant, repaired[0].Role);
    }
}
