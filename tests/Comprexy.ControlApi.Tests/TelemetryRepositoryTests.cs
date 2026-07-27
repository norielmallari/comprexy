using Comprexy.Domain.Entities;
using Comprexy.Infrastructure.Persistence;
using Comprexy.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Comprexy.ControlApi.Tests;

public sealed class TelemetryRepositoryTests
{
    [Fact]
    public async Task TurnRepository_ReturnsBoundedOrderedProjectionsAndHighestFinalTurn()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var conversation = Conversation.Create("repository-test", DateTimeOffset.UnixEpoch);
        fixture.Context.Conversations.Add(conversation);
        fixture.Context.ConversationTurnMetrics.AddRange(
            CreateTurn(conversation.Id, 3, "third-request-secret", "third-sent-secret"),
            CreateTurn(conversation.Id, 1, "first-request-secret", "first-sent-secret"),
            CreateTurn(conversation.Id, 2, "second-request-secret", "second-sent-secret"));
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        var repository = new EfConversationTurnMetricRepository(fixture.Context);

        var projections = await repository.ListBoundedProjectionsAsync(
            conversation.Id,
            2,
            CancellationToken.None);
        var final = await repository.GetFinalTurnProjectionAsync(
            conversation.Id,
            CancellationToken.None);

        Assert.Equal([1, 2], projections.Select(x => x.TurnIndex));
        Assert.Equal(3, final?.TurnIndex);
        Assert.All(projections, projection => Assert.Equal("model", projection.Model));
        Assert.Empty(fixture.Context.ChangeTracker.Entries<ConversationTurnMetric>());
    }

    [Fact]
    public async Task TurnRepository_RejectsInvalidTakeAndHonorsCancellation()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var repository = new EfConversationTurnMetricRepository(fixture.Context);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            repository.ListBoundedProjectionsAsync(Guid.NewGuid(), 0, CancellationToken.None));

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            repository.ListBoundedProjectionsAsync(Guid.NewGuid(), 1, cancellation.Token));
    }

    [Fact]
    public async Task TurnRepository_SavingsAggregatesCoverWholeConversationBeyondBoundedTake()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var conversation = Conversation.Create("aggregate-test", DateTimeOffset.UnixEpoch);
        var otherConversation = Conversation.Create("aggregate-other", DateTimeOffset.UnixEpoch);
        fixture.Context.Conversations.AddRange(conversation, otherConversation);
        var turns = new[]
        {
            CreateTurn(conversation.Id, 1, "r1", "s1", actualPromptTokens: 170),
            CreateTurn(conversation.Id, 2, "r2", "s2", actualPromptTokens: 150),
            CreateTurn(conversation.Id, 3, "r3", "s3", actualPromptTokens: 110),
            CreateTurn(conversation.Id, 4, "r4", "s4", actualPromptTokens: 70),
            CreateTurn(conversation.Id, 5, "r5", "s5", actualPromptTokens: 30)
        };
        fixture.Context.ConversationTurnMetrics.AddRange(turns);
        fixture.Context.ConversationTurnMetrics.Add(
            CreateTurn(otherConversation.Id, 1, "other-r", "other-s", actualPromptTokens: 0));
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        var repository = new EfConversationTurnMetricRepository(fixture.Context);

        var bounded = await repository.ListBoundedProjectionsAsync(
            conversation.Id,
            2,
            CancellationToken.None);
        var aggregates = await repository.GetSavingsAggregatesAsync(
            conversation.Id,
            CancellationToken.None);

        Assert.Equal(2, bounded.Count);
        Assert.NotNull(aggregates);
        Assert.Equal(5, aggregates.TurnCount);
        Assert.Equal(turns.Max(x => x.NetTokenSavingsRatio), aggregates.PeakNetTokenSavingsRatio);
        Assert.Equal(
            turns.Average(x => x.NetTokenSavingsRatio),
            aggregates.SimpleAverageNetTokenSavingsRatio,
            precision: 6);
        Assert.True(aggregates.PeakNetTokenSavingsRatio > bounded.Max(x => x.NetTokenSavingsRatio));
        Assert.Empty(fixture.Context.ChangeTracker.Entries<ConversationTurnMetric>());
    }

    [Fact]
    public async Task ConversationRepository_ExistsAsyncFindsOnlyMatchingRowsWithoutTracking()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var conversation = Conversation.Create("exists-test", DateTimeOffset.UnixEpoch);
        fixture.Context.Conversations.Add(conversation);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        var repository = new EfConversationRepository(fixture.Context);

        var exists = await repository.ExistsAsync(conversation.Id, CancellationToken.None);
        var missing = await repository.ExistsAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.True(exists);
        Assert.False(missing);
        Assert.Empty(fixture.Context.ChangeTracker.Entries<Conversation>());
    }

    [Fact]
    public async Task SummaryRollup_IsNoTrackingWhileTrackedWritePathStillPersists()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UnixEpoch;
        var summary = ConversationMetricsSummary.Create(id, now);
        summary.ApplyTurn(CreateTurn(id, 1, "request", "sent"), now);
        fixture.Context.ConversationMetricsSummaries.Add(summary);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        var repository = new EfConversationMetricsSummaryRepository(fixture.Context);

        var rollup = await repository.GetRollupAsync(id, CancellationToken.None);

        Assert.NotNull(rollup);
        Assert.Equal(1, rollup.TotalTurns);
        Assert.Empty(fixture.Context.ChangeTracker.Entries<ConversationMetricsSummary>());

        var tracked = await repository.FindByConversationIdAsync(id, CancellationToken.None);
        Assert.NotNull(tracked);
        tracked.ApplyCompressionOverhead(25, now.AddMinutes(1));
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        var updated = await repository.GetRollupAsync(id, CancellationToken.None);
        Assert.Equal(25, updated?.TotalCompressionOverheadTokens);
        Assert.Equal(1, updated?.CompressionEventCount);
    }

    private static ConversationTurnMetric CreateTurn(
        Guid conversationId,
        int turnIndex,
        string requestHash,
        string sentHash,
        int actualPromptTokens = 90) =>
        ConversationTurnMetric.Create(
            conversationId,
            turnIndex,
            DateTimeOffset.UnixEpoch.AddMinutes(turnIndex),
            "model",
            rawInputTokensEstimated: 200,
            compressedInputTokensEstimated: 100,
            actualPromptTokens,
            actualCompletionTokens: 10,
            softBudgetExceeded: false,
            hardBudgetExceeded: false,
            trimTriggered: false,
            workingMemoryVersionUsed: 1,
            rawMessageCount: 10,
            sentMessageCount: 5,
            requestHash,
            sentHash,
            DateTimeOffset.UnixEpoch.AddMinutes(turnIndex));

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
