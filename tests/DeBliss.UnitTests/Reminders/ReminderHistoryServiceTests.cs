using DeBliss.Application.Common.Exceptions;
using DeBliss.Application.Notifications;
using DeBliss.Application.Notifications.Dtos;
using DeBliss.Domain.Notifications;

namespace DeBliss.UnitTests.Reminders;

/// <summary>
/// The read surface the operations screens use: newest-first history with the
/// optional filters validated against the enums, and the appointment panel's
/// own oldest-first list of one appointment's rows.
/// </summary>
public sealed class ReminderHistoryServiceTests
{
    private static readonly DateTimeOffset Saturday = new(2026, 10, 3, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Monday10 = new(2026, 10, 5, 10, 0, 0, TimeSpan.Zero);

    private sealed record Fixture(
        Guid CustomerId,
        Guid AppointmentId,
        Guid SentId,
        Guid PendingId,
        Guid FailedId);

    private static async Task<Fixture> ArrangeAsync(RemindersHarness harness)
    {
        var customerId = await harness.SeedCustomerAsync();
        var appointmentId = await harness.SeedAppointmentAsync(customerId, Monday10);

        var sentId = await harness.SeedReminderAsync(
            appointmentId, customerId, ReminderType.Confirmation, Saturday,
            status: ReminderStatus.Sent,
            attemptCount: 1,
            sentAt: Saturday,
            lastAttemptAt: Saturday,
            providerMessageId: "SM-1");

        var pendingId = await harness.SeedReminderAsync(
            appointmentId, customerId, ReminderType.Reminder, Monday10.AddHours(-2));

        var failedId = await harness.SeedReminderAsync(
            appointmentId, customerId, ReminderType.Reminder, Monday10,
            status: ReminderStatus.Failed,
            attemptCount: 3,
            lastAttemptAt: Monday10,
            failureReason: ReminderFailureReasons.MissingPhoneNumber);

        return new Fixture(customerId, appointmentId, sentId, pendingId, failedId);
    }

    private static Guid[] Ids(IReadOnlyList<AppointmentReminderDto> rows) =>
        rows.Select(row => row.Id).ToArray();

    [Fact]
    public async Task History_lists_the_newest_scheduled_first()
    {
        using var harness = new RemindersHarness();
        var fixture = await ArrangeAsync(harness);

        var rows = await harness.History.QueryAsync(new ReminderHistoryQuery());

        Assert.Equal(new[] { fixture.FailedId, fixture.PendingId, fixture.SentId }, Ids(rows));
    }

    [Fact]
    public async Task A_history_row_carries_everything_the_table_shows()
    {
        using var harness = new RemindersHarness();
        var fixture = await ArrangeAsync(harness);

        var rows = await harness.History.QueryAsync(new ReminderHistoryQuery());

        var sent = rows.Single(row => row.Id == fixture.SentId);
        Assert.Equal(fixture.AppointmentId, sent.AppointmentId);
        Assert.Equal(fixture.CustomerId, sent.CustomerId);
        Assert.Equal("Sara Haddad", sent.CustomerName);
        Assert.Equal(Monday10, sent.AppointmentStartsAt);
        Assert.Equal("Confirmation", sent.ReminderType);
        Assert.Equal("WhatsApp", sent.Channel);
        Assert.Equal("Sent", sent.Status);
        Assert.Equal(Saturday, sent.ScheduledAt);
        Assert.Equal(Saturday, sent.SentAt);
        Assert.Equal(1, sent.AttemptCount);
        Assert.Equal("SM-1", sent.ProviderMessageId);
        Assert.Null(sent.FailureReason);

        var failed = rows.Single(row => row.Id == fixture.FailedId);
        Assert.Equal("Failed", failed.Status);
        Assert.Equal(3, failed.AttemptCount);
        Assert.Equal(Monday10, failed.LastAttemptAt);
        Assert.Equal(ReminderFailureReasons.MissingPhoneNumber, failed.FailureReason);
    }

    [Fact]
    public async Task The_status_filter_is_case_insensitive()
    {
        using var harness = new RemindersHarness();
        var fixture = await ArrangeAsync(harness);

        var rows = await harness.History.QueryAsync(new ReminderHistoryQuery(Status: "sent"));

        Assert.Equal(new[] { fixture.SentId }, Ids(rows));
    }

    [Fact]
    public async Task The_type_filter_narrows_the_rows()
    {
        using var harness = new RemindersHarness();
        var fixture = await ArrangeAsync(harness);

        var rows = await harness.History.QueryAsync(new ReminderHistoryQuery(ReminderType: "confirmation"));

        Assert.Equal(new[] { fixture.SentId }, Ids(rows));
    }

    [Fact]
    public async Task Filters_combine()
    {
        using var harness = new RemindersHarness();
        var fixture = await ArrangeAsync(harness);

        var rows = await harness.History.QueryAsync(
            new ReminderHistoryQuery(Status: "failed", ReminderType: "reminder"));

        Assert.Equal(new[] { fixture.FailedId }, Ids(rows));
    }

    [Fact]
    public async Task An_appointment_or_customer_filter_scopes_the_rows()
    {
        using var harness = new RemindersHarness();
        var fixture = await ArrangeAsync(harness);

        var byAppointment = await harness.History.QueryAsync(
            new ReminderHistoryQuery(AppointmentId: fixture.AppointmentId));
        Assert.Equal(3, byAppointment.Count);

        var byCustomer = await harness.History.QueryAsync(
            new ReminderHistoryQuery(CustomerId: fixture.CustomerId));
        Assert.Equal(3, byCustomer.Count);

        var unknown = await harness.History.QueryAsync(
            new ReminderHistoryQuery(AppointmentId: Guid.NewGuid()));
        Assert.Empty(unknown);
    }

    [Fact]
    public async Task The_scheduled_window_keeps_only_rows_inside_it()
    {
        using var harness = new RemindersHarness();
        var fixture = await ArrangeAsync(harness);

        var fromTheAppointment = await harness.History.QueryAsync(
            new ReminderHistoryQuery(ScheduledFrom: Monday10));
        Assert.Equal(new[] { fixture.FailedId }, Ids(fromTheAppointment));

        var untilSunday = await harness.History.QueryAsync(
            new ReminderHistoryQuery(ScheduledTo: Saturday.AddDays(1)));
        Assert.Equal(new[] { fixture.SentId }, Ids(untilSunday));
    }

    [Fact]
    public async Task Take_keeps_the_newest_rows_and_is_clamped_to_at_least_one()
    {
        using var harness = new RemindersHarness();
        var fixture = await ArrangeAsync(harness);

        var two = await harness.History.QueryAsync(new ReminderHistoryQuery(Take: 2));
        Assert.Equal(new[] { fixture.FailedId, fixture.PendingId }, Ids(two));

        var clamped = await harness.History.QueryAsync(new ReminderHistoryQuery(Take: 0));
        Assert.Equal(new[] { fixture.FailedId }, Ids(clamped));
    }

    [Fact]
    public async Task An_unknown_status_is_rejected_with_its_field_name()
    {
        using var harness = new RemindersHarness();

        var error = await Assert.ThrowsAsync<ValidationFailedException>(() =>
            harness.History.QueryAsync(new ReminderHistoryQuery(Status: "Soon")));

        Assert.True(error.Errors.ContainsKey("status"));
        Assert.Contains("Soon", error.Errors["status"][0]);
    }

    [Fact]
    public async Task An_unknown_reminder_type_is_rejected_with_its_field_name()
    {
        using var harness = new RemindersHarness();

        var error = await Assert.ThrowsAsync<ValidationFailedException>(() =>
            harness.History.QueryAsync(new ReminderHistoryQuery(ReminderType: "Marketing")));

        Assert.True(error.Errors.ContainsKey("reminderType"));
    }

    [Fact]
    public async Task The_appointment_panel_lists_only_its_own_rows_oldest_first()
    {
        using var harness = new RemindersHarness();
        var fixture = await ArrangeAsync(harness);

        var otherCustomerId = await harness.SeedCustomerAsync(firstName: "Noor", lastName: "Aziz");
        var otherAppointmentId = await harness.SeedAppointmentAsync(otherCustomerId, Monday10.AddDays(1));
        await harness.SeedReminderAsync(
            otherAppointmentId, otherCustomerId, ReminderType.Confirmation, Monday10.AddDays(1));

        var rows = await harness.History.ListForAppointmentAsync(fixture.AppointmentId);

        Assert.Equal(new[] { fixture.SentId, fixture.PendingId, fixture.FailedId }, Ids(rows));
        Assert.All(rows, row => Assert.Equal(fixture.AppointmentId, row.AppointmentId));
        Assert.Equal("Sara Haddad", rows[0].CustomerName);
    }
}
