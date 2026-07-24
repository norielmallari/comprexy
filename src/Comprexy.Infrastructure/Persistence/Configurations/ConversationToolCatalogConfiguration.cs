using Comprexy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comprexy.Infrastructure.Persistence.Configurations;

public class ConversationToolCatalogConfiguration : IEntityTypeConfiguration<ConversationToolCatalog>
{
    public void Configure(EntityTypeBuilder<ConversationToolCatalog> builder)
    {
        builder.ToTable("ConversationToolCatalogs");
        EntityBaseConfiguration.ConfigureKeys(builder);

        builder.Property(c => c.CatalogHash).IsRequired();
        builder.Property(c => c.CompactIndexJson).IsRequired();
        builder.Property(c => c.SnapshottedAt).IsRequired();

        builder.HasIndex(c => c.ConversationId).IsUnique();
    }
}
