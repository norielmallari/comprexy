using Comprexy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comprexy.Infrastructure.Persistence.Configurations;

public class ConversationConfiguration : IEntityTypeConfiguration<Conversation>
{
    public void Configure(EntityTypeBuilder<Conversation> builder)
    {
        builder.ToTable("Conversations");
        EntityBaseConfiguration.ConfigureKeys(builder);

        builder.Property(c => c.ConversationKey).IsRequired();
        builder.HasIndex(c => c.ConversationKey).IsUnique();

        builder.Property(c => c.SystemPrompt);
        builder.Property(c => c.SyncedMessageCount).IsRequired();
        builder.Property(c => c.CreatedAt).IsRequired();
        builder.Property(c => c.UpdatedAt).IsRequired();
    }
}
