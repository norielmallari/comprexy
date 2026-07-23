using Comprexy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Comprexy.Infrastructure.Persistence;

/// <summary>
/// Assigns <see cref="EntityBase.ClusterId"/> for providers that cannot identity-generate a
/// non-PK bigint (SQLite). SQL Server should use IDENTITY instead and skip this path.
/// </summary>
public sealed class ClusterIdSaveChangesInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        AssignClusterIds(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        AssignClusterIds(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void AssignClusterIds(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        // SQL Server (and similar) generate ClusterId via IDENTITY; do not pre-assign.
        var provider = context.Database.ProviderName ?? string.Empty;
        if (provider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        AssignForClrType<Conversation>(context);
        AssignForClrType<ConversationMessage>(context);
        AssignForClrType<WorkingMemory>(context);
        AssignForClrType<CompressionEvent>(context);
    }

    private static void AssignForClrType<TEntity>(DbContext context)
        where TEntity : EntityBase
    {
        var pending = context.ChangeTracker
            .Entries<TEntity>()
            .Where(e => e.State == EntityState.Added && e.Entity.ClusterId == 0)
            .ToList();

        if (pending.Count == 0)
        {
            return;
        }

        var maxExisting = context.Set<TEntity>()
            .AsNoTracking()
            .Select(e => (long?)e.ClusterId)
            .Max() ?? 0;

        var next = maxExisting;
        foreach (var entry in pending)
        {
            entry.Property(e => e.ClusterId).CurrentValue = ++next;
        }
    }
}
