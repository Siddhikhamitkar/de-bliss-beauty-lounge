using DeBliss.Domain.Loyalty;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DeBliss.Infrastructure.Persistence.Configurations.Loyalty;

public sealed class LoyaltySettingsConfiguration : IEntityTypeConfiguration<LoyaltySettings>
{
    public void Configure(EntityTypeBuilder<LoyaltySettings> builder)
    {
        builder.ToTable("LoyaltySettings", table =>
        {
            // SQLite stores decimals as text; casts keep checks numeric on both providers.
            table.HasCheckConstraint("CK_LoyaltySettings_EarningPercentage", "CAST([EarningPercentage] AS decimal(5,2)) BETWEEN 0 AND 100");
            table.HasCheckConstraint("CK_LoyaltySettings_PointValueAed", "CAST([PointValueAed] AS decimal(18,4)) BETWEEN 0.0001 AND 1000");
            table.HasCheckConstraint("CK_LoyaltySettings_MinimumRedemptionPoints", "CAST([MinimumRedemptionPoints] AS decimal(18,2)) BETWEEN 0 AND 1000000000");
            table.HasCheckConstraint("CK_LoyaltySettings_MaximumRedemptionPercentage", "CAST([MaximumRedemptionPercentage] AS decimal(5,2)) BETWEEN 0 AND 100");
            table.HasCheckConstraint("CK_LoyaltySettings_MinimumInvoiceAmount", "[MinimumInvoiceAmount] IS NULL OR CAST([MinimumInvoiceAmount] AS decimal(18,2)) BETWEEN 0 AND 1000000000");
        });
        builder.HasKey(settings => settings.Id);
        builder.Property(settings => settings.Id).ValueGeneratedNever();
        builder.Property(settings => settings.EarningPercentage).HasPrecision(5, 2);
        builder.Property(settings => settings.PointValueAed).HasPrecision(18, 4);
        builder.Property(settings => settings.MinimumRedemptionPoints).HasPrecision(18, 2);
        builder.Property(settings => settings.MaximumRedemptionPercentage).HasPrecision(5, 2);
        builder.Property(settings => settings.MinimumInvoiceAmount).HasPrecision(18, 2);
        builder.Property(settings => settings.Version).IsConcurrencyToken();
        builder.HasOne(settings => settings.UpdatedByUser).WithMany()
            .HasForeignKey(settings => settings.UpdatedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
