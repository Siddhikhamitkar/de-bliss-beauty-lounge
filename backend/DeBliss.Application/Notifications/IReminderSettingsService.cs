using DeBliss.Application.Notifications.Dtos;

namespace DeBliss.Application.Notifications;

/// <summary>
/// The reminder configuration surface: rule list and edits, WhatsApp
/// connection status, and the operator test message. Everything here is
/// owner-gated at the API; the service itself never exposes credentials.
/// </summary>
public interface IReminderSettingsService
{
    Task<IReadOnlyList<ReminderRuleDto>> GetRulesAsync(CancellationToken cancellationToken = default);

    Task<ReminderRuleDto> UpdateRuleAsync(
        Guid id,
        UpdateReminderRuleRequest request,
        CancellationToken cancellationToken = default);

    Task<ReminderProviderStatusDto> GetProviderStatusAsync(CancellationToken cancellationToken = default);

    Task<SendTestMessageResult> SendTestMessageAsync(
        SendTestMessageRequest request,
        CancellationToken cancellationToken = default);
}
