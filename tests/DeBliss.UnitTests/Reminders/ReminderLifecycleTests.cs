using DeBliss.Application.Notifications;
using DeBliss.Application.Notifications.Dtos;
using DeBliss.Application.Scheduling.Dtos;
using DeBliss.Domain.Notifications;
using DeBliss.Domain.Scheduling;
using DeBliss.Infrastructure.Persistence;
using DeBliss.UnitTests.TestDoubles;
using Microsoft.EntityFrameworkCore;

namespace DeBliss.UnitTests.Reminders;

/// <summary>
/// The reminder lifecycle: which rows a booking, a move, a cancellation, a
/// completion and a no-show leave behind. Every case drives the real booking
/// service, so the planner hooks are exercised exactly as the API invokes
/// them.
/// </summary>
public class ReminderLifecycleTests
{
    private static readonly DateTimeOffset Saturday =
        new(2026, 10, 3, 9, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset At(int dayOffset, int hour, int minute = 0) =>
        Saturday.AddDays(dayOffset).AddHours(hour - 9).AddMinutes(minute);

    private sealed record Fixture(
        RemindersHarness Harness,
        Guid CustomerId,
        Guid StaffId,
        Guid ServiceId,
        Guid CategoryId);

    private static async Task<Fixture> ArrangeAsync()
    {
        var harness = new RemindersHarness();
        var customerId = await harness.SeedCustomerAsync();
        var categoryId = await harness.SeedCategoryAsync("Hair");
        var staffId = await harness.SeedStaffAsync("Jasmin", "Rae");
        await harness.SeedWorkingHoursAsync(staffId, DayOfWeek.Monday, new TimeSpan(8, 0, 0), new TimeSpan(17, 0, 0));
        var serviceId = await harness.SeedServiceAsync(categoryId, "Haircut", staffIds: [staffId]);

        return new Fixture(harness, customerId, staffId, serviceId, categoryId);
    }

    private static SaveAppointmentRequest Request(
        Guid? customerId,
        DateTimeOffset? startsAt = null,
        IReadOnlyList<AppointmentServiceLineInput>? services = null,
        string? walkInCustomerName = null,
        Guid? serviceBundleId = null) =>
        new(customerId, walkInCustomerName, null, startsAt ?? Monday10, services ?? [], null, serviceBundleId);

    private static readonly DateTimeOffset Monday10 = At(2, 10);

    private static async Task<List<AppointmentReminder>> RowsAsync(RemindersHarness harness, Guid appointmentId)
    {
        await using var context = harness.Reopen();

        var rows = await context.AppointmentReminders
            .Where(reminder => reminder.AppointmentId == appointmentId)
            .ToListAsync();

        return rows.OrderBy(reminder => reminder.ScheduledAt).ThenBy(reminder => reminder.ReminderType).ToList();
    }

    [Fact]
    public async Task Booking_plans_a_confirmation_and_both_default_timed_reminders()
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;

        var created = await fixture.Harness.Appointments.CreateAsync(
            Request(fixture.CustomerId, services: [new(fixture.ServiceId, fixture.StaffId)]));

        var rows = await RowsAsync(fixture.Harness, created.Id);

        Assert.Equal(3, rows.Count);

        var confirmation = rows.Single(row => row.ReminderType == ReminderType.Confirmation);
        Assert.Equal(ReminderStatus.Pending, confirmation.Status);
        Assert.Equal(NotificationChannel.WhatsApp, confirmation.Channel);
        Assert.Equal(Saturday, confirmation.ScheduledAt);

        var timed = rows.Where(row => row.ReminderType == ReminderType.Reminder).ToList();
        Assert.Equal(At(1, 10), timed[0].ScheduledAt);  // 24 hours before Monday 10:00
        Assert.Equal(At(2, 8), timed[1].ScheduledAt);   // 2 hours before Monday 10:00
        Assert.All(timed, row => Assert.Equal(ReminderStatus.Pending, row.Status));
        Assert.All(rows, row => Assert.Equal(fixture.CustomerId, row.CustomerId));
    }

    [Fact]
    public async Task Disabled_and_custom_rules_shape_what_a_booking_plans()
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;

        await fixture.Harness.ReplaceRulesAsync(
            (ReminderType.Confirmation, 0, false),
            (ReminderType.Reminder, 60, true),
            (ReminderType.Reminder, 1440, false));

        var created = await fixture.Harness.Appointments.CreateAsync(
            Request(fixture.CustomerId, services: [new(fixture.ServiceId, fixture.StaffId)]));

        var rows = await RowsAsync(fixture.Harness, created.Id);

        var row = Assert.Single(rows);
        Assert.Equal(ReminderType.Reminder, row.ReminderType);
        Assert.Equal(At(2, 9), row.ScheduledAt); // 60 minutes before Monday 10:00
    }

    [Fact]
    public async Task Disabling_a_rule_through_the_settings_service_stops_new_plans()
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;

        var rules = await fixture.Harness.Settings.GetRulesAsync();
        var twoHourRule = rules.Single(rule => rule.ReminderType == nameof(ReminderType.Reminder) && rule.OffsetMinutes == 120);

        await fixture.Harness.Settings.UpdateRuleAsync(twoHourRule.Id, new UpdateReminderRuleRequest(false, null));

        var created = await fixture.Harness.Appointments.CreateAsync(
            Request(fixture.CustomerId, services: [new(fixture.ServiceId, fixture.StaffId)]));

        var rows = await RowsAsync(fixture.Harness, created.Id);

        Assert.Equal(2, rows.Count);
        Assert.DoesNotContain(rows, row => row.ReminderType == ReminderType.Reminder && row.ScheduledAt == At(2, 8));
    }

    [Fact]
    public async Task A_booking_inside_the_reminder_window_skips_triggers_that_already_passed()
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;

        // Monday 07:00, booking 08:30 — both timed triggers (Sunday 08:30 and
        // Monday 06:30) are already in the past.
        fixture.Harness.Clock.Advance(TimeSpan.FromHours(46));

        var created = await fixture.Harness.Appointments.CreateAsync(
            Request(fixture.CustomerId, startsAt: At(2, 8, 30),
                services: [new(fixture.ServiceId, fixture.StaffId)]));

        var rows = await RowsAsync(fixture.Harness, created.Id);

        var row = Assert.Single(rows);
        Assert.Equal(ReminderType.Confirmation, row.ReminderType);
        Assert.Equal(At(2, 7), row.ScheduledAt);
    }

    [Fact]
    public async Task A_walk_in_booking_plans_nothing_and_gaining_a_customer_plans_the_normal_set()
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;

        var created = await fixture.Harness.Appointments.CreateAsync(
            Request(null, walkInCustomerName: "Maya", services: [new(fixture.ServiceId, fixture.StaffId)]));

        Assert.Empty(await RowsAsync(fixture.Harness, created.Id));

        await fixture.Harness.Appointments.UpdateAsync(
            created.Id,
            Request(fixture.CustomerId, services: [new(fixture.ServiceId, fixture.StaffId)]));

        var rows = await RowsAsync(fixture.Harness, created.Id);

        Assert.Equal(3, rows.Count);
        Assert.Contains(rows, row => row.ReminderType == ReminderType.Confirmation);
        Assert.Equal(2, rows.Count(row => row.ReminderType == ReminderType.Reminder));
        Assert.All(rows, row => Assert.Equal(fixture.CustomerId, row.CustomerId));
    }

    [Fact]
    public async Task Cancelling_drops_pending_rows_and_queues_the_cancellation_notice()
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;

        var created = await fixture.Harness.Appointments.CreateAsync(
            Request(fixture.CustomerId, services: [new(fixture.ServiceId, fixture.StaffId)]));

        await fixture.Harness.Appointments.SetStatusAsync(
            created.Id, new SetAppointmentStatusRequest("Cancelled", "Customer called in sick"));

        var rows = await RowsAsync(fixture.Harness, created.Id);

        Assert.Equal(4, rows.Count);

        var cancelled = rows.Where(row => row.Status == ReminderStatus.Cancelled).ToList();
        Assert.Equal(3, cancelled.Count);
        Assert.All(cancelled, row =>
        {
            Assert.Equal(ReminderFailureReasons.AppointmentCancelled, row.FailureReason);
            Assert.DoesNotContain(row.ReminderType, new[] { ReminderType.Cancellation });
        });

        var notice = rows.Single(row => row.ReminderType == ReminderType.Cancellation);
        Assert.Equal(ReminderStatus.Pending, notice.Status);
        Assert.Equal(Saturday, notice.ScheduledAt);
    }

    [Fact]
    public async Task Completing_drops_pending_rows_without_a_notice()
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;

        var created = await fixture.Harness.Appointments.CreateAsync(
            Request(fixture.CustomerId, services: [new(fixture.ServiceId, fixture.StaffId)]));

        await fixture.Harness.Appointments.SetStatusAsync(created.Id, new SetAppointmentStatusRequest("CheckedIn", null));
        await fixture.Harness.Appointments.SetStatusAsync(created.Id, new SetAppointmentStatusRequest("InProgress", null));
        await fixture.Harness.Appointments.SetStatusAsync(created.Id, new SetAppointmentStatusRequest("Completed", null));

        var rows = await RowsAsync(fixture.Harness, created.Id);

        Assert.Equal(3, rows.Count);
        Assert.All(rows, row =>
        {
            Assert.Equal(ReminderStatus.Cancelled, row.Status);
            Assert.Equal(ReminderFailureReasons.AppointmentCompleted, row.FailureReason);
        });
    }

    [Fact]
    public async Task A_no_show_drops_pending_rows()
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;

        var created = await fixture.Harness.Appointments.CreateAsync(
            Request(fixture.CustomerId, services: [new(fixture.ServiceId, fixture.StaffId)]));

        await fixture.Harness.Appointments.SetStatusAsync(created.Id, new SetAppointmentStatusRequest("NoShow", null));

        var rows = await RowsAsync(fixture.Harness, created.Id);

        Assert.Equal(3, rows.Count);
        Assert.All(rows, row =>
        {
            Assert.Equal(ReminderStatus.Cancelled, row.Status);
            Assert.Equal(ReminderFailureReasons.AppointmentNoShow, row.FailureReason);
        });
    }

    [Fact]
    public async Task Rescheduling_cancels_stale_timed_rows_and_rebuilds_them_for_the_new_time()
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;

        var created = await fixture.Harness.Appointments.CreateAsync(
            Request(fixture.CustomerId, services: [new(fixture.ServiceId, fixture.StaffId)]));

        await fixture.Harness.Appointments.UpdateAsync(
            created.Id,
            Request(fixture.CustomerId, startsAt: At(2, 14), services: [new(fixture.ServiceId, fixture.StaffId)]));

        var rows = await RowsAsync(fixture.Harness, created.Id);

        Assert.Equal(6, rows.Count);

        var cancelledTimed = rows
            .Where(row => row.ReminderType == ReminderType.Reminder && row.Status == ReminderStatus.Cancelled)
            .ToList();
        Assert.Equal(2, cancelledTimed.Count);
        Assert.Contains(cancelledTimed, row => row.ScheduledAt == At(1, 10));
        Assert.Contains(cancelledTimed, row => row.ScheduledAt == At(2, 8));
        Assert.All(cancelledTimed, row => Assert.Equal(ReminderFailureReasons.AppointmentRescheduled, row.FailureReason));

        var pendingTimed = rows
            .Where(row => row.ReminderType == ReminderType.Reminder && row.Status == ReminderStatus.Pending)
            .ToList();
        Assert.Equal(2, pendingTimed.Count);
        Assert.Contains(pendingTimed, row => row.ScheduledAt == At(1, 14)); // 24 hours before the new start
        Assert.Contains(pendingTimed, row => row.ScheduledAt == At(2, 12)); // 2 hours before the new start

        // The confirmation from the booking is untouched; the move adds one
        // immediate reschedule notice.
        Assert.Equal(ReminderStatus.Pending,
            rows.Single(row => row.ReminderType == ReminderType.Confirmation).Status);
        Assert.Equal(Saturday, rows.Single(row => row.ReminderType == ReminderType.Rescheduled).ScheduledAt);
    }

    [Fact]
    public async Task A_second_move_keeps_one_pending_reschedule_notice()
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;

        var created = await fixture.Harness.Appointments.CreateAsync(
            Request(fixture.CustomerId, services: [new(fixture.ServiceId, fixture.StaffId)]));

        await fixture.Harness.Appointments.UpdateAsync(
            created.Id,
            Request(fixture.CustomerId, startsAt: At(2, 14), services: [new(fixture.ServiceId, fixture.StaffId)]));
        await fixture.Harness.Appointments.UpdateAsync(
            created.Id,
            Request(fixture.CustomerId, startsAt: At(2, 15), services: [new(fixture.ServiceId, fixture.StaffId)]));

        var rows = await RowsAsync(fixture.Harness, created.Id);

        // Two cancelled pairs (booking, first move) plus the collapsed notice.
        Assert.Equal(4, rows.Count(row => row.ReminderType == ReminderType.Reminder && row.Status == ReminderStatus.Cancelled));
        Assert.Single(rows, row => row.ReminderType == ReminderType.Rescheduled && row.Status == ReminderStatus.Pending);
        Assert.Contains(rows, row => row.ReminderType == ReminderType.Reminder
            && row.Status == ReminderStatus.Pending && row.ScheduledAt == At(2, 13));
    }

    [Fact]
    public async Task A_bundle_booking_dispatches_the_bundle_name_not_the_component_services()
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;

        var colourId = await fixture.Harness.SeedServiceAsync(
            fixture.CategoryId, "Colour", durationMinutes: 60, price: 150m, staffIds: [fixture.StaffId]);
        var bundleId = await fixture.Harness.SeedBundleAsync(
            fixture.CategoryId, "Hair Makeover", 200m, [fixture.ServiceId, colourId]);

        var created = await fixture.Harness.Appointments.CreateAsync(
            Request(fixture.CustomerId, services:
            [
                new(fixture.ServiceId, fixture.StaffId),
                new(colourId, fixture.StaffId)
            ], serviceBundleId: bundleId));

        var result = await fixture.Harness.DispatchAsync();

        Assert.Equal(1, result.Sent);

        var message = Assert.Single(fixture.Harness.Provider.SentMessages);
        Assert.Equal("Hair Makeover", message.Variables[ReminderTemplateVariables.ServiceName]);
        Assert.Equal("Jasmin", message.Variables[ReminderTemplateVariables.StaffName]);
        Assert.Equal("Sara Haddad", message.Variables[ReminderTemplateVariables.CustomerName]);
        Assert.Equal("DeBliss", message.Variables[ReminderTemplateVariables.SalonName]);
        Assert.Equal(created.Id.ToString(), message.Variables[ReminderTemplateVariables.AppointmentId]);
    }
}
