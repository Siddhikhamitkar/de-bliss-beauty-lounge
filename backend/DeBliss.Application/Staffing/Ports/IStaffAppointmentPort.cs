using DeBliss.Domain.Scheduling;

namespace DeBliss.Application.Staffing.Ports;

/// <summary>
/// The scheduling module's answer to "which bookings is this staff member on?",
/// so the staffing module never queries appointments itself. Mirrors
/// <see cref="IAppointmentConflictPort"/>, which the same repository implements.
/// </summary>
public interface IStaffAppointmentPort
{
    /// <summary>
    /// Non-deleted appointments carrying at least one service line for this staff
    /// member and starting inside [from, to), soonest first, read-only, with
    /// lines, customer and bundle loaded.
    /// </summary>
    Task<IReadOnlyList<Appointment>> GetForStaffInRangeAsync(
        Guid staffId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct = default);
}
