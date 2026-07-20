using Comprexy.Application.Services;
using Comprexy.Domain.Entities;
using Comprexy.Domain.Enums;

namespace Comprexy.Application.Tests.Services;

public class ToolCallChainStateTests
{
    private static ConversationMessage User(Guid conversationId, int sequence) =>
        ConversationMessage.Create(
            conversationId,
            sequence,
            MessageRole.User,
            $"user-{sequence}",
            3,
            DateTimeOffset.UtcNow);

    private static ConversationMessage Assistant(Guid conversationId, int sequence, string content = "ok") =>
        ConversationMessage.Create(
            conversationId,
            sequence,
            MessageRole.Assistant,
            content,
            3,
            DateTimeOffset.UtcNow);

    private static ConversationMessage AssistantWithCalls(
        Guid conversationId,
        int sequence,
        params string[] callIds)
    {
        var calls = string.Join(",", callIds.Select(id =>
            $"{{\"id\":\"{id}\",\"type\":\"function\",\"function\":{{\"name\":\"Read\",\"arguments\":\"{{}}\"}}}}"));
        return ConversationMessage.Create(
            conversationId,
            sequence,
            MessageRole.Assistant,
            string.Empty,
            5,
            DateTimeOffset.UtcNow,
            $"{{\"role\":\"assistant\",\"tool_calls\":[{calls}]}}");
    }

    private static ConversationMessage AssistantWithUnparseableToolCalls(Guid conversationId, int sequence) =>
        ConversationMessage.Create(
            conversationId,
            sequence,
            MessageRole.Assistant,
            string.Empty,
            5,
            DateTimeOffset.UtcNow,
            """{"role":"assistant","tool_calls":[{"type":"function","function":{"name":"Read","arguments":"{}"}}]}""");

    private static ConversationMessage Tool(Guid conversationId, int sequence, string callId) =>
        ConversationMessage.Create(
            conversationId,
            sequence,
            MessageRole.Tool,
            "ok",
            5,
            DateTimeOffset.UtcNow,
            $"{{\"role\":\"tool\",\"tool_call_id\":\"{callId}\",\"content\":\"ok\"}}");

    private static ConversationMessage ToolWithoutId(Guid conversationId, int sequence) =>
        ConversationMessage.Create(
            conversationId,
            sequence,
            MessageRole.Tool,
            "ok",
            5,
            DateTimeOffset.UtcNow,
            """{"role":"tool","content":"ok"}""");

    [Fact]
    public void HasOpenToolCalls_NoTools_IsClosed()
    {
        var conversationId = Guid.NewGuid();
        var messages = new[]
        {
            User(conversationId, 0),
            Assistant(conversationId, 1)
        };

        Assert.False(ToolCallChainState.HasOpenToolCalls(messages));
        Assert.Equal(0, ToolCallChainState.Assess(messages).UnmatchedCount);
    }

    [Fact]
    public void HasOpenToolCalls_AssistantToolCallsWithoutResults_IsOpen()
    {
        var conversationId = Guid.NewGuid();
        var messages = new[]
        {
            User(conversationId, 0),
            AssistantWithCalls(conversationId, 1, "c1", "c2")
        };

        var assessment = ToolCallChainState.Assess(messages);
        Assert.True(assessment.IsOpen);
        Assert.Equal(2, assessment.UnmatchedCount);
    }

    [Fact]
    public void HasOpenToolCalls_ResultsPresent_IsClosed()
    {
        var conversationId = Guid.NewGuid();
        var messages = new[]
        {
            User(conversationId, 0),
            AssistantWithCalls(conversationId, 1, "c1"),
            Tool(conversationId, 2, "c1"),
            Assistant(conversationId, 3, "done")
        };

        Assert.False(ToolCallChainState.HasOpenToolCalls(messages));
    }

    [Fact]
    public void HasOpenToolCalls_TipIsToolResultsOnly_IsClosed()
    {
        var conversationId = Guid.NewGuid();
        var messages = new[]
        {
            User(conversationId, 0),
            AssistantWithCalls(conversationId, 1, "c1"),
            Tool(conversationId, 2, "c1")
        };

        Assert.False(ToolCallChainState.HasOpenToolCalls(messages));
    }

    [Fact]
    public void HasOpenToolCalls_PartialResults_IsOpen()
    {
        var conversationId = Guid.NewGuid();
        var messages = new[]
        {
            User(conversationId, 0),
            AssistantWithCalls(conversationId, 1, "c1", "c2"),
            Tool(conversationId, 2, "c1")
        };

        var assessment = ToolCallChainState.Assess(messages);
        Assert.True(assessment.IsOpen);
        Assert.Equal(1, assessment.UnmatchedCount);
    }

    [Fact]
    public void HasOpenToolCalls_UnparseableAssistantToolCalls_IsOpen()
    {
        var conversationId = Guid.NewGuid();
        var messages = new[]
        {
            User(conversationId, 0),
            AssistantWithUnparseableToolCalls(conversationId, 1)
        };

        var assessment = ToolCallChainState.Assess(messages);
        Assert.True(assessment.IsOpen);
        Assert.True(assessment.UnmatchedCount >= 1);
    }

    [Fact]
    public void HasOpenToolCalls_ToolWithoutId_DoesNotClose()
    {
        var conversationId = Guid.NewGuid();
        var messages = new[]
        {
            User(conversationId, 0),
            AssistantWithCalls(conversationId, 1, "c1"),
            ToolWithoutId(conversationId, 2)
        };

        Assert.True(ToolCallChainState.HasOpenToolCalls(messages));
    }

    [Fact]
    public void HasOpenToolCalls_DuplicateToolResults_StillClosed()
    {
        var conversationId = Guid.NewGuid();
        var messages = new[]
        {
            User(conversationId, 0),
            AssistantWithCalls(conversationId, 1, "c1"),
            Tool(conversationId, 2, "c1"),
            Tool(conversationId, 3, "c1")
        };

        Assert.False(ToolCallChainState.HasOpenToolCalls(messages));
    }
}
