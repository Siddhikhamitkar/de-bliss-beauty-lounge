using DeBliss.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DeBliss.Infrastructure.Persistence.Configurations.Notifications;

public class AppointmentReminderConfiguration : IEntityTypeConfiguration<AppointmentReminder>
{
    public void Configure(EntityTypeBuilder<AppointmentReminder> builder)
    {
        builder.ToTable("AppointmentReminders");
        builder.HasKey(reminder => reminder.Id);

        builder.Property(reminder => reminder.ReminderType).HasConversion<string>().HasMaxLength(20);
        builder.Property(reminder => reminder.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(reminder => reminder.Channel).HasConversion<string>().HasMaxLength(20);

        builder.Property(reminder => reminder.ProviderMessageId).HasMaxLength(64);
        builder.Property(reminder => reminder.FailureReason).HasMaxLength(500);
        builder.Property(reminder => reminder.ClaimedBy).HasMaxLength(100);

        // Backs the dispatcher's due scan for pending rows.
        builder.HasIndex(reminder => new { reminder.Status, reminder.ScheduledAt });
        builder.HasIndex(reminder => reminder.CustomerId);

        // Idempotency: at most one pending row may exist for the same
        // appointment, reminder type and trigger time. Re-planning cancels the
        // previous pending rows first, and this index stops a concurrent
        // duplicate from surviving.
        builder.HasIndex(reminder => new { reminder.AppointmentId, reminder.ReminderType, reminder.ScheduledAt })
            .IsUnique()
            .HasFilter("[Status] = 'Pending'");

        // A provider message can never be recorded twice.
        builder.HasIndex(reminder => reminder.ProviderMessageId)
            .IsUnique()
            .HasFilter("[ProviderMessageId] IS NOT NULL");

        // Rows owned by an appointment go with it; appointments themselves are
        // soft-deleted, so this only matters for genuine cleanup.
        builder.HasOne(reminder => reminder.Appointment)
            .WithMany()
            .HasForeignKey(reminder => reminder.AppointmentId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict: reminder history stays readable even if customer records evolve.
        builder.HasOne(reminder => reminder.Customer)
            .WithMany()
            .HasForeignKey(reminder => reminder.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
