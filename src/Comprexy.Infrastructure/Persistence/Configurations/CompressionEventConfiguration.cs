using Comprexy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comprexy.Infrastructure.Persistence.Configurations;

public class CompressionEventConfiguration : IEntityTypeConfiguration<CompressionEvent>
{
    public void Configure(EntityTypeBuilder<CompressionEvent> builder)
    {
        builder.ToTable("CompressionEvents");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Mode).HasConversion<string>().IsRequired();
        builder.Property(e => e.Status).HasConversion<string>().IsRequired();
        builder.Property(e => e.CreatedAt).IsRequired();

        builder.HasIndex(e => e.ConversationId);
        builder.Ignore(e => e.CompressionRatio);
    }
}
