using Comprexy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comprexy.Infrastructure.Persistence.Configurations;

public class WorkingMemoryConfiguration : IEntityTypeConfiguration<WorkingMemory>
{
    public void Configure(EntityTypeBuilder<WorkingMemory> builder)
    {
        builder.ToTable("WorkingMemories");
        builder.HasKey(w => w.Id);

        builder.Property(w => w.Content).IsRequired();
        builder.Property(w => w.TokenCount).IsRequired();
        builder.Property(w => w.CreatedAt).IsRequired();

        builder.HasIndex(w => new { w.ConversationId, w.Version }).IsUnique();
    }
}
