using Comprexy.Application.Abstractions;
using Comprexy.Application.Configuration;
using Comprexy.Application.Services.ToolIr;
using Comprexy.Domain.Entities;
using Comprexy.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;

namespace Comprexy.Application.Tests.Services;

public class ToolIrCallIdMapIsolationTests
{
    [Fact]
    public async Task RegisterAsync_DoesNotCommitDirtyChatEntities_EfSqlite()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ComprexyDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(new ClusterIdSaveChangesInterceptor())
            .Options;

        await using (var bootstrap = new ComprexyDbContext(options))
        {
            await bootstrap.Database.EnsureCreatedAsync();
        }

        await using var chatContext = new ComprexyDbContext(options);
        var dirtyConversation = Conversation.Create("uow-isolation", DateTimeOffset.UnixEpoch);
        chatContext.Conversations.Add(dirtyConversation);
        Assert.Equal(EntityState.Added, chatContext.Entry(dirtyConversation).State);

        var clock = new Mock<IClock>();
        clock.Setup(c => c.UtcNow).Returns(DateTimeOffset.UnixEpoch);
        var toolOptions = Options.Create(new ToolSchemaOptions
        {
            CallIdMapPendingAbsoluteExpiration = TimeSpan.FromMinutes(30)
        });
        var hotCache = new ToolIrCallIdMap(clock.Object, toolOptions);
        var factory = new EfToolIrCallIdMapUnitOfWorkFactory(new SharedConnectionDbContextFactory(options));
        var service = new ToolIrCallIdMapService(hotCache, factory, clock.Object, toolOptions);

        var conversationId = Guid.NewGuid();
        const string clientCallId = "cur_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        await service.RegisterAsync(
            new ToolIrCallMapping(
                conversationId,
                "ir_iso",
                clientCallId,
                "comprexy_read_file_range",
                "Read",
                """{"path":"a.cs"}""",
                """{"path":"a.cs"}""",
                "read_then_slice",
                "a.cs",
                1,
                1,
                Pending: true),
            CancellationToken.None);

        Assert.Equal(EntityState.Added, chatContext.Entry(dirtyConversation).State);

        await using (var verify = new ComprexyDbContext(options))
        {
            Assert.Equal(0, await verify.Conversations.CountAsync());
            var map = Assert.Single(await verify.ConversationToolCallMaps.ToListAsync());
            Assert.Equal(clientCallId, map.ClientCallId);
            Assert.Equal(conversationId, map.ConversationId);
        }

        Assert.True(hotCache.TryGetByClientId(conversationId, clientCallId, out _));
    }

    [Fact]
    public async Task RegisterAsync_DualStoreFake_DoesNotCommitChatDirtiness()
    {
        var chatCommitted = false;
        var mapRepo = new InMemoryConversationToolCallMapRepository();
        Func<Task>? mapSaveHook = null;
        var mapFactory = new InMemoryToolIrCallIdMapUnitOfWorkFactory(
            mapRepo,
            () => mapSaveHook?.Invoke() ?? Task.CompletedTask);

        var clock = new Mock<IClock>();
        clock.Setup(c => c.UtcNow).Returns(DateTimeOffset.UnixEpoch);
        var toolOptions = Options.Create(new ToolSchemaOptions
        {
            CallIdMapPendingAbsoluteExpiration = TimeSpan.FromMinutes(30)
        });
        var hotCache = new ToolIrCallIdMap(clock.Object, toolOptions);
        var service = new ToolIrCallIdMapService(hotCache, mapFactory, clock.Object, toolOptions);

        var conversationId = Guid.NewGuid();
        const string clientCallId = "cur_bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        mapSaveHook = () =>
        {
            Assert.False(chatCommitted);
            Assert.Single(mapRepo.Rows);
            Assert.False(hotCache.TryGetByClientId(conversationId, clientCallId, out _));
            return Task.CompletedTask;
        };

        await service.RegisterAsync(
            new ToolIrCallMapping(
                conversationId,
                "ir_fake",
                clientCallId,
                "comprexy_read_file_range",
                "Read",
                "{}",
                "{}",
                "passthrough",
                null,
                null,
                null,
                Pending: true),
            CancellationToken.None);

        Assert.False(chatCommitted);
        Assert.Single(mapRepo.Rows);
        Assert.True(hotCache.TryGetByClientId(conversationId, clientCallId, out _));
    }

    [Fact]
    public async Task RegisterAsync_TwoMaps_AssignDistinctClusterIds()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ComprexyDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(new ClusterIdSaveChangesInterceptor())
            .Options;

        await using (var bootstrap = new ComprexyDbContext(options))
        {
            await bootstrap.Database.EnsureCreatedAsync();
        }

        var clock = new Mock<IClock>();
        clock.Setup(c => c.UtcNow).Returns(DateTimeOffset.UnixEpoch);
        var toolOptions = Options.Create(new ToolSchemaOptions
        {
            CallIdMapPendingAbsoluteExpiration = TimeSpan.FromMinutes(30)
        });
        var hotCache = new ToolIrCallIdMap(clock.Object, toolOptions);
        var factory = new EfToolIrCallIdMapUnitOfWorkFactory(new SharedConnectionDbContextFactory(options));
        var service = new ToolIrCallIdMapService(hotCache, factory, clock.Object, toolOptions);
        var conversationId = Guid.NewGuid();

        await service.RegisterAsync(
            Mapping(conversationId, "ir_a", "cur_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"),
            CancellationToken.None);
        await service.RegisterAsync(
            Mapping(conversationId, "ir_b", "cur_bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"),
            CancellationToken.None);

        await using var verify = new ComprexyDbContext(options);
        var rows = await verify.ConversationToolCallMaps.AsNoTracking().ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal(2, rows.Select(r => r.ClusterId).Distinct().Count());
        Assert.All(rows, r => Assert.True(r.ClusterId > 0));
    }

    private static ToolIrCallMapping Mapping(Guid conversationId, string irCallId, string clientCallId) =>
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

    private sealed class SharedConnectionDbContextFactory(DbContextOptions<ComprexyDbContext> options)
        : IDbContextFactory<ComprexyDbContext>
    {
        public ComprexyDbContext CreateDbContext() => new(options);
    }
}
