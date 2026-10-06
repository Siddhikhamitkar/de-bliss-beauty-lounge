namespace DeBliss.Application.Staffing.Dtos;

/// <summary>
/// One clock-in/clock-out pair for a work date, with the shift that was scheduled
/// for that weekday alongside it so the owner's Staff → Attendance view can
/// compare planned against actual without a second call. Timestamps only: no
/// worked-hours total is derived or stored, because that belongs to the later
/// compensation phase.
/// </summary>
public sealed record StaffAttendanceDto(
    Guid Id,
    Guid StaffId,
    DateOnly WorkDate,
    DateTimeOffset CheckInAt,
    DateTimeOffset? CheckOutAt,
    string Status,
    bool IsScheduledWorkingDay,
    IReadOnlyList<WorkingIntervalDto> ScheduledIntervals,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

/// <summary>
/// The date range an attendance history is asked for. Both ends are optional —
/// an unspecified end defaults to the last <see cref="DefaultRangeDays"/> days
/// ending today — and an explicit range wider than
/// <see cref="MaximumRangeDays"/> is refused with a field error rather than
/// silently truncated.
/// </summary>
public sealed record AttendanceQuery(DateOnly? From, DateOnly? To)
{
    public const int DefaultRangeDays = 90;

    public const int MaximumRangeDays = 366;
}
