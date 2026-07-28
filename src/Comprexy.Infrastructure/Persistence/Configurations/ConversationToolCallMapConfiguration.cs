using Comprexy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comprexy.Infrastructure.Persistence.Configurations;

public class ConversationToolCallMapConfiguration : IEntityTypeConfiguration<ConversationToolCallMap>
{
    public void Configure(EntityTypeBuilder<ConversationToolCallMap> builder)
    {
        builder.ToTable("ConversationToolCallMaps");
        EntityBaseConfiguration.ConfigureKeys(builder);

        builder.Property(m => m.IrCallId).IsRequired();
        builder.Property(m => m.ClientCallId).IsRequired();
        builder.Property(m => m.ComprexyToolName).IsRequired();
        builder.Property(m => m.IrArgumentsJson).IsRequired();
        builder.Property(m => m.Strategy).IsRequired();
        builder.Property(m => m.Pending).IsRequired().HasDefaultValue(true);
        builder.Property(m => m.RegisteredAt).IsRequired();

        builder.HasIndex(m => new { m.ConversationId, m.ClientCallId }).IsUnique();
        builder.HasIndex(m => new { m.ConversationId, m.IrCallId }).IsUnique();
        builder.HasIndex(m => new { m.ConversationId, m.Pending, m.RegisteredAt });
    }
}
