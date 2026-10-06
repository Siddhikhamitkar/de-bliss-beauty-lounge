using DeBliss.Application.Staffing.Dtos;

namespace DeBliss.Application.Staffing;

/// <summary>
/// The read side of a staff member's own mobile experience. Every method takes
/// the staff id resolved from the authenticated user's token — never one supplied
/// by the client — so one staff member can only ever see their own data.
/// </summary>
public interface IStaffMobileService
{
    /// <summary>The signed-in staff member's own profile.</summary>
    Task<StaffMobileProfileDto> GetProfileAsync(Guid staffId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The signed-in staff member's own bookings in the requested slice: today,
    /// the next month, or the last quarter.
    /// </summary>
    Task<IReadOnlyList<StaffAppointmentDto>> GetAppointmentsAsync(
        Guid staffId,
        StaffAppointmentScope scope,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The signed-in staff member's shift view, derived from the existing weekly
    /// working pattern and dated time off rather than from a separate shift record.
    /// </summary>
    Task<StaffScheduleDto> GetScheduleAsync(Guid staffId, CancellationToken cancellationToken = default);
}
