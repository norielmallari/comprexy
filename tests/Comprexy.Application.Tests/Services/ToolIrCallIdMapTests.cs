using Comprexy.Application.Abstractions;
using Comprexy.Application.Configuration;
using Comprexy.Application.Services.ToolIr;
using Microsoft.Extensions.Options;
using Moq;

namespace Comprexy.Application.Tests.Services;

public class ToolIrCallIdMapTests
{
    private readonly Mock<IClock> _clock = new();
    private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public ToolIrCallIdMapTests()
    {
        _clock.Setup(c => c.UtcNow).Returns(() => _now);
    }

    private ToolIrCallIdMap CreateMap(
        TimeSpan? pendingTtl = null,
        int maxConversations = 1024)
    {
        var options = Options.Create(new ToolSchemaOptions
        {
            CallIdMapPendingAbsoluteExpiration = pendingTtl ?? TimeSpan.FromMinutes(30),
            CallIdMapMaxConversations = maxConversations
        });
        return new ToolIrCallIdMap(_clock.Object, options);
    }

    private static ToolIrCallMapping Mapping(
        Guid conversationId,
        string irCallId,
        string clientCallId,
        bool pending = true) =>
        new(
            conversationId,
            irCallId,
            clientCallId,
            "comprexy_read_file_range",
            "Read",
            """{"path":"a.cs"}""",
            """{"path":"a.cs"}""",
            "read_then_slice",
            "a.cs",
            1,
            1,
            pending);

    [Fact]
    public void TryGetByClientId_AfterPendingTtl_ExpiresAndRemovesEntry()
    {
        var map = CreateMap(pendingTtl: TimeSpan.FromMinutes(5));
        var conversationId = Guid.NewGuid();
        map.Register(Mapping(conversationId, "ir_1", "cur_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"));

        Assert.True(map.TryGetByClientId(conversationId, "cur_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", out _));

        _now = _now.AddMinutes(5);
        Assert.False(map.TryGetByClientId(conversationId, "cur_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", out _));
        Assert.Empty(map.GetPendingClientIds(conversationId));
    }

    [Fact]
    public void Register_WhenConversationCapExceeded_EvictsLeastRecentlyActive()
    {
        var map = CreateMap(maxConversations: 2);
        var convA = Guid.NewGuid();
        var convB = Guid.NewGuid();
        var convC = Guid.NewGuid();

        map.Register(Mapping(convA, "ir_a", "cur_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"));
        _now = _now.AddSeconds(1);
        map.Register(Mapping(convB, "ir_b", "cur_bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"));
        _now = _now.AddSeconds(1);
        map.Register(Mapping(convC, "ir_c", "cur_cccccccccccccccccccccccccccccccc"));

        Assert.False(map.TryGetByClientId(convA, "cur_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", out _));
        Assert.True(map.TryGetByClientId(convB, "cur_bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", out _));
        Assert.True(map.TryGetByClientId(convC, "cur_cccccccccccccccccccccccccccccccc", out _));
    }

    [Fact]
    public void ClearIfNoOpenToolCalls_ClearsWhenClosed_KeepsWhenOpen()
    {
        var map = CreateMap();
        var conversationId = Guid.NewGuid();
        map.Register(Mapping(conversationId, "ir_keep", "cur_dddddddddddddddddddddddddddddddd"));

        map.ClearIfNoOpenToolCalls(conversationId, assistantHasOpenToolCalls: true);
        Assert.Contains("cur_dddddddddddddddddddddddddddddddd", map.GetPendingClientIds(conversationId));

        map.ClearIfNoOpenToolCalls(conversationId, assistantHasOpenToolCalls: false);
        Assert.Empty(map.GetPendingClientIds(conversationId));
        Assert.False(map.TryGetByIrId(conversationId, "ir_keep", out _));
    }
}
