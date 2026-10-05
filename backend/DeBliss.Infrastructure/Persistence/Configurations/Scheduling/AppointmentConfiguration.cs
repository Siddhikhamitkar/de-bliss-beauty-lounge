using DeBliss.Domain.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DeBliss.Infrastructure.Persistence.Configurations.Scheduling;

public class AppointmentConfiguration : IEntityTypeConfiguration<Appointment>
{
    public void Configure(EntityTypeBuilder<Appointment> builder)
    {
        builder.ToTable("Appointments");
        builder.HasKey(appointment => appointment.Id);

        builder.Property(appointment => appointment.WalkInCustomerName).HasMaxLength(200);
        builder.Property(appointment => appointment.WalkInCustomerPhone).HasMaxLength(30);
        builder.Property(appointment => appointment.Notes).HasMaxLength(2000);
        builder.Property(appointment => appointment.CancellationReason).HasMaxLength(500);

        builder.Property(appointment => appointment.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(appointment => appointment.Source).HasConversion<string>().HasMaxLength(20);

        // Backs the day / 3-day / week / month calendar range queries.
        builder.HasIndex(appointment => new { appointment.StartsAt, appointment.EndsAt });
        builder.HasIndex(appointment => new { appointment.CustomerId, appointment.StartsAt });
        builder.HasIndex(appointment => appointment.Status);
        builder.HasIndex(appointment => appointment.RecurringSeriesId);

        // Restrict, not Cascade: cancelled and no-show appointments are kept for
        // history and reporting, so nothing may delete them by accident.
        builder.HasOne(appointment => appointment.Customer)
            .WithMany(customer => customer.Appointments)
            .HasForeignKey(appointment => appointment.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(appointment => appointment.CreatedByUser)
            .WithMany()
            .HasForeignKey(appointment => appointment.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(appointment => appointment.RecurringSeries)
            .WithMany()
            .HasForeignKey(appointment => appointment.RecurringSeriesId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(appointment => appointment.BundlePriceSnapshot).HasPrecision(18, 2);

        // Restrict: a booking that was sold as a bundle stays resolvable even if the
        // bundle is later deactivated.
        builder.HasOne(appointment => appointment.ServiceBundle)
            .WithMany(bundle => bundle.Appointments)
            .HasForeignKey(appointment => appointment.ServiceBundleId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
