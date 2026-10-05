using DeBliss.Domain.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DeBliss.Infrastructure.Persistence.Configurations.Services;

public class ServiceBundleConfiguration : IEntityTypeConfiguration<ServiceBundle>
{
    public void Configure(EntityTypeBuilder<ServiceBundle> builder)
    {
        builder.ToTable("ServiceBundles");
        builder.HasKey(bundle => bundle.Id);

        builder.Property(bundle => bundle.Name).HasMaxLength(150).IsRequired();
        builder.Property(bundle => bundle.Description).HasMaxLength(1000);

        // Money is always decimal with a fixed scale, never a floating point type.
        builder.Property(bundle => bundle.Price).HasPrecision(18, 2);

        builder.HasIndex(bundle => new { bundle.IsActive, bundle.ServiceCategoryId });
        builder.HasIndex(bundle => bundle.ServiceCategoryId);

        builder.HasOne(bundle => bundle.ServiceCategory)
            .WithMany(category => category.Bundles)
            .HasForeignKey(bundle => bundle.ServiceCategoryId)
            .OnDelete(DeleteBehavior.SetNull);

        // Restrict, like Service: bundles are deactivated rather than deleted, and a
        // booking that was sold as a bundle must stay resolvable forever.
        builder.HasMany(bundle => bundle.Appointments)
            .WithOne(appointment => appointment.ServiceBundle)
            .HasForeignKey(appointment => appointment.ServiceBundleId)
            .OnDelete(DeleteBehavior.Restrict);

        // Restrict: a paid invoice must never be rewritten because the bundle changed.
        builder.HasMany(bundle => bundle.Invoices)
            .WithOne(invoice => invoice.ServiceBundle)
            .HasForeignKey(invoice => invoice.ServiceBundleId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
