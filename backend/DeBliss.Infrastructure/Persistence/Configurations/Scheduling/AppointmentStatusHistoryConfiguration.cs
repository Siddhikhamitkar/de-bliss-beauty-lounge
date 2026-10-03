using DeBliss.Domain.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DeBliss.Infrastructure.Persistence.Configurations.Scheduling;

public class AppointmentStatusHistoryConfiguration : IEntityTypeConfiguration<AppointmentStatusHistory>
{
    public void Configure(EntityTypeBuilder<AppointmentStatusHistory> builder)
    {
        builder.ToTable("AppointmentStatusHistory");
        builder.HasKey(history => history.Id);

        builder.Property(history => history.FromStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(history => history.ToStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(history => history.Reason).HasMaxLength(500);

        // Append-only audit trail, always read newest-first for one appointment.
        builder.HasIndex(history => new { history.AppointmentId, history.ChangedAt });

        builder.HasOne(history => history.Appointment)
            .WithMany(appointment => appointment.StatusHistory)
            .HasForeignKey(history => history.AppointmentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(history => history.ChangedByUser)
            .WithMany()
            .HasForeignKey(history => history.ChangedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
