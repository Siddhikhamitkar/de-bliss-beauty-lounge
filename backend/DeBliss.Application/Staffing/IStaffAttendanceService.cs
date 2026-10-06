using DeBliss.Application.Staffing.Dtos;

namespace DeBliss.Application.Staffing;

/// <summary>
/// Clock-in/clock-out for one staff member against the existing Staff record.
/// Both timestamps come from the server clock, never from the caller, and at most
/// one record exists per staff member per work date.
/// </summary>
public interface IStaffAttendanceService
{
    /// <summary>Opens today's record, or refuses when the day is already open or already closed.</summary>
    Task<StaffAttendanceDto> CheckInAsync(Guid staffId, CancellationToken cancellationToken = default);

    /// <summary>Closes today's record, or refuses when there is no open check-in.</summary>
    Task<StaffAttendanceDto> CheckOutAsync(Guid staffId, CancellationToken cancellationToken = default);

    /// <summary>Today's record for the staff member, or null when they have not clocked in.</summary>
    Task<StaffAttendanceDto?> GetTodayAsync(Guid staffId, CancellationToken cancellationToken = default);

    /// <summary>Attendance history newest work date first, each row carrying the shift planned for that weekday.</summary>
    Task<IReadOnlyList<StaffAttendanceDto>> GetHistoryAsync(Guid staffId, AttendanceQuery query, CancellationToken cancellationToken = default);
}
