using Comprexy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comprexy.Infrastructure.Persistence.Configurations;

public class ConversationTurnMetricConfiguration : IEntityTypeConfiguration<ConversationTurnMetric>
{
    public void Configure(EntityTypeBuilder<ConversationTurnMetric> builder)
    {
        builder.ToTable("ConversationTurnMetrics");
        EntityBaseConfiguration.ConfigureKeys(builder);

        builder.Property(e => e.Model).HasMaxLength(256).IsRequired();
        builder.Property(e => e.RequestHash).HasMaxLength(64).IsRequired();
        builder.Property(e => e.SentPayloadHash).HasMaxLength(64).IsRequired();
        builder.Property(e => e.RequestStartedAt).IsRequired();
        builder.Property(e => e.CreatedAt).IsRequired();
        builder.Property(e => e.NetTokenSavingsRatio).IsRequired();

        builder.HasIndex(e => new { e.ConversationId, e.TurnIndex }).IsUnique();
        builder.HasIndex(e => new { e.ConversationId, e.CreatedAt });
    }
}
