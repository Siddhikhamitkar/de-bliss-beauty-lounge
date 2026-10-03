using DeBliss.Application.Staffing.Dtos;

namespace DeBliss.Application.Staffing;

/// <summary>
/// The recurring weekly working pattern and the dated time-off records of one
/// staff member. Working hours are stored as one row per interval, so a split
/// shift is simply two intervals on the same day and a break is the gap between
/// them. Time off is never physically deleted once it has started.
/// </summary>
public interface IStaffScheduleService
{
    /// <summary>The full week, Monday through Sunday; days off carry no intervals.</summary>
    Task<WeekScheduleDto> GetWeekScheduleAsync(Guid staffId, CancellationToken cancellationToken = default);

    /// <summary>Replaces the staff member's whole weekly schedule.</summary>
    Task<WeekScheduleDto> SaveWeekScheduleAsync(Guid staffId, SaveWeekScheduleRequest request, CancellationToken cancellationToken = default);

    /// <summary>All time off for the staff member, newest first.</summary>
    Task<IReadOnlyList<StaffTimeOffDto>> GetTimeOffAsync(Guid staffId, CancellationToken cancellationToken = default);

    Task<StaffTimeOffDto> CreateTimeOffAsync(Guid staffId, SaveStaffTimeOffRequest request, CancellationToken cancellationToken = default);

    Task<StaffTimeOffDto> UpdateTimeOffAsync(Guid staffId, Guid timeOffId, SaveStaffTimeOffRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a future time-off entry. Anything that has already started is
    /// kept on the record and refused with a conflict instead.
    /// </summary>
    Task DeleteTimeOffAsync(Guid staffId, Guid timeOffId, CancellationToken cancellationToken = default);
}
