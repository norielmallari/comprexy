using Comprexy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comprexy.Infrastructure.Persistence.Configurations;

public class ModelPricingEntryConfiguration : IEntityTypeConfiguration<ModelPricingEntry>
{
    public void Configure(EntityTypeBuilder<ModelPricingEntry> builder)
    {
        builder.ToTable("ModelPricingEntries");
        EntityBaseConfiguration.ConfigureKeys(builder);

        builder.Property(e => e.ModelKey).IsRequired().HasMaxLength(128);
        builder.HasIndex(e => e.ModelKey).IsUnique();

        builder.Property(e => e.DisplayLabel).IsRequired().HasMaxLength(256);

        builder.Property(e => e.CurrencyCode)
            .IsRequired()
            .HasMaxLength(16)
            .HasDefaultValue("USD");

        builder.Property(e => e.InputUsdPer1M).IsRequired().HasPrecision(18, 6);
        builder.Property(e => e.OutputUsdPer1M).IsRequired().HasPrecision(18, 6);
        builder.Property(e => e.CachedInputUsdPer1M).HasPrecision(18, 6);
        builder.Property(e => e.CachedOutputUsdPer1M).HasPrecision(18, 6);

        builder.Property(e => e.SortOrder).IsRequired();
        builder.Property(e => e.IsActive).IsRequired();

        builder.HasIndex(e => new { e.IsActive, e.SortOrder });
    }
}
