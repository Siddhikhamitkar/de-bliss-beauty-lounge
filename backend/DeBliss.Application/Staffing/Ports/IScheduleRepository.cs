using DeBliss.Domain.Scheduling;
using DeBliss.Domain.Staffing;

namespace DeBliss.Application.Staffing.Ports;

/// <summary>
/// Working-hour and time-off persistence for the staffing module. Working hours
/// are read per staff member; writes go through the tracked staff entity, so
/// there is no separate mutation API here.
/// </summary>
public interface IScheduleRepository
{
    /// <summary>All working-hour rows for the staff member, tracked.</summary>
    Task<IReadOnlyList<StaffWorkingHour>> GetWorkingHoursAsync(Guid staffId, CancellationToken ct = default);

    /// <summary>All time-off rows for the staff member, newest first, tracked.</summary>
    Task<IReadOnlyList<StaffTimeOff>> GetTimeOffAsync(Guid staffId, CancellationToken ct = default);

    /// <summary>Single time-off row, tracked, or null.</summary>
    Task<StaffTimeOff?> GetTimeOffByIdAsync(Guid id, CancellationToken ct = default);

    void AddTimeOff(StaffTimeOff timeOff);

    void RemoveTimeOff(StaffTimeOff timeOff);

    /// <summary>Time-off rows overlapping [start, end) for the staff member.</summary>
    Task<IReadOnlyList<StaffTimeOff>> GetOverlappingTimeOffAsync(Guid staffId, DateTimeOffset start, DateTimeOffset end, CancellationToken ct = default);

    /// <summary>Blocked periods that apply to this staff member (their own or salon-wide).</summary>
    Task<IReadOnlyList<BlockedTime>> GetOverlappingBlockedTimesAsync(Guid staffId, DateTimeOffset start, DateTimeOffset end, CancellationToken ct = default);
}
