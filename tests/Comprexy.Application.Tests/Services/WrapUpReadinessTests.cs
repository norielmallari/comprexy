using Comprexy.Application.Services;
using Comprexy.Application.Services.CacheAlignment;
using Comprexy.Domain.Entities;
using Comprexy.Domain.Enums;

namespace Comprexy.Application.Tests.Services;

public class WrapUpReadinessTests
{
    [Fact]
    public void IsWrapUpReady_OpenToolCallWithoutResult_ReturnsFalse()
    {
        var conversationId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var frontier = new List<ConversationMessage>
        {
            ConversationMessage.Create(conversationId, 0, MessageRole.User, "go", 1, now),
            ConversationMessage.Create(
                conversationId,
                1,
                MessageRole.Assistant,
                string.Empty,
                1,
                now,
                """{"role":"assistant","tool_calls":[{"id":"call_1","type":"function","function":{"name":"lookup","arguments":"{}"}}]}""")
        };

        Assert.False(WrapUpReadiness.IsWrapUpReady(frontier));
    }

    [Fact]
    public void TryEnsureWrapUpReady_OpenAssistant_ExcludesAndReturnsReady()
    {
        var conversationId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var user = ConversationMessage.Create(conversationId, 0, MessageRole.User, "go", 1, now);
        var openAssistant = ConversationMessage.Create(
            conversationId,
            1,
            MessageRole.Assistant,
            string.Empty,
            1,
            now,
            """{"role":"assistant","tool_calls":[{"id":"call_1","type":"function","function":{"name":"lookup","arguments":"{}"}}]}""");

        var ok = WrapUpReadiness.TryEnsureWrapUpReady(
            [user, openAssistant],
            out var prefix,
            out var excluded);

        Assert.True(ok);
        Assert.Single(prefix);
        Assert.Equal(user.Id, prefix[0].Id);
        Assert.Single(excluded);
        Assert.Equal(openAssistant.Id, excluded[0].Id);
        Assert.True(WrapUpReadiness.IsWrapUpReady(prefix));
    }

    [Fact]
    public void TryEnsureWrapUpReady_OrphanTool_DroppedViaChatTemplateMessageOrder()
    {
        var conversationId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var orphanTool = ConversationMessage.Create(
            conversationId,
            0,
            MessageRole.Tool,
            "orphan",
            1,
            now,
            """{"role":"tool","tool_call_id":"call_x","content":"orphan"}""");
        var user = ConversationMessage.Create(conversationId, 1, MessageRole.User, "hi", 1, now);

        var ok = WrapUpReadiness.TryEnsureWrapUpReady([orphanTool, user], out var prefix, out var excluded);

        Assert.True(ok);
        Assert.Single(prefix);
        Assert.Equal(user.Id, prefix[0].Id);
        Assert.Empty(excluded);
    }

    [Fact]
    public void TryEnsureWrapUpReady_ClosedChain_KeepsAll()
    {
        var conversationId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var assistant = ConversationMessage.Create(
            conversationId,
            0,
            MessageRole.Assistant,
            string.Empty,
            1,
            now,
            """{"role":"assistant","tool_calls":[{"id":"call_1","type":"function","function":{"name":"lookup","arguments":"{}"}}]}""");
        var tool = ConversationMessage.Create(
            conversationId,
            1,
            MessageRole.Tool,
            "result",
            1,
            now,
            """{"role":"tool","tool_call_id":"call_1","content":"result"}""");

        var ok = WrapUpReadiness.TryEnsureWrapUpReady([assistant, tool], out var prefix, out var excluded);

        Assert.True(ok);
        Assert.Equal(2, prefix.Count);
        Assert.Empty(excluded);
    }

    [Fact]
    public void TryEnsureWrapUpReady_PartialBatch_ExcludesMatchedAndOpenToolsOfOpenAssistant()
    {
        var conversationId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var closedUser = ConversationMessage.Create(conversationId, 0, MessageRole.User, "older", 1, now);
        var closedAssistant = ConversationMessage.Create(
            conversationId,
            1,
            MessageRole.Assistant,
            string.Empty,
            1,
            now,
            """{"role":"assistant","tool_calls":[{"id":"call_closed","type":"function","function":{"name":"lookup","arguments":"{}"}}]}""");
        var closedTool = ConversationMessage.Create(
            conversationId,
            2,
            MessageRole.Tool,
            "closed result",
            1,
            now,
            """{"role":"tool","tool_call_id":"call_closed","content":"closed result"}""");
        var openAssistant = ConversationMessage.Create(
            conversationId,
            3,
            MessageRole.Assistant,
            string.Empty,
            1,
            now,
            """{"role":"assistant","tool_calls":[{"id":"call_a","type":"function","function":{"name":"lookup","arguments":"{}"}},{"id":"call_b","type":"function","function":{"name":"lookup","arguments":"{}"}}]}""");
        var matchedTool = ConversationMessage.Create(
            conversationId,
            4,
            MessageRole.Tool,
            "partial a",
            1,
            now,
            """{"role":"tool","tool_call_id":"call_a","content":"partial a"}""");

        var ok = WrapUpReadiness.TryEnsureWrapUpReady(
            [closedUser, closedAssistant, closedTool, openAssistant, matchedTool],
            out var prefix,
            out var excluded);

        Assert.True(ok);
        Assert.True(WrapUpReadiness.IsWrapUpReady(prefix));
        Assert.Equal(3, prefix.Count);
        Assert.Equal(
            new[] { closedUser.Id, closedAssistant.Id, closedTool.Id },
            prefix.Select(m => m.Id).ToArray());
        Assert.Contains(excluded, m => m.Id == openAssistant.Id);
        Assert.Contains(excluded, m => m.Id == matchedTool.Id);
        Assert.DoesNotContain(prefix, m => m.Id == matchedTool.Id);
        Assert.DoesNotContain(prefix, m => m.Id == openAssistant.Id);
    }

    [Fact]
    public void TryEnsureWrapUpReady_OnlyOpenTip_ReturnsEmptyClosedPrefix()
    {
        var conversationId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var openAssistant = ConversationMessage.Create(
            conversationId,
            0,
            MessageRole.Assistant,
            string.Empty,
            1,
            now,
            """{"role":"assistant","tool_calls":[{"id":"call_1","type":"function","function":{"name":"lookup","arguments":"{}"}}]}""");

        var ok = WrapUpReadiness.TryEnsureWrapUpReady(
            [openAssistant],
            out var prefix,
            out var excluded);

        Assert.True(ok);
        Assert.Empty(prefix);
        Assert.Single(excluded);
        Assert.Equal(openAssistant.Id, excluded[0].Id);
    }

    [Fact]
    public void TryEnsureWrapUpReady_UnrepairableOrphanAfterExclusion_ReturnsFalse()
    {
        var conversationId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var openAssistant = ConversationMessage.Create(
            conversationId,
            0,
            MessageRole.Assistant,
            string.Empty,
            1,
            now,
            """{"role":"assistant","tool_calls":[{"id":"call_1","type":"function","function":{"name":"lookup","arguments":"{}"}}]}""");
        var toolWithoutCallId = ConversationMessage.Create(
            conversationId,
            1,
            MessageRole.Tool,
            "no id",
            1,
            now,
            """{"role":"tool","content":"no id"}""");

        var ok = WrapUpReadiness.TryEnsureWrapUpReady(
            [openAssistant, toolWithoutCallId],
            out _,
            out _);

        Assert.False(ok);
    }
}
