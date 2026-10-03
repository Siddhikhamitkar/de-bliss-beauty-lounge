namespace DeBliss.Application.Staffing.Dtos;

/// <summary>One working interval in a day, as HH:mm local salon time.</summary>
public sealed record WorkingIntervalDto(string Start, string End);

/// <summary>The working pattern for one weekday. A day off carries no intervals.</summary>
public sealed record DayScheduleDto(
    string DayOfWeek,
    bool IsWorkingDay,
    IReadOnlyList<WorkingIntervalDto> Intervals);

/// <summary>The full recurring weekly schedule for one staff member.</summary>
public sealed record WeekScheduleDto(IReadOnlyList<DayScheduleDto> Days);

/// <summary>Payload that replaces a staff member's whole weekly schedule.</summary>
public sealed record SaveWeekScheduleRequest(IReadOnlyList<DayScheduleDto>? Days);
