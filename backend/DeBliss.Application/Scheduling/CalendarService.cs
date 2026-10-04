using System.Globalization;
using DeBliss.Application.Common.Exceptions;
using DeBliss.Application.Scheduling.Dtos;
using DeBliss.Application.Scheduling.Ports;
using DeBliss.Application.Staffing.Dtos;
using DeBliss.Application.Staffing.Ports;
using DeBliss.Domain.Staffing;

namespace DeBliss.Application.Scheduling;

/// <summary>
/// Composes one calendar range query. Columns are every active staff member
/// plus anyone with a booking in range, so a deactivated member's remaining
/// appointments never vanish from the grid. Working hours travel as the
/// recurring week pattern; one-off absences and blocked periods travel as
/// dated markers, deduplicated so a salon-wide block is not repeated per
/// column. Read-only: nothing here mutates or saves.
/// </summary>
public sealed class CalendarService(
    IAppointmentRepository appointments,
    IStaffRepository staff,
    IScheduleRepository schedule) : ICalendarService
{
    private static readonly DayOfWeek[] WeekDays =
    [
        DayOfWeek.Monday,
        DayOfWeek.Tuesday,
        DayOfWeek.Wednesday,
        DayOfWeek.Thursday,
        DayOfWeek.Friday,
        DayOfWeek.Saturday,
        DayOfWeek.Sunday
    ];

    public async Task<CalendarDto> GetAsync(CalendarQuery query, CancellationToken cancellationToken = default)
    {
        var errors = new ValidationErrorBuilder();

        if (query.From is null)
        {
            errors.Add("from", "A start date is required.");
        }

        if (query.To is null)
        {
            errors.Add("to", "An end date is required.");
        }

        errors.ThrowIfInvalid();

        var from = query.From!.Value;
        var to = query.To!.Value;

        if (to <= from)
        {
            errors.Add("to", "The end date must be after the start date.");
        }
        else if (to - from > TimeSpan.FromDays(CalendarQuery.MaximumRangeDays))
        {
            errors.Add("to", $"The calendar range cannot span more than {CalendarQuery.MaximumRangeDays} days.");
        }

        errors.ThrowIfInvalid();

        var columns = await LoadColumnsAsync(from, to, cancellationToken);
        var staffDtos = new List<CalendarStaffDto>(columns.Count);

        foreach (var member in columns)
        {
            var hours = await schedule.GetWorkingHoursAsync(member.Id, cancellationToken);

            staffDtos.Add(new CalendarStaffDto(
                member.Id,
                member.FirstName,
                member.LastName,
                member.JobTitle,
                member.CalendarColor,
                member.SortOrder,
                MapWeek(hours),
                member.IsActive));
        }

        var markers = await LoadMarkersAsync(columns, from, to, cancellationToken);

        var booked = await appointments.GetInRangeAsync(from, to, cancellationToken);

        return new CalendarDto(
            from,
            to,
            staffDtos,
            booked.Select(AppointmentMapping.Map).ToList(),
            markers);
    }

    /// <summary>
    /// Active staff first, then booked-but-inactive members appended, each
    /// group in display order, so history keeps its column at the far side
    /// of the grid.
    /// </summary>
    private async Task<IReadOnlyList<Staff>> LoadColumnsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var active = await staff.GetActiveAsync(cancellationToken);
        var bookedIds = await appointments.GetStaffIdsInRangeAsync(from, to, cancellationToken);
        var extraIds = bookedIds.Except(active.Select(member => member.Id)).ToArray();

        if (extraIds.Length == 0)
        {
            return active;
        }

        return [.. active, .. await staff.GetByIdsAsync(extraIds, cancellationToken)];
    }

    /// <summary>
    /// Time off is per member; blocked periods include salon-wide rows, which
    /// every member's query returns, so the final list is deduplicated by id.
    /// </summary>
    private async Task<IReadOnlyList<CalendarMarkerDto>> LoadMarkersAsync(
        IReadOnlyList<Staff> columns,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var markers = new List<CalendarMarkerDto>();

        foreach (var member in columns)
        {
            var timeOff = await schedule.GetOverlappingTimeOffAsync(member.Id, from, to, cancellationToken);

            markers.AddRange(timeOff.Select(entry => new CalendarMarkerDto(
                entry.Id,
                entry.StaffId,
                entry.StartsAt,
                entry.EndsAt,
                Kind: "TimeOff",
                Type: Enum.GetName(entry.Type),
                Label: string.IsNullOrWhiteSpace(entry.Reason) ? null : entry.Reason.Trim())));

            var blocked = await schedule.GetOverlappingBlockedTimesAsync(member.Id, from, to, cancellationToken);

            markers.AddRange(blocked.Select(block => new CalendarMarkerDto(
                block.Id,
                block.StaffId,
                block.StartsAt,
                block.EndsAt,
                Kind: "Blocked",
                Type: Enum.GetName(block.Type),
                Label: string.IsNullOrWhiteSpace(block.Reason) ? null : block.Reason.Trim())));
        }

        return markers
            .GroupBy(marker => marker.Id)
            .Select(group => group.First())
            .ToList();
    }

    /// <summary>Projects working-hour rows into the same week shape the staff schedule screens use.</summary>
    private static WeekScheduleDto MapWeek(IEnumerable<StaffWorkingHour> rows)
    {
        var byDay = rows
            .GroupBy(row => row.DayOfWeek)
            .ToDictionary(group => group.Key, group => group.ToList());

        var days = new List<DayScheduleDto>(WeekDays.Length);

        foreach (var weekDay in WeekDays)
        {
            if (byDay.TryGetValue(weekDay, out var dayRows))
            {
                days.Add(new DayScheduleDto(
                    Enum.GetName(weekDay)!,
                    IsWorkingDay: true,
                    [.. dayRows
                        .OrderBy(row => row.StartTime)
                        .Select(row => new WorkingIntervalDto(FormatTime(row.StartTime), FormatTime(row.EndTime)))]));
            }
            else
            {
                days.Add(new DayScheduleDto(Enum.GetName(weekDay)!, IsWorkingDay: false, []));
            }
        }

        return new WeekScheduleDto(days);
    }

    private static string FormatTime(TimeSpan value)
    {
        var totalMinutes = (int)value.TotalMinutes;

        return string.Create(CultureInfo.InvariantCulture, $"{totalMinutes / 60:D2}:{totalMinutes % 60:D2}");
    }
}
