using DeBliss.Domain.Customers;
using DeBliss.Domain.Notifications;
using DeBliss.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DeBliss.UnitTests.TestDoubles;

/// <summary>
/// Arranges reminder state past the application services: the default rules
/// every environment is seeded with, a customer's WhatsApp consent, and
/// reminder rows in any status so dispatcher tests can start mid-lifecycle.
/// </summary>
internal static class ReminderSeed
{
    private static readonly DateTimeOffset SeededAt = new(2026, 10, 5, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The five rules the migration seeds. A no-op when the rows are already
    /// there, so calling it is safe regardless of whether EnsureCreated
    /// applied the model's seed data.
    /// </summary>
    public static async Task DefaultRulesAsync(DeBlissDbContext context)
    {
        if (await context.ReminderRules.AnyAsync())
        {
            return;
        }

        context.ReminderRules.AddRange(
            Rule("5eed0000-0000-4000-8000-000000000001", ReminderType.Confirmation, 0),
            Rule("5eed0000-0000-4000-8000-000000000002", ReminderType.Reminder, 24 * 60),
            Rule("5eed0000-0000-4000-8000-000000000003", ReminderType.Reminder, 2 * 60),
            Rule("5eed0000-0000-4000-8000-000000000004", ReminderType.Cancellation, 0),
            Rule("5eed0000-0000-4000-8000-000000000005", ReminderType.Rescheduled, 0));

        await context.SaveChangesAsync();
    }

    public static async Task<Guid> SetOptInAsync(
        DeBlissDbContext context,
        Guid customerId,
        WhatsAppOptInStatus status,
        string source = "Front desk")
    {
        var customer = await context.Customers.FirstAsync(each => each.Id == customerId);

        customer.WhatsAppOptInStatus = status;
        customer.WhatsAppOptInSource = source;

        if (status == WhatsAppOptInStatus.OptedIn)
        {
            customer.WhatsAppOptInDate = SeededAt;
            customer.WhatsAppOptOutDate = null;
        }
        else if (status == WhatsAppOptInStatus.OptedOut)
        {
            customer.WhatsAppOptOutDate = SeededAt;
        }

        await context.SaveChangesAsync();

        return customerId;
    }

    public static async Task<Guid> ReminderAsync(
        DeBlissDbContext context,
        Guid appointmentId,
        Guid customerId,
        ReminderType reminderType,
        DateTimeOffset scheduledAt,
        ReminderStatus status = ReminderStatus.Pending,
        int attemptCount = 0,
        DateTimeOffset? sentAt = null,
        DateTimeOffset? lastAttemptAt = null,
        string? providerMessageId = null,
        string? failureReason = null,
        DateTimeOffset? nextAttemptAt = null,
        string? claimedBy = null,
        DateTimeOffset? claimedAt = null)
    {
        var reminder = new AppointmentReminder
        {
            Id = Guid.NewGuid(),
            AppointmentId = appointmentId,
            CustomerId = customerId,
            ReminderType = reminderType,
            Status = status,
            Channel = NotificationChannel.WhatsApp,
            ScheduledAt = scheduledAt,
            AttemptCount = attemptCount,
            SentAt = sentAt,
            LastAttemptAt = lastAttemptAt,
            ProviderMessageId = providerMessageId,
            FailureReason = failureReason,
            NextAttemptAt = nextAttemptAt,
            ClaimedBy = claimedBy,
            ClaimedAt = claimedAt
        };

        context.AppointmentReminders.Add(reminder);
        await context.SaveChangesAsync();

        return reminder.Id;
    }

    /// <summary>Replaces the whole rule set, e.g. to test a configuration with only one rule.</summary>
    public static async Task ReplaceRulesAsync(
        DeBlissDbContext context,
        params (ReminderType Type, int OffsetMinutes, bool IsEnabled)[] rules)
    {
        var existing = await context.ReminderRules.ToListAsync();
        context.ReminderRules.RemoveRange(existing);
        await context.SaveChangesAsync();

        var sortOrder = 0;

        context.ReminderRules.AddRange(rules.Select(rule =>
            Rule($"5eed0000-0000-4000-8000-0000000000{++sortOrder:D2}", rule.Type, rule.OffsetMinutes, rule.IsEnabled)));

        await context.SaveChangesAsync();
    }

    private static ReminderRule Rule(string id, ReminderType type, int offsetMinutes, bool isEnabled = true) => new()
    {
        Id = Guid.Parse(id),
        ReminderType = type,
        OffsetMinutes = offsetMinutes,
        IsEnabled = isEnabled,
        CreatedAt = SeededAt
    };
}
