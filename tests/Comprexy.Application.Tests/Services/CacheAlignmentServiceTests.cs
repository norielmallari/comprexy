using Comprexy.Application.Configuration;
using Comprexy.Application.Models;
using Comprexy.Application.Services.CacheAlignment;
using Comprexy.Domain.Entities;
using Comprexy.Domain.Enums;
using Microsoft.Extensions.Options;

namespace Comprexy.Application.Tests.Services;

public class CacheAlignmentServiceTests
{
    private static CacheAlignmentService CreateService(int maxConversations = 1024) =>
        new(Options.Create(new CacheAlignmentOptions
        {
            Enabled = true,
            MaxConversations = maxConversations
        }));

    [Fact]
    public void AppendTip_DoesNotChangePrefixBytes()
    {
        var service = CreateService();
        var conversationId = Guid.NewGuid();
        var prefix = new List<ChatMessage>
        {
            new(MessageRole.System, "You are helpful."),
            new(MessageRole.User, "hello")
        };
        Assert.True(service.TryStorePrefix(conversationId, prefix, [], 0, 0, null));
        var before = service.GetSnapshot(conversationId)!.Prefix.ToList();

        service.AppendTip(conversationId, Guid.NewGuid());

        var after = service.GetSnapshot(conversationId)!.Prefix;
        Assert.True(CacheAlignmentService.ArePrefixEqual(before, after));
    }

    [Fact]
    public void TryCommitWorkingMemory_RebuildsPrefixAndTrimsFoldedSuffix()
    {
        var service = CreateService();
        var conversationId = Guid.NewGuid();
        var foldedId = Guid.NewGuid();
        var keepId = Guid.NewGuid();
        var prefix = new List<ChatMessage> { new(MessageRole.System, "sys") };
        Assert.True(service.TryStorePrefix(conversationId, prefix, [], 0, 0, null));
        service.ReplaceSuffix(conversationId, [foldedId, keepId]);

        var newPrefix = new List<ChatMessage>
        {
            new(MessageRole.System, "sys"),
            new(MessageRole.System, "wm v1")
        };
        Assert.True(service.TryCommitWorkingMemory(
            conversationId,
            newPrefix,
            [keepId],
            workingMemoryVersion: 1,
            retainFrontierWatermark: 3,
            foldedMessageIds: new HashSet<Guid> { foldedId }));

        var snap = service.GetSnapshot(conversationId)!;
        Assert.Equal(1, snap.WorkingMemoryVersion);
        Assert.Equal(3, snap.RetainFrontierWatermark);
        Assert.True(CacheAlignmentService.ArePrefixEqual(newPrefix, snap.Prefix));
        Assert.Equal([keepId], snap.SuffixMessageIds);
    }

    [Fact]
    public void MaterializeLive_OmitDoesNotMutateStoredPrefixOrSuffix()
    {
        var service = CreateService();
        var conversationId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var msgA = ConversationMessage.Create(conversationId, 0, MessageRole.User, "a", 1, now);
        var msgB = ConversationMessage.Create(conversationId, 1, MessageRole.User, "b", 1, now);
        var prefix = new List<ChatMessage>
        {
            new(MessageRole.System, "sys"),
            new(MessageRole.User, "a")
        };
        Assert.True(service.TryStorePrefix(conversationId, prefix, [msgA.Id], 0, 0, null));
        service.ReplaceSuffix(conversationId, [msgB.Id]);
        var before = service.GetSnapshot(conversationId)!;

        var byId = new Dictionary<Guid, ConversationMessage>
        {
            [msgA.Id] = msgA,
            [msgB.Id] = msgB
        };
        var projected = service.MaterializeLive(
            conversationId,
            byId,
            corpus => corpus.Where(m => m.Id != msgA.Id).ToList());

        Assert.DoesNotContain(projected, m => m.Content == "a");
        var after = service.GetSnapshot(conversationId)!;
        Assert.True(CacheAlignmentService.ArePrefixEqual(before.Prefix, after.Prefix));
        Assert.Equal(before.SuffixMessageIds, after.SuffixMessageIds);
    }

    [Fact]
    public void ProjectWrapUp_StopTurn_AppendsAssistantAndTip_WithoutMutatingStore()
    {
        var service = CreateService();
        var conversationId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var tip = ConversationMessage.Create(conversationId, 0, MessageRole.User, "tip", 1, now);
        var prefix = new List<ChatMessage> { new(MessageRole.System, "sys") };
        Assert.True(service.TryStorePrefix(conversationId, prefix, [], 0, 0, null));
        service.ReplaceSuffix(conversationId, [tip.Id]);
        var before = service.GetSnapshot(conversationId)!;

        var projection = service.ProjectWrapUp(
            conversationId,
            CacheAlignmentWrapUpMode.StopTurn,
            new ChatMessage(MessageRole.Assistant, "answer"),
            new ChatMessage(MessageRole.User, "wrap-up tip"),
            new Dictionary<Guid, ConversationMessage> { [tip.Id] = tip });

        Assert.False(projection.SoftFailed);
        Assert.Equal(4, projection.Messages.Count);
        Assert.Equal("answer", projection.Messages[^2].Content);
        Assert.Equal("wrap-up tip", projection.Messages[^1].Content);
        var after = service.GetSnapshot(conversationId)!;
        Assert.True(CacheAlignmentService.ArePrefixEqual(before.Prefix, after.Prefix));
    }

    [Fact]
    public void ProjectWrapUp_MidChain_AppendsTipOnly()
    {
        var service = CreateService();
        var conversationId = Guid.NewGuid();
        var prefix = new List<ChatMessage> { new(MessageRole.System, "sys") };
        Assert.True(service.TryStorePrefix(conversationId, prefix, [], 0, 0, null));

        var projection = service.ProjectWrapUp(
            conversationId,
            CacheAlignmentWrapUpMode.MidChainPrefix,
            new ChatMessage(MessageRole.Assistant, "open"),
            new ChatMessage(MessageRole.User, "wrap-up tip"),
            new Dictionary<Guid, ConversationMessage>());

        Assert.False(projection.SoftFailed);
        Assert.Equal(2, projection.Messages.Count);
        Assert.Equal("wrap-up tip", projection.Messages[^1].Content);
    }

    [Fact]
    public void ProjectWrapUp_MidChain_OpenRepairableSuffix_RebuildsClosedWorld_IgnoresOpenLive()
    {
        var service = CreateService();
        var conversationId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var closedUser = ConversationMessage.Create(conversationId, 0, MessageRole.User, "closed", 1, now);
        var openAssistant = ConversationMessage.Create(
            conversationId,
            1,
            MessageRole.Assistant,
            string.Empty,
            1,
            now,
            """{"role":"assistant","tool_calls":[{"id":"call_open","type":"function","function":{"name":"lookup","arguments":"{}"}}]}""");
        var tipUser = ConversationMessage.Create(conversationId, 2, MessageRole.User, "next", 1, now);
        var prefix = new List<ChatMessage>
        {
            new(MessageRole.System, "sys"),
            new(MessageRole.User, "closed")
        };
        Assert.True(service.TryStorePrefix(conversationId, prefix, [closedUser.Id], 0, 0, null));
        service.ReplaceSuffix(conversationId, [openAssistant.Id, tipUser.Id]);

        var openLive = new List<ChatMessage>
        {
            new(MessageRole.System, "sys"),
            new(MessageRole.User, "closed"),
            new(
                MessageRole.Assistant,
                string.Empty,
                System.Text.Json.JsonDocument.Parse(
                    """{"role":"assistant","tool_calls":[{"id":"call_open","type":"function","function":{"name":"lookup","arguments":"{}"}}]}""")
                    .RootElement.Clone()),
            new(MessageRole.User, "next")
        };

        var projection = service.ProjectWrapUp(
            conversationId,
            CacheAlignmentWrapUpMode.MidChainPrefix,
            visibleAssistant: null,
            wrapUpTip: new ChatMessage(MessageRole.User, "wrap-up tip"),
            messagesById: new Dictionary<Guid, ConversationMessage>
            {
                [closedUser.Id] = closedUser,
                [openAssistant.Id] = openAssistant,
                [tipUser.Id] = tipUser
            },
            liveMessages: openLive);

        Assert.False(projection.SoftFailed);
        Assert.Equal("wrap-up tip", projection.Messages[^1].Content);
        Assert.DoesNotContain(
            projection.Messages,
            m => m.Role == MessageRole.Assistant
                 && m.RawWireMessage is { } wire
                 && wire.TryGetProperty("tool_calls", out var toolCalls)
                 && toolCalls.ValueKind == System.Text.Json.JsonValueKind.Array
                 && toolCalls.GetArrayLength() > 0);
        Assert.Contains(projection.Messages, m => m.Content == "next");
    }

    [Fact]
    public void ProjectWrapUp_MidChain_OpenUnrepairableSuffix_SoftFails()
    {
        var service = CreateService();
        var conversationId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        // Open assistant excluded, but a following tool with no extractable tool_call_id stays
        // and becomes an orphan after exclusion → TryEnsureWrapUpReady fails closed.
        var openAssistant = ConversationMessage.Create(
            conversationId,
            0,
            MessageRole.Assistant,
            string.Empty,
            1,
            now,
            """{"role":"assistant","tool_calls":[{"id":"call_open","type":"function","function":{"name":"lookup","arguments":"{}"}}]}""");
        var orphanShapedTool = ConversationMessage.Create(
            conversationId,
            1,
            MessageRole.Tool,
            "no id",
            1,
            now,
            """{"role":"tool","content":"no id"}""");
        var prefix = new List<ChatMessage> { new(MessageRole.System, "sys") };
        Assert.True(service.TryStorePrefix(conversationId, prefix, [], 0, 0, null));
        service.ReplaceSuffix(conversationId, [openAssistant.Id, orphanShapedTool.Id]);

        var projection = service.ProjectWrapUp(
            conversationId,
            CacheAlignmentWrapUpMode.MidChainPrefix,
            visibleAssistant: null,
            wrapUpTip: new ChatMessage(MessageRole.User, "wrap-up tip"),
            messagesById: new Dictionary<Guid, ConversationMessage>
            {
                [openAssistant.Id] = openAssistant,
                [orphanShapedTool.Id] = orphanShapedTool
            });

        Assert.True(projection.SoftFailed);
        Assert.Equal("suffix_open_unrepairable", projection.SoftFailReason);
        Assert.Empty(projection.Messages);
    }

    [Fact]
    public void Eviction_WhenOverMaxConversations_RemovesLeastRecentlyUsed()
    {
        var service = CreateService(maxConversations: 2);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        var prefix = new List<ChatMessage> { new(MessageRole.System, "sys") };

        Assert.True(service.TryStorePrefix(a, prefix, [], 0, 0, null));
        Assert.True(service.TryStorePrefix(b, prefix, [], 0, 0, null));
        _ = service.GetSnapshot(a); // touch a → b is LRU
        Assert.True(service.TryStorePrefix(c, prefix, [], 0, 0, null));

        Assert.NotNull(service.GetSnapshot(a));
        Assert.Null(service.GetSnapshot(b));
        Assert.NotNull(service.GetSnapshot(c));
    }

    [Fact]
    public void Invalidate_ForcesNextMaterializeThroughColdPath()
    {
        var service = CreateService();
        var conversationId = Guid.NewGuid();
        var prefix = new List<ChatMessage> { new(MessageRole.System, "sys") };
        Assert.True(service.TryStorePrefix(conversationId, prefix, [], 0, 0, null));
        Assert.NotNull(service.GetSnapshot(conversationId));

        service.Invalidate(conversationId);

        Assert.Null(service.GetSnapshot(conversationId));
        Assert.Empty(service.MaterializeLive(conversationId, new Dictionary<Guid, ConversationMessage>()));
    }

    [Fact]
    public void MaterializeLive_WithPendingRuleMessages_SplicesAfterBaseSystemWithoutMutatingPrefix()
    {
        var service = CreateService();
        var conversationId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var msgA = ConversationMessage.Create(conversationId, 0, MessageRole.User, "a", 1, now);
        var msgB = ConversationMessage.Create(conversationId, 1, MessageRole.User, "b", 1, now);
        var prefix = new List<ChatMessage>
        {
            new(MessageRole.System, "base"),
            new(MessageRole.System, "wm"),
            new(MessageRole.User, "a")
        };
        Assert.True(service.TryStorePrefix(conversationId, prefix, [msgA.Id], 1, 0, null));
        service.ReplaceSuffix(conversationId, [msgB.Id]);
        var before = service.GetSnapshot(conversationId)!;
        var pending = new List<ChatMessage>
        {
            new(MessageRole.System, "[Rule: scoped.md] scoped body")
        };

        var projected = service.MaterializeLive(
            conversationId,
            new Dictionary<Guid, ConversationMessage> { [msgA.Id] = msgA, [msgB.Id] = msgB },
            pendingRuleMessages: pending);

        Assert.Equal("base", projected[0].Content);
        Assert.Contains("scoped body", projected[1].Content);
        Assert.Equal("wm", projected[2].Content);
        Assert.Equal("b", projected[^1].Content);
        var after = service.GetSnapshot(conversationId)!;
        Assert.True(CacheAlignmentService.ArePrefixEqual(before.Prefix, after.Prefix));
    }
}
