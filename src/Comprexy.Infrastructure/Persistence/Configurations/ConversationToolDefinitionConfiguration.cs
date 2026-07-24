using Comprexy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comprexy.Infrastructure.Persistence.Configurations;

public class ConversationToolDefinitionConfiguration : IEntityTypeConfiguration<ConversationToolDefinition>
{
    public void Configure(EntityTypeBuilder<ConversationToolDefinition> builder)
    {
        builder.ToTable("ConversationToolDefinitions");
        EntityBaseConfiguration.ConfigureKeys(builder);

        builder.Property(d => d.ToolName).IsRequired();
        builder.Property(d => d.DefinitionHash).IsRequired();
        builder.Property(d => d.DefinitionJson).IsRequired();
        builder.Property(d => d.HydratedAt);

        builder.HasIndex(d => new { d.ConversationId, d.ToolName }).IsUnique();
        builder.HasIndex(d => d.ConversationId);
    }
}
