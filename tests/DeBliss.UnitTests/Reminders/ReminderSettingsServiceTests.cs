using DeBliss.Application.Common.Exceptions;
using DeBliss.Application.Notifications;
using DeBliss.Application.Notifications.Dtos;
using DeBliss.Domain.Notifications;

namespace DeBliss.UnitTests.Reminders;

/// <summary>
/// The owner-facing settings surface: the seeded rule set and its edits, the
/// connection status projection, and the operator test send that must go
/// through the exact provider path a real reminder uses.
/// </summary>
public sealed class ReminderSettingsServiceTests
{
    [Fact]
    public async Task The_default_rules_come_back_in_display_order()
    {
        using var harness = new RemindersHarness();

        var rules = await harness.Settings.GetRulesAsync();

        Assert.All(rules, rule => Assert.True(rule.IsEnabled));
        Assert.Equal(
            new[]
            {
                ("Confirmation", 0),
                ("Reminder", 1440),
                ("Reminder", 120),
                ("Cancellation", 0),
                ("Rescheduled", 0)
            },
            rules.Select(rule => (rule.ReminderType, rule.OffsetMinutes)).ToArray());
    }

    [Fact]
    public async Task A_rule_can_be_disabled_and_stays_disabled()
    {
        using var harness = new RemindersHarness();
        var target = await RuleAsync(harness, "Reminder", 1440);

        var updated = await harness.Settings.UpdateRuleAsync(
            target.Id, new UpdateReminderRuleRequest(IsEnabled: false, OffsetMinutes: null));

        Assert.False(updated.IsEnabled);
        Assert.Equal(1440, updated.OffsetMinutes);

        var reread = await harness.Settings.GetRulesAsync();
        Assert.False(reread.Single(rule => rule.Id == target.Id).IsEnabled);
    }

    [Fact]
    public async Task Retiming_a_notice_without_timing_is_rejected()
    {
        using var harness = new RemindersHarness();
        var cancellation = await RuleAsync(harness, "Cancellation", 0);

        var error = await Assert.ThrowsAsync<ValidationFailedException>(() =>
            harness.Settings.UpdateRuleAsync(cancellation.Id, new UpdateReminderRuleRequest(null, 60)));

        Assert.True(error.Errors.ContainsKey("offsetMinutes"));
    }

    [Fact]
    public async Task A_reminder_offset_must_stay_inside_the_30_day_window()
    {
        using var harness = new RemindersHarness();
        var reminder = await RuleAsync(harness, "Reminder", 1440);

        foreach (var invalid in new[] { 0, ReminderSettingsService.MaxOffsetMinutes + 1 })
        {
            var error = await Assert.ThrowsAsync<ValidationFailedException>(() =>
                harness.Settings.UpdateRuleAsync(reminder.Id, new UpdateReminderRuleRequest(null, invalid)));

            Assert.True(error.Errors.ContainsKey("offsetMinutes"));
        }
    }

    [Fact]
    public async Task Duplicating_the_other_reminders_timing_is_rejected()
    {
        using var harness = new RemindersHarness();
        var twoHours = await RuleAsync(harness, "Reminder", 120);

        var error = await Assert.ThrowsAsync<ValidationFailedException>(() =>
            harness.Settings.UpdateRuleAsync(twoHours.Id, new UpdateReminderRuleRequest(null, 1440)));

        Assert.True(error.Errors.ContainsKey("offsetMinutes"));
    }

    [Fact]
    public async Task An_unknown_rule_is_a_not_found()
    {
        using var harness = new RemindersHarness();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            harness.Settings.UpdateRuleAsync(Guid.NewGuid(), new UpdateReminderRuleRequest(true, null)));
    }

    [Fact]
    public async Task The_provider_status_reflects_the_connection()
    {
        using var harness = new RemindersHarness();

        var configured = await harness.Settings.GetProviderStatusAsync();

        Assert.True(configured.IsConfigured);
        Assert.Equal("WhatsApp", configured.Channel);
        Assert.Equal(harness.Provider.Description, configured.Description);

        harness.Provider.IsConfigured = false;

        var notConfigured = await harness.Settings.GetProviderStatusAsync();
        Assert.False(notConfigured.IsConfigured);
    }

    [Fact]
    public async Task A_test_message_goes_through_the_configured_provider_as_a_marked_sample()
    {
        using var harness = new RemindersHarness();

        var result = await harness.Settings.SendTestMessageAsync(new SendTestMessageRequest("+971 50 000 0001"));

        Assert.True(result.Success);
        Assert.Equal("SM0001", result.ProviderMessageId);
        Assert.Null(result.FailureReason);

        var message = Assert.Single(harness.Provider.SentMessages);
        Assert.Equal("+971500000001", message.RecipientE164);
        Assert.Equal("HXconfirmation", message.TemplateId);
        Assert.Equal("Test Message (ignore)", message.Variables[ReminderTemplateVariables.CustomerName]);
    }

    [Fact]
    public async Task A_test_message_is_refused_when_whatsapp_is_not_configured()
    {
        using var harness = new RemindersHarness();
        harness.Provider.IsConfigured = false;

        var result = await harness.Settings.SendTestMessageAsync(new SendTestMessageRequest("+971500000001"));

        Assert.False(result.Success);
        Assert.Equal(ReminderFailureReasons.ProviderNotConfigured, result.FailureReason);
        Assert.Equal(0, harness.Provider.SendCount);
    }

    [Fact]
    public async Task A_test_message_to_a_number_without_an_international_prefix_is_refused()
    {
        using var harness = new RemindersHarness();

        var result = await harness.Settings.SendTestMessageAsync(new SendTestMessageRequest("050 123 4567"));

        Assert.False(result.Success);
        Assert.Contains("E.164", result.FailureReason);
        Assert.Equal(0, harness.Provider.SendCount);
    }

    [Fact]
    public async Task A_test_message_is_refused_when_no_confirmation_template_is_configured()
    {
        using var harness = new RemindersHarness();
        harness.Provider.Templates[ReminderType.Confirmation] = null;

        var result = await harness.Settings.SendTestMessageAsync(new SendTestMessageRequest("+971500000001"));

        Assert.False(result.Success);
        Assert.Equal(ReminderFailureReasons.TemplateNotConfigured, result.FailureReason);
        Assert.Equal(0, harness.Provider.SendCount);
    }

    [Fact]
    public async Task A_throwing_provider_becomes_a_clean_test_failure()
    {
        using var harness = new RemindersHarness();
        harness.Provider.ThrowOnSend = true;

        var result = await harness.Settings.SendTestMessageAsync(new SendTestMessageRequest("+971500000001"));

        Assert.False(result.Success);
        Assert.Contains("provider call failed", result.FailureReason);
    }

    private static async Task<ReminderRuleDto> RuleAsync(
        RemindersHarness harness, string reminderType, int offsetMinutes)
    {
        var rules = await harness.Settings.GetRulesAsync();

        return rules.Single(rule =>
            rule.ReminderType == reminderType && rule.OffsetMinutes == offsetMinutes);
    }
}
