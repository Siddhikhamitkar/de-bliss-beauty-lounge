namespace DeBliss.Application.Notifications;

/// <summary>
/// Reason strings recorded on reminder rows: why a row was cancelled or
/// skipped, or why the last send attempt failed. They are stored verbatim in
/// <c>FailureReason</c> and rendered in the history UI, so they are written
/// for salon staff — never containing phone numbers or credentials.
/// </summary>
public static class ReminderFailureReasons
{
    // The appointment lifecycle moved on before this row was sent.
    public const string AppointmentCancelled = "Cancelled because the appointment was cancelled.";
    public const string AppointmentRescheduled = "Cancelled because the appointment was rescheduled.";
    public const string AppointmentCompleted = "Cancelled because the appointment was completed.";
    public const string AppointmentNoShow = "Cancelled because the appointment was marked as a no-show.";
    public const string AppointmentDeleted = "Cancelled because the appointment was deleted.";
    public const string CustomerUnlinked = "Cancelled because the customer was removed from the appointment.";

    // Not sendable at dispatch time.
    public const string AppointmentStarted = "Skipped because the appointment had already started.";
    public const string CustomerNotOptedIn = "Skipped because the customer has not opted in to WhatsApp messages.";
    public const string CustomerOptedOut = "Skipped because the customer opted out of WhatsApp messages.";

    // Send failures.
    public const string MissingPhoneNumber = "The customer has no phone number.";
    public const string InvalidPhoneNumber = "The customer's phone number is not a valid international (E.164) number.";
    public const string TemplateNotConfigured = "No WhatsApp template is configured for this reminder type.";
    public const string CustomerRecordMissing = "The customer record no longer exists.";
    public const string ProviderNotConfigured = "WhatsApp sending is not configured.";
}
