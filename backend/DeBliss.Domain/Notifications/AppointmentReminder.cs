using DeBliss.Domain.Common;
using DeBliss.Domain.Customers;
using DeBliss.Domain.Scheduling;

namespace DeBliss.Domain.Notifications;

/// <summary>
/// One appointment message the system intends to send, or has sent. The same
/// table doubles as the dispatch work queue: a row starts Pending, the worker
/// claims it, and it ends in Sent, Failed, Cancelled or Skipped. Rows are
/// historical records and are never deleted.
/// </summary>
public class AppointmentReminder : AuditableEntity
{
    public Guid AppointmentId { get; set; }

    public Appointment? Appointment { get; set; }

    /// <summary>
    /// Always set: reminders are only planned for appointments that have a
    /// customer record, because WhatsApp consent lives on the customer.
    /// </summary>
    public Guid CustomerId { get; set; }

    public Customer? Customer { get; set; }

    public ReminderType ReminderType { get; set; }

    public ReminderStatus Status { get; set; }

    public NotificationChannel Channel { get; set; }

    /// <summary>
    /// When the message is due. Timed reminders carry the appointment start
    /// minus the rule offset; lifecycle notices (confirmation, cancellation,
    /// reschedule) are due as soon as they are planned.
    /// </summary>
    public DateTimeOffset ScheduledAt { get; set; }

    public DateTimeOffset? SentAt { get; set; }

    public int AttemptCount { get; set; }

    public DateTimeOffset? LastAttemptAt { get; set; }

    /// <summary>The provider's receipt id, e.g. a Twilio message SID. Null until a send succeeded.</summary>
    public string? ProviderMessageId { get; set; }

    /// <summary>
    /// Why the row is Failed or Skipped, why a Cancelled row was dropped, or
    /// the latest transient failure while a retry is still owed. Null until
    /// the first attempt or lifecycle event, and cleared by a successful send.
    /// </summary>
    public string? FailureReason { get; set; }

    /// <summary>Earliest time a retry may run. Null means "due as soon as <see cref="ScheduledAt"/> passes".</summary>
    public DateTimeOffset? NextAttemptAt { get; set; }

    /// <summary>
    /// Which worker instance currently holds the row. Claims are taken over when
    /// <see cref="ClaimedAt"/> is older than the staleness window, so a crashed
    /// worker never strands a reminder.
    /// </summary>
    public string? ClaimedBy { get; set; }

    public DateTimeOffset? ClaimedAt { get; set; }
}
