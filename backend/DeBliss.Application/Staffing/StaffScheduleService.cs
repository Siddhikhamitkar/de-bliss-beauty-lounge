using System.Globalization;
using System.Text.RegularExpressions;
using DeBliss.Application.Common.Exceptions;
using DeBliss.Application.Common.Interfaces;
using DeBliss.Application.Staffing.Dtos;
using DeBliss.Application.Staffing.Ports;
using DeBliss.Domain.Staffing;

namespace DeBliss.Application.Staffing;

public sealed class StaffScheduleService(
    IStaffRepository staff,
    IScheduleRepository schedule,
    TimeProvider timeProvider,
    IUnitOfWork unitOfWork) : IStaffScheduleService
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

    private static readonly Regex TimePattern = new(
        "^(2[0-4]|[01]?[0-9]):([0-5][0-9])$",
        RegexOptions.None,
        TimeSpan.FromMilliseconds(100));

    public async Task<WeekScheduleDto> GetWeekScheduleAsync(
        Guid staffId,
        CancellationToken cancellationToken = default)
    {
        var member = await LoadForScheduleAsync(staffId, cancellationToken);

        return MapWeek(member.WorkingHours);
    }

    /// <summary>
    /// Replaces the whole weekly pattern. One row is stored per interval, so a
    /// working day with a mid-day break is two intervals and the break is simply
    /// the gap between them; a non-working day stores no rows at all.
    /// </summary>
    public async Task<WeekScheduleDto> SaveWeekScheduleAsync(
        Guid staffId,
        SaveWeekScheduleRequest request,
        CancellationToken cancellationToken = default)
    {
        var member = await LoadForScheduleAsync(staffId, cancellationToken);

        var validated = ValidateWeek(request);

        foreach (var row in member.WorkingHours.ToList())
        {
            member.WorkingHours.Remove(row);
        }

        foreach (var day in validated.Days)
        {
            foreach (var interval in day.Intervals)
            {
                member.WorkingHours.Add(new StaffWorkingHour
                {
                    Staff = member,
                    DayOfWeek = day.Day,
                    StartTime = interval.Start,
                    EndTime = interval.End,
                    IsWorkingDay = true
                });
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return MapWeek(member.WorkingHours);
    }

    public async Task<IReadOnlyList<StaffTimeOffDto>> GetTimeOffAsync(
        Guid staffId,
        CancellationToken cancellationToken = default)
    {
        await LoadForScheduleAsync(staffId, cancellationToken);

        var entries = await schedule.GetTimeOffAsync(staffId, cancellationToken);

        return [.. entries.Select(Map)];
    }

    public async Task<StaffTimeOffDto> CreateTimeOffAsync(
        Guid staffId,
        SaveStaffTimeOffRequest request,
        CancellationToken cancellationToken = default)
    {
        var member = await LoadForScheduleAsync(staffId, cancellationToken);

        var validated = ValidateTimeOff(request);

        var entry = new StaffTimeOff
        {
            Staff = member,
            Type = validated.Type,
            StartsAt = request.StartsAt,
            EndsAt = request.EndsAt,
            Reason = validated.Reason
        };

        schedule.AddTimeOff(entry);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Map(entry);
    }

    public async Task<StaffTimeOffDto> UpdateTimeOffAsync(
        Guid staffId,
        Guid timeOffId,
        SaveStaffTimeOffRequest request,
        CancellationToken cancellationToken = default)
    {
        await LoadForScheduleAsync(staffId, cancellationToken);

        var entry = await LoadTimeOffAsync(staffId, timeOffId, cancellationToken);

        var validated = ValidateTimeOff(request);

        entry.Type = validated.Type;
        entry.StartsAt = request.StartsAt;
        entry.EndsAt = request.EndsAt;
        entry.Reason = validated.Reason;

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Map(entry);
    }

    public async Task DeleteTimeOffAsync(
        Guid staffId,
        Guid timeOffId,
        CancellationToken cancellationToken = default)
    {
        await LoadForScheduleAsync(staffId, cancellationToken);

        var entry = await LoadTimeOffAsync(staffId, timeOffId, cancellationToken);

        if (entry.StartsAt <= timeProvider.GetUtcNow())
        {
            throw new ConflictException(
                "Time off that has already started is part of the historical record and cannot be deleted.");
        }

        schedule.RemoveTimeOff(entry);

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task<Staff> LoadForScheduleAsync(Guid staffId, CancellationToken cancellationToken)
    {
        return await staff.GetByIdForScheduleAsync(staffId, cancellationToken)
            ?? throw new NotFoundException($"Staff member '{staffId}' was not found.");
    }

    private async Task<StaffTimeOff> LoadTimeOffAsync(Guid staffId, Guid timeOffId, CancellationToken cancellationToken)
    {
        var entry = await schedule.GetTimeOffByIdAsync(timeOffId, cancellationToken);

        if (entry is null || entry.StaffId != staffId)
        {
            throw new NotFoundException($"Time-off entry '{timeOffId}' was not found for this staff member.");
        }

        return entry;
    }

    private static ValidatedWeek ValidateWeek(SaveWeekScheduleRequest request)
    {
        var errors = new ValidationErrorBuilder();

        if (request.Days is null || request.Days.Count == 0)
        {
            errors.Add("days", "Provide the full week schedule.");

            errors.ThrowIfInvalid();

            return new ValidatedWeek([]);
        }

        var seen = new HashSet<DayOfWeek>();
        var days = new List<ValidatedDay>(request.Days.Count);

        foreach (var day in request.Days)
        {
            var dayName = day.DayOfWeek?.Trim() ?? string.Empty;

            if (!TryParseDay(dayName, out var parsedDay))
            {
                errors.Add("days", $"'{dayName}' is not a day of the week.");
                continue;
            }

            if (!seen.Add(parsedDay))
            {
                errors.Add("days", $"{dayName} is listed more than once.");
                continue;
            }

            var intervals = day.Intervals ?? [];
            var dayLabel = dayName;

            if (!day.IsWorkingDay)
            {
                if (intervals.Count > 0)
                {
                    errors.Add("days", $"{dayLabel} is a non-working day and cannot have time intervals.");
                }

                days.Add(new ValidatedDay(parsedDay, []));
                continue;
            }

            if (intervals.Count == 0)
            {
                errors.Add("days", $"Add at least one working interval for {dayLabel}, or mark the day as off.");
                days.Add(new ValidatedDay(parsedDay, []));
                continue;
            }

            var parsed = new List<ValidatedInterval>(intervals.Count);
            var dayHasError = false;

            foreach (var interval in intervals)
            {
                if (!TryParseTime(interval.Start, out var start) || !TryParseTime(interval.End, out var end))
                {
                    errors.Add("days", $"{dayLabel}: use HH:mm times between 00:00 and 24:00.");
                    dayHasError = true;
                    continue;
                }

                if (end <= start)
                {
                    errors.Add("days", $"{dayLabel}: end time must be after the start time.");
                    dayHasError = true;
                    continue;
                }

                parsed.Add(new ValidatedInterval(start, end));
            }

            if (!dayHasError)
            {
                var ordered = parsed.OrderBy(interval => interval.Start).ToList();

                for (var index = 1; index < ordered.Count; index++)
                {
                    if (ordered[index].Start < ordered[index - 1].End)
                    {
                        errors.Add("days", $"{dayLabel}: working intervals cannot overlap.");
                        break;
                    }
                }
            }

            days.Add(new ValidatedDay(parsedDay, [.. parsed.OrderBy(interval => interval.Start)]));
        }

        errors.ThrowIfInvalid();

        return new ValidatedWeek(days);
    }

    private static bool TryParseDay(string name, out DayOfWeek day)
    {
        day = default;

        return Enum.TryParse(name, ignoreCase: true, out day)
            && Enum.GetName(day) is { } canonical
            && canonical.Equals(name, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseTime(string? text, out TimeSpan value)
    {
        value = default;

        if (text is null)
        {
            return false;
        }

        var match = TimePattern.Match(text.Trim());

        if (!match.Success)
        {
            return false;
        }

        var hours = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var minutes = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);

        if (hours == 24 && minutes != 0)
        {
            return false;
        }

        value = new TimeSpan(hours, minutes, 0);

        return true;
    }

    private static ValidatedTimeOff ValidateTimeOff(SaveStaffTimeOffRequest request)
    {
        var errors = new ValidationErrorBuilder();

        TimeOffType? type = null;

        if (string.IsNullOrWhiteSpace(request.Type))
        {
            errors.Add("type", "Choose a time-off type.");
        }
        else if (!TryParseTimeOffType(request.Type, out var parsed))
        {
            errors.Add("type", $"'{request.Type.Trim()}' is not a recognised time-off type.");
        }
        else
        {
            type = parsed;
        }

        if (request.EndsAt <= request.StartsAt)
        {
            errors.Add("endsAt", "End must be after the start.");
        }

        var reason = request.Reason?.Trim();

        if (request.Reason?.Length > StaffingLimits.TimeOffReasonMaxLength)
        {
            errors.Add("reason", $"Reason must be {StaffingLimits.TimeOffReasonMaxLength} characters or fewer.");
        }

        errors.ThrowIfInvalid();

        return new ValidatedTimeOff(type!.Value, string.IsNullOrEmpty(reason) ? null : reason);
    }

    private static bool TryParseTimeOffType(string name, out TimeOffType type)
    {
        type = default;

        return Enum.TryParse(name, ignoreCase: true, out type)
            && Enum.GetName(type) is { } canonical
            && canonical.Equals(name, StringComparison.OrdinalIgnoreCase);
    }

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

    private static StaffTimeOffDto Map(StaffTimeOff entry) => new(
        entry.Id,
        entry.StaffId,
        Enum.GetName(entry.Type)!,
        entry.StartsAt,
        entry.EndsAt,
        entry.Reason,
        entry.CreatedAt,
        entry.UpdatedAt);

    private sealed record ValidatedInterval(TimeSpan Start, TimeSpan End);

    private sealed record ValidatedDay(DayOfWeek Day, IReadOnlyList<ValidatedInterval> Intervals);

    private sealed record ValidatedWeek(IReadOnlyList<ValidatedDay> Days);

    private sealed record ValidatedTimeOff(TimeOffType Type, string? Reason);
}
