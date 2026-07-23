namespace Comprexy.Domain.Entities;

/// <summary>
/// Shared persistence identity: GUID primary key (app/FK identity) plus a sequential
/// <see cref="ClusterId"/> used as the SQL Server clustered index (not the PK).
/// </summary>
public abstract class EntityBase
{
    /// <summary>Nonclustered primary key. Assigned in factories; never use as SQL Server clustering key.</summary>
    public Guid Id { get; protected set; }

    /// <summary>
    /// Clustered sequential surrogate for SQL Server. Database-generated; remain 0 until SaveChanges.
    /// Not used as domain identity or FK target.
    /// </summary>
    public long ClusterId { get; protected set; }
}
