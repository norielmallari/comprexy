using Comprexy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comprexy.Infrastructure.Persistence.Configurations;

public class ConversationMetricsSummaryConfiguration : IEntityTypeConfiguration<ConversationMetricsSummary>
{
    public void Configure(EntityTypeBuilder<ConversationMetricsSummary> builder)
    {
        builder.ToTable("ConversationMetricsSummaries");
        EntityBaseConfiguration.ConfigureKeys(builder);

        builder.Property(e => e.CreatedAt).IsRequired();
        builder.Property(e => e.UpdatedAt).IsRequired();
        builder.Property(e => e.AverageTokenSavingsRatio).IsRequired();

        builder.HasIndex(e => e.ConversationId).IsUnique();
        builder.HasIndex(e => e.UpdatedAt);
    }
}
