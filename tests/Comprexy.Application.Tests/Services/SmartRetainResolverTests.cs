using Comprexy.Application.Services;
using Comprexy.Domain.Entities;
using Comprexy.Domain.Enums;

namespace Comprexy.Application.Tests.Services;

public class SmartRetainResolverTests
{
    private static ConversationMessage Msg(Guid conversationId, int sequence, int tokens = 10, MessageRole role = MessageRole.User) =>
        ConversationMessage.Create(conversationId, sequence, role, $"m-{sequence}", tokens, DateTimeOffset.UtcNow);

    private static ConversationMessage AssistantWithToolCall(Guid conversationId, int sequence, string callId, int tokens = 10) =>
        ConversationMessage.Create(
            conversationId,
            sequence,
            MessageRole.Assistant,
            string.Empty,
            tokens,
            DateTimeOffset.UtcNow,
            $"{{\"role\":\"assistant\",\"tool_calls\":[{{\"id\":\"{callId}\",\"type\":\"function\",\"function\":{{\"name\":\"Read\",\"arguments\":\"{{}}\"}}}}]}}");

    private static ConversationMessage ToolResult(Guid conversationId, int sequence, string callId, int tokens = 10) =>
        ConversationMessage.Create(
            conversationId,
            sequence,
            MessageRole.Tool,
            "ok",
            tokens,
            DateTimeOffset.UtcNow,
            $"{{\"role\":\"tool\",\"tool_call_id\":\"{callId}\",\"content\":\"ok\"}}");

    [Fact]
    public void Resolve_DropsUnknownSequencesAndDedupes()
    {
        var conversationId = Guid.NewGuid();
        var candidates = new[] { Msg(conversationId, 1), Msg(conversationId, 2), Msg(conversationId, 3) };
        var tip = candidates[^1];

        var retain = SmartRetainResolver.Resolve([99, 2, 2, 1], candidates, tip, maxMessages: 8, maxTokens: 1000);

        Assert.Equal([1, 2, 3], retain.Select(m => m.Sequence).ToArray());
    }

    [Fact]
    public void Resolve_ForceIncludesTipWhenOmitted()
    {
        var conversationId = Guid.NewGuid();
        var candidates = new[] { Msg(conversationId, 1), Msg(conversationId, 2), Msg(conversationId, 3) };

        var retain = SmartRetainResolver.Resolve([1], candidates, candidates[^1], maxMessages: 8, maxTokens: 1000);

        Assert.Equal([1, 3], retain.Select(m => m.Sequence).ToArray());
    }

    [Fact]
    public void Resolve_ClampsByMessageCountDroppingOldestNonTip()
    {
        var conversationId = Guid.NewGuid();
        var candidates = new[]
        {
            Msg(conversationId, 1),
            Msg(conversationId, 2),
            Msg(conversationId, 3),
            Msg(conversationId, 4)
        };

        var retain = SmartRetainResolver.Resolve(
            [1, 2, 3, 4],
            candidates,
            candidates[^1],
            maxMessages: 2,
            maxTokens: 1000);

        Assert.Equal([3, 4], retain.Select(m => m.Sequence).ToArray());
    }

    [Fact]
    public void Resolve_ClampsByTokenBudgetDroppingOldestNonTip()
    {
        var conversationId = Guid.NewGuid();
        var candidates = new[]
        {
            Msg(conversationId, 1, tokens: 10),
            Msg(conversationId, 2, tokens: 10),
            Msg(conversationId, 3, tokens: 10)
        };

        var retain = SmartRetainResolver.Resolve(
            [1, 2, 3],
            candidates,
            candidates[^1],
            maxMessages: 8,
            maxTokens: 20);

        Assert.Equal([2, 3], retain.Select(m => m.Sequence).ToArray());
        Assert.Equal(20, retain.Sum(m => m.TokenCount));
    }

    [Fact]
    public void Resolve_ExpandsNominatedToolToParentAssistantGroup()
    {
        var conversationId = Guid.NewGuid();
        var candidates = new[]
        {
            Msg(conversationId, 1),
            AssistantWithToolCall(conversationId, 2, "c1"),
            ToolResult(conversationId, 3, "c1"),
            Msg(conversationId, 4)
        };

        var retain = SmartRetainResolver.Resolve(
            [3],
            candidates,
            candidates[^1],
            maxMessages: 8,
            maxTokens: 1000);

        Assert.Equal([2, 3, 4], retain.Select(m => m.Sequence).ToArray());
    }

    [Fact]
    public void Resolve_ClampsDropsEntireAssistantToolGroupNotOrphanTools()
    {
        var conversationId = Guid.NewGuid();
        var candidates = new[]
        {
            AssistantWithToolCall(conversationId, 1, "c1", tokens: 10),
            ToolResult(conversationId, 2, "c1", tokens: 10),
            Msg(conversationId, 3, tokens: 10)
        };

        var retain = SmartRetainResolver.Resolve(
            [1, 2, 3],
            candidates,
            candidates[^1],
            maxMessages: 1,
            maxTokens: 1000);

        Assert.Equal([3], retain.Select(m => m.Sequence).ToArray());
        Assert.DoesNotContain(retain, m => m.Role == MessageRole.Tool);
    }

    [Fact]
    public void FindForcedTip_SkipsTrailingSystemMessages()
    {
        var conversationId = Guid.NewGuid();
        var messages = new[]
        {
            Msg(conversationId, 1),
            Msg(conversationId, 2, role: MessageRole.Assistant),
            Msg(conversationId, 3, role: MessageRole.System)
        };

        var tip = SmartRetainResolver.FindForcedTip(messages);

        Assert.Equal(2, tip!.Sequence);
    }
}
