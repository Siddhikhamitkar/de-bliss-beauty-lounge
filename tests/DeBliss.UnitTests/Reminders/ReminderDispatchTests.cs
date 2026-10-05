using DeBliss.Application.Notifications;
using DeBliss.Application.Notifications.Ports;
using DeBliss.Domain.Notifications;
using DeBliss.Domain.Scheduling;
using DeBliss.UnitTests.TestDoubles;
using Microsoft.EntityFrameworkCore;

namespace DeBliss.UnitTests.Reminders;

/// <summary>
/// Drives the dispatcher over directly-seeded reminder rows: success, opt-in
/// and phone gates, template gate, transient retries with backoff, the attempt
/// cap, permanent failures, a throwing provider, the unconfigured no-op, row
/// cancellation for terminal appointments, and the claim/enqueue race rules.
/// </summary>
public class ReminderDispatchTests
{
    private static readonly DateTimeOffset Saturday = new(2026, 10, 3, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Monday10 = new(2026, 10, 5, 10, 0, 0, TimeSpan.Zero);

    private sealed record Fixture(
        Guid CustomerId,
        Guid CategoryId,
        Guid ServiceId,
        Guid StaffId,
        Guid AppointmentId);

    private static async Task<Fixture> ArrangeAsync(
        RemindersHarness harness,
        WhatsAppOptInStatus optInStatus = WhatsAppOptInStatus.OptedIn,
        string phone = "+971500000001",
        AppointmentStatus appointmentStatus = AppointmentStatus.Confirmed)
    {
        var customerId = await harness.SeedCustomerAsync(phoneNumber: phone, optInStatus: optInStatus);
        var categoryId = await harness.SeedCategoryAsync("Hair");
        var staffId = await harness.SeedStaffAsync("Jasmin", "Rae");
        var serviceId = await harness.SeedServiceAsync(categoryId, "Haircut", staffIds: [staffId]);
        var appointmentId = await harness.SeedAppointmentAsync(
            customerId,
            Monday10,
            status: appointmentStatus,
            lines: [new SchedulingSeed.Line(serviceId, staffId, "Haircut")]);
        return new Fixture(customerId, categoryId, serviceId, staffId, appointmentId);
    }

    private static async Task<List<AppointmentReminder>> RowsAsync(
        RemindersHarness harness, Guid appointmentId)
    {
        await using var context = harness.Reopen();
        var rows = await context.AppointmentReminders
            .Where(row => row.AppointmentId == appointmentId)
            .ToListAsync();
        return rows.OrderBy(row => row.ScheduledAt).ThenBy(row => row.ReminderType).ToList();
    }

    [Fact]
    public async Task A_due_row_is_sent_and_records_provider_identity()
    {
        using var harness = new RemindersHarness();
        var fixture = await ArrangeAsync(harness);
        harness.Provider.QueuedResults.Enqueue(NotificationSendResult.Sent("SM1234"));
        var reminderId = await harness.SeedReminderAsync(
            fixture.AppointmentId, fixture.CustomerId, ReminderType.Confirmation, Saturday);

        var result = await harness.DispatchAsync();

        var row = Assert.Single(await RowsAsync(harness, fixture.AppointmentId));
        Assert.Equal("SM1234", row.ProviderMessageId);
        Assert.NotNull(row.SentAt);
        Assert.Equal(1, row.AttemptCount);
        Assert.Null(row.ClaimedBy);
        Assert.Null(row.FailureReason);
        Assert.Equal("+971500000001", harness.Provider.SentMessages[0].RecipientE164);
        Assert.Equal("HXconfirmation", harness.Provider.SentMessages[0].TemplateId);
        Assert.Equal(fixture.AppointmentId.ToString(), harness.Provider.SentMessages[0].Variables[ReminderTemplateVariables.AppointmentId]);
        Assert.Equal(1, result.Sent);
        Assert.Equal(0, result.Deferred);
    }

    [Fact]
    public async Task An_opted_out_customer_row_is_skipped_and_never_sent()
    {
        using var harness = new RemindersHarness();
        var fixture = await ArrangeAsync(harness, optInStatus: WhatsAppOptInStatus.OptedOut);

        var id = await harness.SeedReminderAsync(
            fixture.AppointmentId, fixture.CustomerId, ReminderType.Confirmation, Saturday);

        var result = await harness.DispatchAsync();

        var row = Assert.Single(await RowsAsync(harness, fixture.AppointmentId));
        Assert.Equal(ReminderFailureReasons.CustomerOptedOut, row.FailureReason);
        Assert.Equal(0, harness.Provider.SendCount);
        Assert.Equal(1, result.Skipped);
    }

    [Fact]
    public async Task A_never_optin_row_is_skipped_and_never_sent()
    {
        using var harness = new RemindersHarness();
        var fixture = await ArrangeAsync(harness, optInStatus: WhatsAppOptInStatus.Unknown);

        await harness.SeedReminderAsync(fixture.AppointmentId, fixture.CustomerId, ReminderType.Confirmation, Saturday);

        var result = await harness.DispatchAsync();

        var row = await RowsAsync(harness, fixture.AppointmentId);
        Assert.Equal(ReminderStatus.Skipped, row[0].Status);
        Assert.Equal(ReminderFailureReasons.CustomerNotOptedIn, row[0].FailureReason);
        Assert.Equal(0, harness.Provider.SendCount);
        Assert.Equal(1, result.Skipped);
    }

    [Fact]
    public async Task An_invalid_phone_fails_the_row_without_sending()
    {
        using var harness = new RemindersHarness();
        var fixture = await ArrangeAsync(harness, phone: "12");

        await harness.SeedReminderAsync(fixture.AppointmentId, fixture.CustomerId, ReminderType.Confirmation, Saturday);

        var result = await harness.DispatchAsync();

        var row = Assert.Single(await RowsAsync(harness, fixture.AppointmentId));
        Assert.Equal(ReminderStatus.Failed, row.Status);
        Assert.Equal(ReminderFailureReasons.InvalidPhoneNumber, row.FailureReason);
        Assert.Equal(0, harness.Provider.SendCount);
        Assert.Equal(1, result.Failed);
    }

    [Fact]
    public async Task A_missing_phone_fails_the_row()
    {
        using var harness = new RemindersHarness();
        var fixture = await ArrangeAsync(harness, phone: "");

        await harness.SeedReminderAsync(fixture.AppointmentId, fixture.CustomerId, ReminderType.Confirmation, Saturday);

        var result = await harness.DispatchAsync();

        var row = Assert.Single(await RowsAsync(harness, fixture.AppointmentId));
        Assert.Equal(ReminderStatus.Failed, row.Status);
        Assert.Equal(ReminderFailureReasons.MissingPhoneNumber, row.FailureReason);
        Assert.Equal(0, harness.Provider.SendCount);
        Assert.Equal(1, result.Failed);
    }

    [Fact]
    public async Task A_missing_template_fails_the_row_without_sending()
    {
        using var harness = new RemindersHarness();
        var fixture = await ArrangeAsync(harness);
        harness.Provider.Templates[ReminderType.Confirmation] = null;

        await harness.SeedReminderAsync(fixture.AppointmentId, fixture.CustomerId, ReminderType.Confirmation, Saturday);

        var result = await harness.DispatchAsync();

        var row = Assert.Single(await RowsAsync(harness, fixture.AppointmentId));
        Assert.Equal(ReminderStatus.Failed, row.Status);
        Assert.Equal(ReminderFailureReasons.TemplateNotConfigured, row.FailureReason);
        Assert.Equal(0, harness.Provider.SendCount);
        Assert.Equal(1, result.Failed);
    }

    private static DateTimeOffset At(int dayOffset, int hour, int minute) =>
        Saturday.AddDays(dayOffset).AddHours(hour - 9).AddMinutes(minute);

    [Fact]
    public async Task A_transient_failure_defers_and_retries_after_the_backoff()
    {
        using var harness = new RemindersHarness();
        var fixture = await ArrangeAsync(harness);
        harness.Provider.QueuedResults.Enqueue(NotificationSendResult.Transient("Provider timeout"));

        var reminderId = await harness.SeedReminderAsync(
            fixture.AppointmentId, fixture.CustomerId, ReminderType.Confirmation, Saturday);

        var first = await harness.DispatchAsync();

        var row = Assert.Single(await RowsAsync(harness, fixture.AppointmentId));
        Assert.Equal(ReminderStatus.Pending, row.Status);
        Assert.Equal(1, row.AttemptCount);
        Assert.Equal(Saturday.AddMinutes(5), row.NextAttemptAt);
        Assert.Contains("Provider timeout", row.FailureReason);
        Assert.Equal(1, first.Deferred);

        // Inside the backoff window nothing is due yet.
        Assert.Equal(0, (await harness.DispatchAsync()).Deferred);

        harness.Clock.Advance(TimeSpan.FromMinutes(5));
        harness.Provider.QueuedResults.Enqueue(NotificationSendResult.Sent("SM5678"));

        var second = await harness.DispatchAsync();

        var retried = Assert.Single(await RowsAsync(harness, fixture.AppointmentId));
        Assert.Equal(ReminderStatus.Sent, retried.Status);
        Assert.Equal(2, retried.AttemptCount);
        Assert.Equal(2, harness.Provider.SendCount);
        Assert.Equal(1, second.Sent);
    }

    [Fact]
    public async Task The_attempt_cap_marks_the_row_failed()
    {
        using var harness = new RemindersHarness();
        var fixture = await ArrangeAsync(harness);

        // Already at the cap: the final transient failure fails for good.
        await harness.SeedReminderAsync(
            fixture.AppointmentId, fixture.CustomerId, ReminderType.Confirmation, Saturday,
            attemptCount: 2);
        harness.Provider.QueuedResults.Enqueue(NotificationSendResult.Transient("Provider timeout"));

        var result = await harness.DispatchAsync();

        var row = Assert.Single(await RowsAsync(harness, fixture.AppointmentId));
        Assert.Equal(ReminderStatus.Failed, row.Status);
        Assert.Equal(3, row.AttemptCount);
        Assert.Null(row.NextAttemptAt);
        Assert.Equal(1, result.Failed);
        Assert.Equal(0, result.Deferred);
    }

    [Fact]
    public async Task A_permanent_failure_fails_immediately()
    {
        using var harness = new RemindersHarness();
        var fixture = await ArrangeAsync(harness);
        harness.Provider.QueuedResults.Enqueue(
            NotificationSendResult.Permanent("Invalid recipient", "400"));

        await harness.SeedReminderAsync(fixture.AppointmentId, fixture.CustomerId, ReminderType.Confirmation, Saturday);

        var result = await harness.DispatchAsync();

        var row = Assert.Single(await RowsAsync(harness, fixture.AppointmentId));
        Assert.Equal(ReminderStatus.Failed, row.Status);
        Assert.Equal(1, row.AttemptCount);
        Assert.Contains("Invalid recipient", row.FailureReason);
        Assert.Contains("400", row.FailureReason);
        Assert.Equal(1, result.Failed);
    }

    [Fact]
    public async Task A_throwing_provider_defers_the_row()
    {
        using var harness = new RemindersHarness();
        var fixture = await ArrangeAsync(harness);
        harness.Provider.ThrowOnSend = true;

        await harness.SeedReminderAsync(fixture.AppointmentId, fixture.CustomerId, ReminderType.Confirmation, Saturday);

        var result = await harness.DispatchAsync();

        var row = Assert.Single(await RowsAsync(harness, fixture.AppointmentId));
        Assert.Equal(ReminderStatus.Pending, row.Status);
        Assert.Equal(1, row.AttemptCount);
        Assert.NotNull(row.NextAttemptAt);
        Assert.Equal(1, result.Deferred);
    }

    [Fact]
    public async Task An_unconfigured_provider_leaves_everything_untouched()
    {
        using var harness = new RemindersHarness();
        var fixture = await ArrangeAsync(harness);
        harness.Provider.IsConfigured = false;

        await harness.SeedReminderAsync(fixture.AppointmentId, fixture.CustomerId, ReminderType.Confirmation, Saturday);

        var result = await harness.DispatchAsync();

        var row = Assert.Single(await RowsAsync(harness, fixture.AppointmentId));
        Assert.Equal(ReminderStatus.Pending, row.Status);
        Assert.Equal(0, row.AttemptCount);
        Assert.Equal(0, harness.Provider.SendCount);
        Assert.Equal(0, result.Sent + result.Failed + result.Skipped + result.Cancelled + result.Deferred);
    }

    [Fact]
    public async Task A_cancelled_appointment_row_is_cancelled_without_sending()
    {
        using var harness = new RemindersHarness();
        var fixture = await ArrangeAsync(harness, appointmentStatus: AppointmentStatus.Cancelled);

        await harness.SeedReminderAsync(fixture.AppointmentId, fixture.CustomerId, ReminderType.Reminder, Saturday);

        var result = await harness.DispatchAsync();

        var row = Assert.Single(await RowsAsync(harness, fixture.AppointmentId));
        Assert.Equal(ReminderStatus.Cancelled, row.Status);
        Assert.Equal(ReminderFailureReasons.AppointmentCancelled, row.FailureReason);
        Assert.Equal(0, harness.Provider.SendCount);
        Assert.Equal(1, result.Cancelled);
    }

    [Fact]
    public async Task A_completed_appointment_row_is_cancelled_without_sending()
    {
        using var harness = new RemindersHarness();
        var fixture = await ArrangeAsync(harness, appointmentStatus: AppointmentStatus.Completed);

        await harness.SeedReminderAsync(fixture.AppointmentId, fixture.CustomerId, ReminderType.Reminder, Saturday);

        var result = await harness.DispatchAsync();

        var row = Assert.Single(await RowsAsync(harness, fixture.AppointmentId));
        Assert.Equal(ReminderStatus.Cancelled, row.Status);
        Assert.Equal(0, harness.Provider.SendCount);
        Assert.Equal(1, result.Cancelled);
    }

    [Fact]
    public async Task The_cancellation_notice_still_sends_for_a_cancelled_appointment()
    {
        using var harness = new RemindersHarness();
        var fixture = await ArrangeAsync(harness, appointmentStatus: AppointmentStatus.Cancelled);
        harness.Provider.QueuedResults.Enqueue(NotificationSendResult.Sent("SM9999"));

        await harness.SeedReminderAsync(
            fixture.AppointmentId, fixture.CustomerId, ReminderType.Cancellation, Saturday);

        var result = await harness.DispatchAsync();

        var row = Assert.Single(await RowsAsync(harness, fixture.AppointmentId));
        Assert.Equal(ReminderStatus.Sent, row.Status);
        Assert.Equal("HXcancellation", harness.Provider.SentMessages[0].TemplateId);
        Assert.Equal(1, result.Sent);
    }

    [Fact]
    public async Task A_row_claimed_by_another_worker_is_not_due()
    {
        using var harness = new RemindersHarness();
        var fixture = await ArrangeAsync(harness);

        await harness.SeedReminderAsync(
            fixture.AppointmentId, fixture.CustomerId, ReminderType.Confirmation, Saturday,
            claimedBy: "another-worker", claimedAt: Saturday);

        var result = await harness.DispatchAsync();

        var row = Assert.Single(await RowsAsync(harness, fixture.AppointmentId));
        Assert.Equal(ReminderStatus.Pending, row.Status);
        Assert.Equal("another-worker", row.ClaimedBy);
        Assert.Equal(0, harness.Provider.SendCount);
        Assert.Equal(0, result.Sent + result.Failed + result.Skipped + result.Cancelled + result.Deferred);
    }

    [Fact]
    public async Task A_stale_claim_is_taken_over()
    {
        using var harness = new RemindersHarness();
        var fixture = await ArrangeAsync(harness);
        harness.Provider.QueuedResults.Enqueue(NotificationSendResult.Sent("SM4321"));

        // The claim is older than the lease, so the row comes back into view.
        await harness.SeedReminderAsync(
            fixture.AppointmentId, fixture.CustomerId, ReminderType.Confirmation, Saturday,
            attemptCount: 1, claimedBy: "dead-worker", claimedAt: Saturday.AddMinutes(-30));

        var result = await harness.DispatchAsync();

        var row = Assert.Single(await RowsAsync(harness, fixture.AppointmentId));
        Assert.Equal(ReminderStatus.Sent, row.Status);
        Assert.Equal(2, row.AttemptCount);
        Assert.Equal(1, result.Sent);
    }
}
