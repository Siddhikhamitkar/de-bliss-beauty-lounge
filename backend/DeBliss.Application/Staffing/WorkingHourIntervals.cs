using DeBliss.Application.Staffing.Dtos;
using DeBliss.Domain.Staffing;

namespace DeBliss.Application.Staffing;

/// <summary>
/// The single projection from stored working-hour rows to the intervals a client
/// sees. The weekly schedule and a day's attendance both describe a shift, so the
/// ordering and HH:mm rendering live here rather than being repeated.
/// </summary>
internal static class WorkingHourIntervals
{
    public static IReadOnlyList<WorkingIntervalDto> For(IEnumerable<StaffWorkingHour> rows) =>
        [.. rows
            .OrderBy(row => row.StartTime)
            .Select(row => WorkingIntervalDto.From(row.StartTime, row.EndTime))];

    public static IReadOnlyList<WorkingIntervalDto> For(IEnumerable<StaffWorkingHour> rows, DayOfWeek day) =>
        For(rows.Where(row => row.DayOfWeek == day));
}
