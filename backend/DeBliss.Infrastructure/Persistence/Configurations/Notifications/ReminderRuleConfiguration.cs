using DeBliss.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DeBliss.Infrastructure.Persistence.Configurations.Notifications;

public class ReminderRuleConfiguration : IEntityTypeConfiguration<ReminderRule>
{
    /// <summary>Fixed timestamps keep the seed data deterministic across migrations.</summary>
    private static readonly DateTimeOffset SeededAt = new(2026, 10, 5, 0, 0, 0, TimeSpan.Zero);

    public void Configure(EntityTypeBuilder<ReminderRule> builder)
    {
        builder.ToTable("ReminderRules");
        builder.HasKey(rule => rule.Id);

        builder.Property(rule => rule.ReminderType).HasConversion<string>().HasMaxLength(20);

        // The same offset may not be configured twice for one notification type.
        builder.HasIndex(rule => new { rule.ReminderType, rule.OffsetMinutes }).IsUnique();

        // Defaults: an immediate confirmation, 24-hour and 2-hour reminders, and
        // immediate cancellation and reschedule notices. Seeded through the
        // migration so every environment starts with rules without hardcoding
        // timings in business logic.
        builder.HasData(
            new ReminderRule
            {
                Id = Guid.Parse("5eed0000-0000-4000-8000-000000000001"),
                ReminderType = ReminderType.Confirmation,
                OffsetMinutes = 0,
                IsEnabled = true,
                CreatedAt = SeededAt
            },
            new ReminderRule
            {
                Id = Guid.Parse("5eed0000-0000-4000-8000-000000000002"),
                ReminderType = ReminderType.Reminder,
                OffsetMinutes = 24 * 60,
                IsEnabled = true,
                CreatedAt = SeededAt
            },
            new ReminderRule
            {
                Id = Guid.Parse("5eed0000-0000-4000-8000-000000000003"),
                ReminderType = ReminderType.Reminder,
                OffsetMinutes = 2 * 60,
                IsEnabled = true,
                CreatedAt = SeededAt
            },
            new ReminderRule
            {
                Id = Guid.Parse("5eed0000-0000-4000-8000-000000000004"),
                ReminderType = ReminderType.Cancellation,
                OffsetMinutes = 0,
                IsEnabled = true,
                CreatedAt = SeededAt
            },
            new ReminderRule
            {
                Id = Guid.Parse("5eed0000-0000-4000-8000-000000000005"),
                ReminderType = ReminderType.Rescheduled,
                OffsetMinutes = 0,
                IsEnabled = true,
                CreatedAt = SeededAt
            });
    }
}
