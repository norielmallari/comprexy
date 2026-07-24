using Comprexy.Domain.Entities;
using Comprexy.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;

namespace Comprexy.Infrastructure.Persistence;

public class ComprexyDbContext : DbContext
{
    public ComprexyDbContext(DbContextOptions<ComprexyDbContext> options) : base(options)
    {
    }

    public DbSet<Conversation> Conversations => Set<Conversation>();

    public DbSet<ConversationMessage> ConversationMessages => Set<ConversationMessage>();

    public DbSet<WorkingMemory> WorkingMemories => Set<WorkingMemory>();

    public DbSet<CompressionEvent> CompressionEvents => Set<CompressionEvent>();

    public DbSet<ConversationTurnMetric> ConversationTurnMetrics => Set<ConversationTurnMetric>();

    public DbSet<ConversationMetricsSummary> ConversationMetricsSummaries => Set<ConversationMetricsSummary>();

    public DbSet<ConversationToolCatalog> ConversationToolCatalogs => Set<ConversationToolCatalog>();

    public DbSet<ConversationToolDefinition> ConversationToolDefinitions => Set<ConversationToolDefinition>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder
            .Properties<DateTimeOffset>()
            .HaveConversion<DateTimeOffsetToUtcTicksConverter>();

        configurationBuilder
            .Properties<DateTimeOffset?>()
            .HaveConversion<NullableDateTimeOffsetToUtcTicksConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Ignore<EntityBase>();
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ComprexyDbContext).Assembly);
    }
}
