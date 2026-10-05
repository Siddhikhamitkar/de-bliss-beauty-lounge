using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DeBliss.Application.Identity;
using DeBliss.Application.Notifications;
using DeBliss.Application.Notifications.Dtos;
using DeBliss.Domain.Notifications;
using DeBliss.Domain.Scheduling;
using DeBliss.UnitTests.TestDoubles;

namespace DeBliss.UnitTests.Api;

/// <summary>
/// The reminder surface over the real pipeline: history reads with their filters,
/// the rule table, the (unconfigured) provider status, the gated test send, and
/// the role boundary — owner-only settings, receptionist read access, staff excluded.
/// </summary>
public sealed class RemindersApiTests : ApiTestBase
{
    private const string ReceptionistEmail = "receptionist@debliss.test";
    private const string ReceptionistPassword = "Test#Front123";
    private const string StaffEmail = "staff@debliss.test";
    private const string StaffPassword = "Test#Team123";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly DateTimeOffset Saturday = new(2026, 10, 3, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Monday10 = new(2026, 10, 5, 10, 0, 0, TimeSpan.Zero);

    public RemindersApiTests()
    {
        Factory.SeedUserAsync(
            ReceptionistEmail, ReceptionistPassword, RoleNames.Receptionist, "Rita", "Desk").GetAwaiter().GetResult();
        Factory.SeedUserAsync(
            StaffEmail, StaffPassword, RoleNames.Staff, "Aisha", "Stylist").GetAwaiter().GetResult();
    }

    private async Task<(Guid AppointmentId, Guid SentId, Guid FailedId)> SeedHistoryAsync()
    {
        var customerId = await Factory.SeedCustomerAsync("Sara", "Haddad", "+971 50 000 0001");
        var appointmentId = await Factory.SeedCustomerAppointmentAsync(
            customerId, Monday10, AppointmentStatus.Confirmed);

        await using var context = Factory.Database.CreateContext();
        var sentId = await ReminderSeed.ReminderAsync(
            context, appointmentId, customerId, ReminderType.Confirmation, Saturday,
            ReminderStatus.Sent,
            attemptCount: 1,
            sentAt: Saturday,
            lastAttemptAt: Saturday,
            providerMessageId: "SM-API-1");
        var failedId = await ReminderSeed.ReminderAsync(
            context, appointmentId, customerId, ReminderType.Reminder, Monday10.AddHours(-2),
            ReminderStatus.Failed,
            attemptCount: 3,
            lastAttemptAt: Monday10,
            failureReason: "The provider is unreachable.");

        return (appointmentId, sentId, failedId);
    }

    [Fact]
    public async Task The_owner_sees_the_history_newest_first_with_full_rows()
    {
        var (_, sentId, failedId) = await SeedHistoryAsync();

        var rows = await ReadAsync<List<AppointmentReminderDto>>(
            await Client.GetAsync("/api/reminders/history"));

        Assert.Equal([failedId, sentId], rows.Select(row => row.Id));

        var failed = rows[0];
        Assert.Equal("Failed", failed.Status);
        Assert.Equal("Reminder", failed.ReminderType);
        Assert.Equal("WhatsApp", failed.Channel);
        Assert.Equal("Sara Haddad", failed.CustomerName);
        Assert.Equal(Monday10, failed.AppointmentStartsAt);
        Assert.Equal(3, failed.AttemptCount);
        Assert.Equal("The provider is unreachable.", failed.FailureReason);

        var sent = rows[1];
        Assert.Equal("Sent", sent.Status);
        Assert.Equal("Confirmation", sent.ReminderType);
        Assert.Equal("SM-API-1", sent.ProviderMessageId);
        Assert.Equal(Saturday, sent.SentAt);
        Assert.Null(sent.FailureReason);
    }

    [Fact]
    public async Task The_history_status_filter_narrows_the_rows()
    {
        var (_, sentId, _) = await SeedHistoryAsync();

        var rows = await ReadAsync<List<AppointmentReminderDto>>(
            await Client.GetAsync("/api/reminders/history?status=Sent"));

        Assert.Equal(sentId, Assert.Single(rows).Id);
    }

    [Fact]
    public async Task An_unknown_status_filter_is_a_validation_error()
    {
        var response = await Client.GetAsync("/api/reminders/history?status=Soon");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadAsync<ValidationProblem>(response);
        Assert.True(problem.Errors.ContainsKey("status"));
    }

    [Fact]
    public async Task The_default_rules_are_served_and_can_be_toggled_persistently()
    {
        var rules = await ReadAsync<List<ReminderRuleDto>>(await Client.GetAsync("/api/reminders/rules"));

        Assert.Equal(
            [
                ("Confirmation", 0), ("Reminder", 1440), ("Reminder", 120),
                ("Cancellation", 0), ("Rescheduled", 0),
            ],
            rules.Select(rule => (rule.ReminderType, rule.OffsetMinutes)));
        Assert.All(rules, rule => Assert.True(rule.IsEnabled));

        var reminder1440 = rules.First(rule => rule.ReminderType == "Reminder" && rule.OffsetMinutes == 1440);
        var update = await Client.PutAsJsonAsync(
            $"/api/reminders/rules/{reminder1440.Id}", new { isEnabled = false }, Json);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        var reread = await ReadAsync<List<ReminderRuleDto>>(await Client.GetAsync("/api/reminders/rules"));
        Assert.False(reread.First(rule => rule.Id == reminder1440.Id).IsEnabled);
        Assert.True(reread.First(rule => rule.Id != reminder1440.Id).IsEnabled);
    }

    [Fact]
    public async Task Retiming_a_notice_without_timing_is_rejected()
    {
        var rules = await ReadAsync<List<ReminderRuleDto>>(await Client.GetAsync("/api/reminders/rules"));
        var cancellation = rules.First(rule => rule.ReminderType == "Cancellation");

        var response = await Client.PutAsJsonAsync(
            $"/api/reminders/rules/{cancellation.Id}", new { offsetMinutes = 60 }, Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadAsync<ValidationProblem>(response);
        Assert.True(problem.Errors.ContainsKey("offsetMinutes"));
    }

    [Fact]
    public async Task Updating_an_unknown_rule_is_a_not_found()
    {
        var response = await Client.PutAsJsonAsync(
            $"/api/reminders/rules/{Guid.NewGuid()}", new { isEnabled = false }, Json);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task The_provider_status_degrades_cleanly_and_never_returns_credentials()
    {
        var response = await Client.GetAsync("/api/reminders/provider");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("AccountSid", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AuthToken", body, StringComparison.OrdinalIgnoreCase);

        var status = JsonSerializer.Deserialize<ReminderProviderStatusDto>(body, Json);
        Assert.NotNull(status);
        Assert.False(status.IsConfigured);
        Assert.Equal("WhatsApp", status.Channel);
    }

    [Fact]
    public async Task The_test_send_is_refused_cleanly_while_whatsapp_is_not_configured()
    {
        var response = await Client.PostAsJsonAsync(
            "/api/reminders/test", new { phoneNumber = "+971500000001" }, Json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await ReadAsync<SendTestMessageResult>(response);
        Assert.False(result.Success);
        Assert.Equal(ReminderFailureReasons.ProviderNotConfigured, result.FailureReason);
        Assert.Null(result.ProviderMessageId);
    }

    [Fact]
    public async Task The_receptionist_watches_the_history_but_cannot_touch_the_settings()
    {
        await SeedHistoryAsync();

        using var receptionist = AnonymousClient;
        await AuthenticateAsync(receptionist, ReceptionistEmail, ReceptionistPassword);

        var history = await receptionist.GetAsync("/api/reminders/history");
        Assert.Equal(HttpStatusCode.OK, history.StatusCode);

        var rules = await receptionist.GetAsync("/api/reminders/rules");
        Assert.Equal(HttpStatusCode.Forbidden, rules.StatusCode);

        var provider = await receptionist.GetAsync("/api/reminders/provider");
        Assert.Equal(HttpStatusCode.Forbidden, provider.StatusCode);

        var testSend = await receptionist.PostAsJsonAsync(
            "/api/reminders/test", new { phoneNumber = "+971500000001" }, Json);
        Assert.Equal(HttpStatusCode.Forbidden, testSend.StatusCode);

        var rulesList = await ReadAsync<List<ReminderRuleDto>>(await Client.GetAsync("/api/reminders/rules"));
        var toggle = await receptionist.PutAsJsonAsync(
            $"/api/reminders/rules/{rulesList[0].Id}", new { isEnabled = false }, Json);
        Assert.Equal(HttpStatusCode.Forbidden, toggle.StatusCode);
    }

    [Fact]
    public async Task The_staff_role_cannot_reach_the_reminder_surface()
    {
        var (appointmentId, _, _) = await SeedHistoryAsync();

        using var staff = AnonymousClient;
        await AuthenticateAsync(staff, StaffEmail, StaffPassword);

        var history = await staff.GetAsync("/api/reminders/history");
        Assert.Equal(HttpStatusCode.Forbidden, history.StatusCode);

        var panel = await staff.GetAsync($"/api/appointments/{appointmentId}/reminders");
        Assert.Equal(HttpStatusCode.Forbidden, panel.StatusCode);
    }

    [Fact]
    public async Task Anonymous_callers_are_sent_to_sign_in()
    {
        using var anonymous = AnonymousClient;

        var history = await anonymous.GetAsync("/api/reminders/history");
        Assert.Equal(HttpStatusCode.Unauthorized, history.StatusCode);

        var rules = await anonymous.GetAsync("/api/reminders/rules");
        Assert.Equal(HttpStatusCode.Unauthorized, rules.StatusCode);
    }

    [Fact]
    public async Task The_appointment_panel_lists_its_own_rows_oldest_first()
    {
        var (appointmentId, sentId, failedId) = await SeedHistoryAsync();

        var rows = await ReadAsync<List<AppointmentReminderDto>>(
            await Client.GetAsync($"/api/appointments/{appointmentId}/reminders"));

        Assert.Equal([sentId, failedId], rows.Select(row => row.Id));
        Assert.All(rows, row => Assert.Equal(appointmentId, row.AppointmentId));
        Assert.All(rows, row => Assert.Equal("Sara Haddad", row.CustomerName));
    }

    [Fact]
    public async Task The_receptionist_can_see_an_appointments_reminders()
    {
        var (appointmentId, _, _) = await SeedHistoryAsync();

        using var receptionist = AnonymousClient;
        await AuthenticateAsync(receptionist, ReceptionistEmail, ReceptionistPassword);

        var response = await receptionist.GetAsync($"/api/appointments/{appointmentId}/reminders");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rows = await ReadAsync<List<AppointmentReminderDto>>(response);
        Assert.Equal(2, rows.Count);
    }
}
