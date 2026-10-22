using DeBliss.Domain.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DeBliss.Infrastructure.Persistence.Configurations.Settings;

public class SalonSettingsConfiguration : IEntityTypeConfiguration<SalonSettings>
{
    public void Configure(EntityTypeBuilder<SalonSettings> builder)
    {
        builder.ToTable("SalonSettings");
        builder.HasKey(settings => settings.Id);

        // A percentage with two decimals: 5.00, never a fraction of one.
        builder.Property(settings => settings.TaxRate).HasPrecision(5, 2);
    }
}
