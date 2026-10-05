namespace DeBliss.Domain.Notifications;

/// <summary>
/// The transactional appointment message a reminder row represents. Deliberately
/// limited to the four appointment lifecycle notices: marketing messages are not
/// part of the reminder system.
/// </summary>
public enum ReminderType
{
    /// <summary>Sent when an appointment becomes confirmed.</summary>
    Confirmation = 0,

    /// <summary>A timed nudge before the appointment. One row per configured rule.</summary>
    Reminder = 1,

    /// <summary>Sent when a confirmed appointment is cancelled.</summary>
    Cancellation = 2,

    /// <summary>Sent when a confirmed appointment's time changes.</summary>
    Rescheduled = 3
}

/// <summary>
/// Lifecycle of one reminder row. Rows are historical records and are never
/// deleted; a row that will not be sent is Cancelled (superseded by the
/// appointment lifecycle) or Skipped (not sendable — for example the customer
/// never opted in).
/// </summary>
public enum ReminderStatus
{
    Pending = 0,
    Sent = 1,
    Failed = 2,
    Cancelled = 3,
    Skipped = 4
}

/// <summary>
/// Delivery channel. V1 only sends WhatsApp; the provider abstraction keeps the
/// dispatcher free of channel-specific details so more values can be added later.
/// </summary>
public enum NotificationChannel
{
    WhatsApp = 0
}

/// <summary>
/// Customer's consent to receive appointment notifications on WhatsApp. Unknown
/// is the default and never counts as consent: the dispatcher only sends when
/// the status is explicitly OptedIn.
/// </summary>
public enum WhatsAppOptInStatus
{
    Unknown = 0,
    OptedIn = 1,
    OptedOut = 2
}
