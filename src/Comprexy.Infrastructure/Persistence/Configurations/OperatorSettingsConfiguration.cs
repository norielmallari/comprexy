using Comprexy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comprexy.Infrastructure.Persistence.Configurations;

public class OperatorSettingsConfiguration : IEntityTypeConfiguration<OperatorSettings>
{
    public void Configure(EntityTypeBuilder<OperatorSettings> builder)
    {
        builder.ToTable("OperatorSettings");
        EntityBaseConfiguration.ConfigureKeys(builder);

        builder.Property(e => e.Revision).IsRequired();
        builder.Property(e => e.SettingsJson).IsRequired();
        builder.Property(e => e.UpdatedAt).IsRequired();
    }
}
