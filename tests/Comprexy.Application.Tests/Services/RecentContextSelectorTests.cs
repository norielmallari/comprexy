using Comprexy.Application.Configuration;
using Comprexy.Application.Services;
using Comprexy.Domain.Entities;
using Comprexy.Domain.Enums;
using Microsoft.Extensions.Options;

namespace Comprexy.Application.Tests.Services;

public class RecentContextSelectorTests
{
    [Fact]
    public void Select_RespectsMessageCountCap()
    {
        var selector = CreateSelector(recentMessageCount: 2, maxRecentRawTokens: 10_000);
        var conversationId = Guid.NewGuid();
        var messages = Enumerable.Range(0, 5)
            .Select(i => ConversationMessage.Create(conversationId, i, MessageRole.User, $"m{i}", 10, DateTimeOffset.UtcNow))
            .ToList();

        var selected = selector.Select(messages);

        Assert.Equal(2, selected.Count);
        Assert.Equal(3, selected[0].Sequence);
        Assert.Equal(4, selected[1].Sequence);
    }

    [Fact]
    public void Select_RespectsTokenBudgetFromNewest()
    {
        var selector = CreateSelector(recentMessageCount: 8, maxRecentRawTokens: 25);
        var conversationId = Guid.NewGuid();
        var messages = new List<ConversationMessage>
        {
            ConversationMessage.Create(conversationId, 0, MessageRole.User, "old", 20, DateTimeOffset.UtcNow),
            ConversationMessage.Create(conversationId, 1, MessageRole.Assistant, "mid", 20, DateTimeOffset.UtcNow),
            ConversationMessage.Create(conversationId, 2, MessageRole.User, "new", 20, DateTimeOffset.UtcNow)
        };

        var selected = selector.Select(messages);

        Assert.Single(selected);
        Assert.Equal(2, selected[0].Sequence);
    }

    [Fact]
    public void Select_IncludesOversizedNewestMessageAlone()
    {
        var selector = CreateSelector(recentMessageCount: 8, maxRecentRawTokens: 10);
        var conversationId = Guid.NewGuid();
        var messages = new List<ConversationMessage>
        {
            ConversationMessage.Create(conversationId, 0, MessageRole.User, "huge", 50, DateTimeOffset.UtcNow)
        };

        var selected = selector.Select(messages);

        Assert.Single(selected);
    }

    [Fact]
    public void Select_KeepsAssistantToolCallChainAtomic()
    {
        // Cap would normally keep only the newest tool message; chain must stay intact.
        var selector = CreateSelector(recentMessageCount: 1, maxRecentRawTokens: 10_000);
        var conversationId = Guid.NewGuid();
        var messages = new List<ConversationMessage>
        {
            ConversationMessage.Create(conversationId, 0, MessageRole.User, "read the file", 5, DateTimeOffset.UtcNow),
            ConversationMessage.Create(conversationId, 1, MessageRole.Assistant, string.Empty, 5, DateTimeOffset.UtcNow,
                """{"role":"assistant","content":null,"tool_calls":[{"id":"call_1","type":"function","function":{"name":"read_file","arguments":"{}"}}]}"""),
            ConversationMessage.Create(conversationId, 2, MessageRole.Tool, "file contents", 5, DateTimeOffset.UtcNow,
                """{"role":"tool","tool_call_id":"call_1","content":"file contents"}""")
        };

        var selected = selector.Select(messages);

        Assert.Equal(2, selected.Count);
        Assert.Equal(MessageRole.Assistant, selected[0].Role);
        Assert.Equal(MessageRole.Tool, selected[1].Role);
        Assert.Equal(1, selected[0].Sequence);
        Assert.Equal(2, selected[1].Sequence);
    }

    [Fact]
    public void Select_DropsLeadingOrphanToolMessages()
    {
        var selector = CreateSelector(recentMessageCount: 8, maxRecentRawTokens: 10_000);
        var conversationId = Guid.NewGuid();
        var messages = new List<ConversationMessage>
        {
            ConversationMessage.Create(conversationId, 0, MessageRole.Tool, "orphan", 5, DateTimeOffset.UtcNow),
            ConversationMessage.Create(conversationId, 1, MessageRole.User, "hello", 5, DateTimeOffset.UtcNow)
        };

        var selected = selector.Select(messages);

        Assert.Single(selected);
        Assert.Equal(MessageRole.User, selected[0].Role);
    }

    [Fact]
    public void Select_DoesNotSplitToolChainOnTokenBudget()
    {
        var selector = CreateSelector(recentMessageCount: 8, maxRecentRawTokens: 15);
        var conversationId = Guid.NewGuid();
        var messages = new List<ConversationMessage>
        {
            ConversationMessage.Create(conversationId, 0, MessageRole.User, "older", 20, DateTimeOffset.UtcNow),
            ConversationMessage.Create(conversationId, 1, MessageRole.Assistant, string.Empty, 10, DateTimeOffset.UtcNow),
            ConversationMessage.Create(conversationId, 2, MessageRole.Tool, "result", 10, DateTimeOffset.UtcNow)
        };

        var selected = selector.Select(messages);

        Assert.Equal(2, selected.Count);
        Assert.Equal(MessageRole.Assistant, selected[0].Role);
        Assert.Equal(MessageRole.Tool, selected[1].Role);
    }

    private static RecentContextSelector CreateSelector(int recentMessageCount, int maxRecentRawTokens) =>
        new(Options.Create(new ContextPolicyOptions
        {
            CompressionRetainMessageCount = recentMessageCount,
            EmergencyRecentMessageCount = 1,
            MaxRecentRawTokens = maxRecentRawTokens
        }));
}
