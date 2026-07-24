namespace Comprexy.Domain.Entities;

/// <summary>
/// Shared persistence identity: sequential <see cref="ClusterId"/> (SQL Server clustering surrogate)
/// plus GUID <see cref="Id"/> primary key (app/FK identity).
/// </summary>
public abstract class EntityBase
{
    /// <summary>
    /// Clustered sequential surrogate for SQL Server. Database-generated; remain 0 until SaveChanges.
    /// Not used as domain identity or FK target. Stored as the first physical column.
    /// </summary>
    public long ClusterId { get; protected set; }

    /// <summary>Nonclustered primary key. Assigned in factories; never use as SQL Server clustering key.</summary>
    public Guid Id { get; protected set; }
}
