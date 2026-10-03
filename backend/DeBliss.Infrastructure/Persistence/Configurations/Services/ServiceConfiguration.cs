using DeBliss.Domain.Scheduling;
using DeBliss.Domain.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DeBliss.Infrastructure.Persistence.Configurations.Services;

public class ServiceConfiguration : IEntityTypeConfiguration<Service>
{
    public void Configure(EntityTypeBuilder<Service> builder)
    {
        builder.ToTable("Services");
        builder.HasKey(service => service.Id);

        builder.Property(service => service.Name).HasMaxLength(150).IsRequired();
        builder.Property(service => service.Description).HasMaxLength(1000);

        // Money is always decimal with a fixed scale, never a floating point type.
        builder.Property(service => service.Price).HasPrecision(18, 2);

        // IsOnlineBookable is what the future customer-facing website filters on.
        builder.HasIndex(service => new { service.IsActive, service.IsOnlineBookable });
        builder.HasIndex(service => service.ServiceCategoryId);

        builder.HasOne(service => service.ServiceCategory)
            .WithMany(category => category.Services)
            .HasForeignKey(service => service.ServiceCategoryId)
            .OnDelete(DeleteBehavior.SetNull);

        // Restrict: a service that has ever been booked must not be removable,
        // because appointment rows snapshot it and must stay resolvable.
        builder.HasMany(service => service.AppointmentServices)
            .WithOne(appointmentService => appointmentService.Service)
            .HasForeignKey(appointmentService => appointmentService.ServiceId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
