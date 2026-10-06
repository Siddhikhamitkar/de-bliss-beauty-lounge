using System.Globalization;

namespace DeBliss.Application.Staffing.Dtos;

/// <summary>One working interval in a day, as HH:mm local salon time.</summary>
public sealed record WorkingIntervalDto(string Start, string End)
{
    /// <summary>Renders stored shift times in the HH:mm form the schedule UI expects.</summary>
    public static WorkingIntervalDto From(TimeSpan start, TimeSpan end) => new(Format(start), Format(end));

    private static string Format(TimeSpan value)
    {
        var totalMinutes = (int)value.TotalMinutes;

        return string.Create(CultureInfo.InvariantCulture, $"{totalMinutes / 60:D2}:{totalMinutes % 60:D2}");
    }
}

/// <summary>The working pattern for one weekday. A day off carries no intervals.</summary>
public sealed record DayScheduleDto(
    string DayOfWeek,
    bool IsWorkingDay,
    IReadOnlyList<WorkingIntervalDto> Intervals);

/// <summary>The full recurring weekly schedule for one staff member.</summary>
public sealed record WeekScheduleDto(IReadOnlyList<DayScheduleDto> Days);

/// <summary>Payload that replaces a staff member's whole weekly schedule.</summary>
public sealed record SaveWeekScheduleRequest(IReadOnlyList<DayScheduleDto>? Days);
