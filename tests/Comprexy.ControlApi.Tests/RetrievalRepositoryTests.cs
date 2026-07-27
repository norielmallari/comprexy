using Comprexy.Domain.Entities;
using Comprexy.Domain.Enums;
using Comprexy.Infrastructure.Persistence;
using Comprexy.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Comprexy.ControlApi.Tests;

public sealed class RetrievalRepositoryTests
{
    [Fact]
    public async Task MessageRepository_SearchesRangesAndRecentWithBounds()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var conversation = Conversation.Create("retrieval-messages", DateTimeOffset.UnixEpoch);
        fixture.Context.Conversations.Add(conversation);
        var messages = new[]
        {
            Message(conversation.Id, 0, MessageRole.User, "alpha fingerprint"),
            Message(conversation.Id, 1, MessageRole.Assistant, "beta"),
            Message(conversation.Id, 2, MessageRole.User, "gamma fingerprint"),
            Message(conversation.Id, 3, MessageRole.Assistant, "delta")
        };
        messages[1].MarkFoldedInto(1);
        fixture.Context.ConversationMessages.AddRange(messages);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        var repository = new EfConversationMessageRepository(fixture.Context);

        var searchAll = await repository.SearchContentAsync(
            conversation.Id,
            "fingerprint",
            includeFolded: true,
            take: 10,
            CancellationToken.None);
        var searchUnfolded = await repository.SearchContentAsync(
            conversation.Id,
            "fingerprint",
            includeFolded: false,
            take: 10,
            CancellationToken.None);
        var window = await repository.ListBySequenceRangeAsync(
            conversation.Id,
            1,
            2,
            take: 10,
            CancellationToken.None);
        var recent = await repository.ListRecentAsync(
            conversation.Id,
            take: 2,
            unfoldedOnly: true,
            CancellationToken.None);

        Assert.Equal([2, 0], searchAll.Select(m => m.Sequence));
        Assert.Equal([2, 0], searchUnfolded.Select(m => m.Sequence));
        Assert.Equal([1, 2], window.Select(m => m.Sequence));
        Assert.Equal([2, 3], recent.Select(m => m.Sequence));
        Assert.Empty(fixture.Context.ChangeTracker.Entries<ConversationMessage>());
    }

    [Fact]
    public async Task WorkingMemoryRepository_GetsVersionAndSearchesContent()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var conversation = Conversation.Create("retrieval-wm", DateTimeOffset.UnixEpoch);
        fixture.Context.Conversations.Add(conversation);
        fixture.Context.WorkingMemories.AddRange(
            WorkingMemory.Create(conversation.Id, 1, "old goal", 2, DateTimeOffset.UnixEpoch),
            WorkingMemory.Create(conversation.Id, 2, "new fingerprint goal", 3, DateTimeOffset.UnixEpoch.AddMinutes(1)));
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        var repository = new EfWorkingMemoryRepository(fixture.Context);

        var latest = await repository.GetLatestAsync(conversation.Id, CancellationToken.None);
        var v1 = await repository.GetByVersionAsync(conversation.Id, 1, CancellationToken.None);
        var hits = await repository.SearchContentAsync(
            conversation.Id,
            "fingerprint",
            take: 5,
            CancellationToken.None);

        Assert.Equal(2, latest?.Version);
        Assert.Equal(1, v1?.Version);
        Assert.Equal([2], hits.Select(m => m.Version));
    }

    private static ConversationMessage Message(
        Guid conversationId,
        int sequence,
        MessageRole role,
        string content) =>
        ConversationMessage.Create(
            conversationId,
            sequence,
            role,
            content,
            tokenCount: content.Length,
            DateTimeOffset.UnixEpoch.AddMinutes(sequence));

    private sealed class SqliteFixture : IAsyncDisposable
    {
        private SqliteFixture(SqliteConnection connection, ComprexyDbContext context)
        {
            Connection = connection;
            Context = context;
        }

        public SqliteConnection Connection { get; }

        public ComprexyDbContext Context { get; }

        public static async Task<SqliteFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<ComprexyDbContext>()
                .UseSqlite(connection)
                .AddInterceptors(new ClusterIdSaveChangesInterceptor())
                .Options;
            var context = new ComprexyDbContext(options);
            await context.Database.EnsureCreatedAsync();
            return new SqliteFixture(connection, context);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }
}
