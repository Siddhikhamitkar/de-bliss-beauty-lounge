using DeBliss.Domain.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DeBliss.Infrastructure.Persistence.Configurations.Services;

public class ServiceBundleItemConfiguration : IEntityTypeConfiguration<ServiceBundleItem>
{
    public void Configure(EntityTypeBuilder<ServiceBundleItem> builder)
    {
        builder.ToTable("ServiceBundleItems");
        builder.HasKey(item => item.Id);

        // V1 forbids the same service twice in one bundle; the unique index backs
        // the rule at the schema level.
        builder.HasIndex(item => new { item.ServiceBundleId, item.ServiceId }).IsUnique();
        builder.HasIndex(item => item.ServiceId);

        builder.HasOne(item => item.ServiceBundle)
            .WithMany(bundle => bundle.Items)
            .HasForeignKey(item => item.ServiceBundleId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict: a component that has ever been sold in a bundle must stay
        // resolvable, exactly like a booked service.
        builder.HasOne(item => item.Service)
            .WithMany(service => service.BundleItems)
            .HasForeignKey(item => item.ServiceId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
