using Comprexy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comprexy.Infrastructure.Persistence.Configurations;

public class ConversationMessageConfiguration : IEntityTypeConfiguration<ConversationMessage>
{
    public void Configure(EntityTypeBuilder<ConversationMessage> builder)
    {
        builder.ToTable("ConversationMessages");
        EntityBaseConfiguration.ConfigureKeys(builder);

        builder.Property(m => m.Role).HasConversion<string>().IsRequired();
        builder.Property(m => m.Content).IsRequired();
        builder.Property(m => m.RawWireJson);
        builder.Property(m => m.TokenCount).IsRequired();
        builder.Property(m => m.CreatedAt).IsRequired();

        builder.HasIndex(m => new { m.ConversationId, m.Sequence }).IsUnique();
        builder.HasIndex(m => new { m.ConversationId, m.FoldedIntoWorkingMemoryVersion });
    }
}
