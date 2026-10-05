using DeBliss.Domain.Notifications;

namespace DeBliss.Application.Notifications.Ports;

/// <summary>
/// One outbound message handed to a provider. The engine composes the template
/// variables and chooses the template id; the provider only knows how to
/// deliver the message on its channel, so replacing the WhatsApp provider never
/// touches the reminder engine.
/// </summary>
public sealed record NotificationMessage(
    NotificationChannel Channel,
    string RecipientE164,
    string TemplateId,
    IReadOnlyDictionary<string, string> Variables);

/// <summary>How a failed send must be treated by the retry policy.</summary>
public enum NotificationFailureKind
{
    None = 0,

    /// <summary>Worth retrying: timeouts, throttling, provider outages.</summary>
    Transient = 1,

    /// <summary>Retrying cannot help: invalid recipient, unknown template, rejected number.</summary>
    Permanent = 2
}

/// <summary>Structured outcome of one send attempt.</summary>
public sealed record NotificationSendResult(
    bool Success,
    string? ProviderMessageId,
    string? FailureReason,
    string? ProviderStatus,
    NotificationFailureKind FailureKind)
{
    public static NotificationSendResult Sent(string providerMessageId, string? providerStatus = null) =>
        new(true, providerMessageId, null, providerStatus, NotificationFailureKind.None);

    public static NotificationSendResult Transient(string reason, string? providerStatus = null) =>
        new(false, null, reason, providerStatus, NotificationFailureKind.Transient);

    public static NotificationSendResult Permanent(string reason, string? providerStatus = null) =>
        new(false, null, reason, providerStatus, NotificationFailureKind.Permanent);
}

/// <summary>
/// The delivery port the reminder engine talks to. Implementations translate a
/// message to a concrete channel (V1: Twilio WhatsApp); nothing above this port
/// references a provider SDK. Status reporting never exposes credentials.
/// </summary>
public interface INotificationProvider
{
    /// <summary>True when the provider has everything it needs to send.</summary>
    bool IsConfigured { get; }

    /// <summary>Human-readable status for the admin UI. Never contains secrets.</summary>
    string Description { get; }

    /// <summary>The provider's template id for a reminder type, or null when none is configured for it.</summary>
    string? ResolveTemplateId(ReminderType reminderType);

    /// <summary>
    /// Sends one message. Provider-side failures are reported through the
    /// result rather than thrown, so the engine can classify them.
    /// </summary>
    Task<NotificationSendResult> SendNotificationAsync(
        NotificationMessage message,
        CancellationToken cancellationToken = default);
}
