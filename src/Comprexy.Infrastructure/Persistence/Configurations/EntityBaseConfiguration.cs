using Comprexy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comprexy.Infrastructure.Persistence.Configurations;

/// <summary>
/// Shared key layout for <see cref="EntityBase"/>: sequential <see cref="EntityBase.ClusterId"/>
/// as column 0 (unique index; clustered on SQL Server when that provider is used), GUID
/// <see cref="EntityBase.Id"/> as column 1 (PK, NCI on SQL Server).
/// </summary>
public static class EntityBaseConfiguration
{
    public const int ClusterIdColumnOrder = 0;
    public const int IdColumnOrder = 1;

    public static void ConfigureKeys<TEntity>(EntityTypeBuilder<TEntity> builder)
        where TEntity : EntityBase
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.ClusterId)
            .HasColumnOrder(ClusterIdColumnOrder)
            .IsRequired();

        builder.Property(e => e.Id)
            .HasColumnOrder(IdColumnOrder)
            .ValueGeneratedNever();

        // Unique sequential surrogate. SQLite: ClusterIdSaveChangesInterceptor assigns values.
        // SQL Server (later): mark this index clustered + UseIdentityColumn on the property.
        builder.HasIndex(e => e.ClusterId).IsUnique();
    }
}
