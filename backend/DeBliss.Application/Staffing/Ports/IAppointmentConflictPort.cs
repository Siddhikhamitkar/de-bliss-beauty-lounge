namespace DeBliss.Application.Staffing.Ports;

/// <summary>
/// The appointment module's side of the availability check: whether a staff
/// member already has a booking overlapping a window. Declared in Staffing
/// because the availability service consumes it; implemented by the
/// appointment repository in Infrastructure. The exclude parameter lets a
/// reschedule ignore the appointment being moved.
/// </summary>
public interface IAppointmentConflictPort
{
    /// <summary>
    /// True when an appointment overlapping [start, end) has at least one
    /// service line assigned to the staff member and still holds its slot.
    /// Cancelled, no-show and completed appointments free their slot again,
    /// so they never conflict; the exclude parameter lets a reschedule ignore
    /// the appointment being moved.
    /// </summary>
    Task<bool> HasConflictingAppointmentAsync(
        Guid staffId,
        DateTimeOffset start,
        DateTimeOffset end,
        Guid? excludeAppointmentId,
        CancellationToken ct = default);
}
