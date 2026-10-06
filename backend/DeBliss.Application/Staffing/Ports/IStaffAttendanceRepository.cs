using DeBliss.Domain.Staffing;

namespace DeBliss.Application.Staffing.Ports;

/// <summary>
/// Attendance persistence. Both reads are keyed the way the unique
/// (StaffId, WorkDate) index is, so a day is fetched by one seek and a history by
/// one bounded range scan. Writes go through the unit of work like every other
/// module.
/// </summary>
public interface IStaffAttendanceRepository
{
    /// <summary>The staff member's attendance row for one work date, tracked, or null.</summary>
    Task<StaffAttendance?> GetForDateAsync(Guid staffId, DateOnly workDate, CancellationToken ct = default);

    /// <summary>Attendance rows with a work date inside [from, to], newest work date first, read-only.</summary>
    Task<IReadOnlyList<StaffAttendance>> GetInRangeAsync(Guid staffId, DateOnly from, DateOnly to, CancellationToken ct = default);

    void Add(StaffAttendance attendance);
}
