using DeBliss.Domain.Common;

namespace DeBliss.Domain.Notifications;

/// <summary>
/// Owner-configured notification rule. Timed reminders (type Reminder) carry
/// their offset before the appointment start; the confirmation, cancellation
/// and reschedule notices are event-based and always use offset 0. Rules are
/// seeded with defaults per environment and are edited, not deleted.
/// </summary>
public class ReminderRule : AuditableEntity
{
    public ReminderType ReminderType { get; set; }

    /// <summary>Minutes before the appointment start a timed reminder fires. Always 0 for event-based types.</summary>
    public int OffsetMinutes { get; set; }

    public bool IsEnabled { get; set; } = true;
}
