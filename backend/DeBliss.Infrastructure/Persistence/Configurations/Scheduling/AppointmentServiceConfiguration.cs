using DeBliss.Domain.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DeBliss.Infrastructure.Persistence.Configurations.Scheduling;

public class AppointmentServiceConfiguration : IEntityTypeConfiguration<AppointmentService>
{
    public void Configure(EntityTypeBuilder<AppointmentService> builder)
    {
        builder.ToTable("AppointmentServices");
        builder.HasKey(appointmentService => appointmentService.Id);

        builder.Property(appointmentService => appointmentService.ServiceNameSnapshot)
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(appointmentService => appointmentService.Notes).HasMaxLength(2000);

        // Price and duration are snapshotted at booking time so that later edits to
        // the service catalogue never rewrite what the customer actually booked.
        builder.Property(appointmentService => appointmentService.UnitPrice).HasPrecision(18, 2);

        builder.HasIndex(appointmentService => new
        {
            appointmentService.AppointmentId,
            appointmentService.SortOrder,
        });
        builder.HasIndex(appointmentService => new
        {
            appointmentService.StaffId,
            appointmentService.AppointmentId,
        });

        builder.HasOne(appointmentService => appointmentService.Appointment)
            .WithMany(appointment => appointment.Services)
            .HasForeignKey(appointmentService => appointmentService.AppointmentId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict on both of these so that retiring a service or a staff member
        // can never delete a booked line.
        builder.HasOne(appointmentService => appointmentService.Service)
            .WithMany(service => service.AppointmentServices)
            .HasForeignKey(appointmentService => appointmentService.ServiceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(appointmentService => appointmentService.Staff)
            .WithMany(staff => staff.AppointmentServices)
            .HasForeignKey(appointmentService => appointmentService.StaffId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
