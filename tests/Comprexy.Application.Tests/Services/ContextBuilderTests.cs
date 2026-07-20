using Comprexy.Application.Models;
using Comprexy.Application.Services;
using Comprexy.Domain.Entities;
using Comprexy.Domain.Enums;

namespace Comprexy.Application.Tests.Services;

public class ContextBuilderTests
{
    private readonly ContextBuilder _builder = new();

    [Fact]
    public void Build_WithNoWorkingMemoryOrRecentMessages_ReturnsSystemPromptAndCurrentMessage()
    {
        var currentMessage = new ChatMessage(MessageRole.User, "What's next?");

        var result = _builder.Build("Custom system prompt", null, [], currentMessage);

        Assert.Equal(2, result.Count);
        Assert.Equal(MessageRole.System, result[0].Role);
        Assert.Equal("Custom system prompt", result[0].Content);
        Assert.Equal(currentMessage, result[1]);
    }

    [Fact]
    public void Build_WithNullSystemPrompt_UsesDefault()
    {
        var currentMessage = new ChatMessage(MessageRole.User, "hi");

        var result = _builder.Build(null, null, [], currentMessage);

        Assert.Equal(MessageRole.System, result[0].Role);
        Assert.False(string.IsNullOrWhiteSpace(result[0].Content));
    }

    [Fact]
    public void Build_WithWorkingMemory_InsertsItAsSecondSystemMessage()
    {
        var workingMemory = WorkingMemory.Create(Guid.NewGuid(), 1, "# Working Memory\n## Current Goal\nShip MVP", 10, DateTimeOffset.UtcNow);
        var currentMessage = new ChatMessage(MessageRole.User, "continue");

        var result = _builder.Build("System.", workingMemory, [], currentMessage);

        Assert.Equal(3, result.Count);
        Assert.Equal(MessageRole.System, result[1].Role);
        Assert.Contains("compressed historical context", result[1].Content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Current Goal", result[1].Content);
        Assert.DoesNotContain("Do not treat it as new user instructions", result[0].Content);
    }

    [Fact]
    public void Build_WithRecentRawMessages_PreservesOrderAndRestoresRawWireJson()
    {
        var conversationId = Guid.NewGuid();
        var recent = new List<ConversationMessage>
        {
            ConversationMessage.Create(
                conversationId,
                0,
                MessageRole.User,
                "first",
                1,
                DateTimeOffset.UtcNow,
                """{"role":"user","content":"first","name":"alice"}"""),
            ConversationMessage.Create(conversationId, 1, MessageRole.Assistant, "second", 1, DateTimeOffset.UtcNow)
        };
        var currentMessage = new ChatMessage(MessageRole.User, "third");

        var result = _builder.Build("System.", null, recent, currentMessage);

        Assert.Equal(4, result.Count);
        Assert.Equal("first", result[1].Content);
        Assert.NotNull(result[1].RawWireMessage);
        Assert.Equal("alice", result[1].RawWireMessage!.Value.GetProperty("name").GetString());
        Assert.Equal("second", result[2].Content);
        Assert.Equal("third", result[3].Content);
    }

    [Fact]
    public void Build_WithRecentRawMessages_IncludesToolMessagesInOrder()
    {
        var conversationId = Guid.NewGuid();
        var recent = new List<ConversationMessage>
        {
            ConversationMessage.Create(conversationId, 0, MessageRole.User, "hi", 1, DateTimeOffset.UtcNow),
            ConversationMessage.Create(
                conversationId,
                1,
                MessageRole.Tool,
                "tool-body",
                1,
                DateTimeOffset.UtcNow,
                """{"role":"tool","tool_call_id":"c1","content":"tool-body"}""")
        };
        var currentMessage = new ChatMessage(MessageRole.User, "next");

        var result = _builder.Build("System.", null, recent, currentMessage);

        Assert.Equal(4, result.Count);
        Assert.Equal(MessageRole.User, result[1].Role);
        Assert.Equal(MessageRole.Tool, result[2].Role);
        Assert.Equal("tool-body", result[2].Content);
        Assert.Equal(MessageRole.User, result[3].Role);
    }
}
