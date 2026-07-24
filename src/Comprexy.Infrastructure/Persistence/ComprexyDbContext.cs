using Comprexy.Domain.Entities;
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

    public DbSet<ConversationToolCatalog> ConversationToolCatalogs => Set<ConversationToolCatalog>();

    public DbSet<ConversationToolDefinition> ConversationToolDefinitions => Set<ConversationToolDefinition>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Ignore<EntityBase>();
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ComprexyDbContext).Assembly);
    }
}
