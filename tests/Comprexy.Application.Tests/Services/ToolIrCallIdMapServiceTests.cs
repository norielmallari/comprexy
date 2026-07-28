using Comprexy.Application.Abstractions;
using Comprexy.Application.Configuration;
using Comprexy.Application.Services.ToolIr;
using Microsoft.Extensions.Options;
using Moq;

namespace Comprexy.Application.Tests.Services;

public class ToolIrCallIdMapServiceTests
{
    private readonly Mock<IClock> _clock = new();
    private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private Func<Task>? _onSaveChanges;

    public ToolIrCallIdMapServiceTests()
    {
        _clock.Setup(c => c.UtcNow).Returns(() => _now);
    }

    private (ToolIrCallIdMap HotCache, InMemoryConversationToolCallMapRepository Repo, ToolIrCallIdMapService Service)
        CreateSut(TimeSpan? pendingTtl = null)
    {
        var options = Options.Create(new ToolSchemaOptions
        {
            CallIdMapPendingAbsoluteExpiration = pendingTtl ?? TimeSpan.FromMinutes(30)
        });
        var hotCache = new ToolIrCallIdMap(_clock.Object, options);
        var repo = new InMemoryConversationToolCallMapRepository();
        var factory = new InMemoryToolIrCallIdMapUnitOfWorkFactory(
            repo,
            () => _onSaveChanges?.Invoke() ?? Task.CompletedTask);
        var service = new ToolIrCallIdMapService(
            hotCache,
            factory,
            _clock.Object,
            options);
        return (hotCache, repo, service);
    }

    private static ToolIrCallMapping Mapping(
        Guid conversationId,
        string irCallId,
        string clientCallId) =>
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
            Pending: true);

    [Fact]
    public async Task RegisterAsync_CommitsBeforeHotCache_AndPersistsRow()
    {
        var (hotCache, repo, service) = CreateSut();
        var conversationId = Guid.NewGuid();
        var mapping = Mapping(conversationId, "ir_1", "cur_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        var saveOrder = new List<string>();
        _onSaveChanges = () =>
        {
            saveOrder.Add("save");
            Assert.Single(repo.Rows);
            Assert.False(hotCache.TryGetByClientId(conversationId, mapping.ClientCallId, out _));
            return Task.CompletedTask;
        };

        await service.RegisterAsync(mapping, CancellationToken.None);
        saveOrder.Add("after");

        Assert.Equal(["save", "after"], saveOrder);
        Assert.True(hotCache.TryGetByClientId(conversationId, mapping.ClientCallId, out _));
        Assert.Single(repo.Rows);
        Assert.Equal(mapping.ClientCallId, repo.Rows[0].ClientCallId);
    }

    [Fact]
    public async Task TryGetByClientIdAsync_AfterMemoryDrop_HydratesFromRepository()
    {
        var (hotCache, repo, service) = CreateSut();
        var conversationId = Guid.NewGuid();
        const string clientId = "cur_bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        await service.RegisterAsync(Mapping(conversationId, "ir_2", clientId), CancellationToken.None);

        hotCache.ClearConversation(conversationId);
        Assert.False(hotCache.TryGetByClientId(conversationId, clientId, out _));
        Assert.Single(repo.Rows);

        var loaded = await service.TryGetByClientIdAsync(conversationId, clientId, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal("ir_2", loaded!.IrCallId);
        Assert.True(hotCache.TryGetByClientId(conversationId, clientId, out var cached));
        Assert.Equal("ir_2", cached!.IrCallId);
    }

    [Fact]
    public async Task CompleteAsync_RemovesRow_SecondGetReturnsNull()
    {
        var (hotCache, repo, service) = CreateSut();
        var conversationId = Guid.NewGuid();
        const string clientId = "cur_cccccccccccccccccccccccccccccccc";
        await service.RegisterAsync(Mapping(conversationId, "ir_3", clientId), CancellationToken.None);

        await service.CompleteAsync(conversationId, clientId, CancellationToken.None);

        Assert.Empty(repo.Rows);
        Assert.False(hotCache.TryGetByClientId(conversationId, clientId, out _));
        Assert.Null(await service.TryGetByClientIdAsync(conversationId, clientId, CancellationToken.None));
    }

    [Fact]
    public async Task ClearIfNoOpenToolCallsAsync_WhenClosed_ClearsEfAndMemory()
    {
        var (hotCache, repo, service) = CreateSut();
        var conversationId = Guid.NewGuid();
        await service.RegisterAsync(
            Mapping(conversationId, "ir_4", "cur_dddddddddddddddddddddddddddddddd"),
            CancellationToken.None);

        await service.ClearIfNoOpenToolCallsAsync(conversationId, assistantHasOpenToolCalls: true, CancellationToken.None);
        Assert.Single(repo.Rows);

        await service.ClearIfNoOpenToolCallsAsync(conversationId, assistantHasOpenToolCalls: false, CancellationToken.None);

        Assert.Empty(repo.Rows);
        Assert.Empty(hotCache.GetPendingClientIds(conversationId));
    }

    [Fact]
    public async Task TryGetByClientIdAsync_WhenTtlExpired_ReturnsNullAndDeletesRow()
    {
        var (hotCache, repo, service) = CreateSut(pendingTtl: TimeSpan.FromMinutes(5));
        var conversationId = Guid.NewGuid();
        const string clientId = "cur_eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";
        await service.RegisterAsync(Mapping(conversationId, "ir_5", clientId), CancellationToken.None);

        hotCache.ClearConversation(conversationId);
        _now = _now.AddMinutes(5);

        var loaded = await service.TryGetByClientIdAsync(conversationId, clientId, CancellationToken.None);

        Assert.Null(loaded);
        Assert.Empty(repo.Rows);
    }

    [Fact]
    public async Task RegisterAsync_UsesCallIdMapPendingAbsoluteExpirationFromOptions()
    {
        var (_, repo, service) = CreateSut(pendingTtl: TimeSpan.FromMinutes(10));
        var conversationId = Guid.NewGuid();
        await service.RegisterAsync(
            Mapping(conversationId, "ir_6", "cur_ffffffffffffffffffffffffffffffff"),
            CancellationToken.None);

        _now = _now.AddMinutes(9);
        Assert.NotNull(
            await service.TryGetByClientIdAsync(
                conversationId,
                "cur_ffffffffffffffffffffffffffffffff",
                CancellationToken.None));

        _now = _now.AddMinutes(2);
        // Opportunistic sweep on next register of another conversation entry.
        await service.RegisterAsync(
            Mapping(Guid.NewGuid(), "ir_other", "cur_11111111111111111111111111111111"),
            CancellationToken.None);

        Assert.DoesNotContain(repo.Rows, r => r.ClientCallId == "cur_ffffffffffffffffffffffffffffffff");
    }
}
