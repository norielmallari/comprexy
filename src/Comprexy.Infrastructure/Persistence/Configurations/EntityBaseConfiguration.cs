using Comprexy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comprexy.Infrastructure.Persistence.Configurations;

/// <summary>
/// Shared key layout for <see cref="EntityBase"/>: GUID PK (NCI on SQL Server) + sequential
/// <see cref="EntityBase.ClusterId"/> unique index (clustered on SQL Server when that provider is used).
/// </summary>
public static class EntityBaseConfiguration
{
    public static void ConfigureKeys<TEntity>(EntityTypeBuilder<TEntity> builder)
        where TEntity : EntityBase
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.ClusterId).IsRequired();

        // Unique sequential surrogate. SQLite: ClusterIdSaveChangesInterceptor assigns values.
        // SQL Server (later): mark this index clustered + UseIdentityColumn on the property.
        builder.HasIndex(e => e.ClusterId).IsUnique();
    }
}
