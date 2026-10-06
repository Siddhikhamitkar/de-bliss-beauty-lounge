namespace DeBliss.Application.Staffing.Dtos;

/// <summary>Which slice of the signed-in staff member's own bookings to return.</summary>
public enum StaffAppointmentScope
{
    Today = 0,
    Upcoming = 1,
    History = 2
}

/// <summary>
/// The signed-in staff member's own profile. Deliberately narrower than the
/// owner-side <see cref="StaffDto"/>: sort order, calendar colour and the service
/// assignment list are configuration the team member has no use for, and nothing
/// about any other staff member appears here.
/// </summary>
public sealed record StaffMobileProfileDto(
    Guid StaffId,
    string FirstName,
    string LastName,
    string? Email,
    string? PhoneNumber,
    string? JobTitle,
    bool IsActive);

/// <summary>
/// One of the signed-in staff member's own service lines on an appointment: the
/// booking-time snapshot name and duration plus the operational note.
/// <see cref="Scheduling.Dtos.AppointmentServiceLineDto"/> is deliberately not
/// reused — it carries the unit price and the colleague assigned to the line.
/// </summary>
public sealed record StaffAppointmentServiceDto(
    Guid ServiceId,
    string ServiceName,
    int DurationMinutes,
    string? Notes);

/// <summary>
/// One appointment the signed-in staff member is booked onto. <see cref="Services"/>
/// holds only the lines they personally perform, so a shared booking never
/// reveals a colleague's work, and nothing financial is carried: no prices, no
/// customer contact details.
/// </summary>
public sealed record StaffAppointmentDto(
    Guid Id,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string Status,
    string CustomerName,
    string? Notes,
    int DurationMinutes,
    Guid? ServiceBundleId,
    string? BundleName,
    IReadOnlyList<StaffAppointmentServiceDto> Services);

/// <summary>
/// One calendar day of the staff member's own schedule, derived from the
/// EXISTING weekly working pattern with the dated time off that covers the day
/// listed alongside. <see cref="IsWorkingDay"/> stays whatever the recurring
/// pattern says — a day off is expressed by <see cref="TimeOff"/>, never by
/// rewriting the pattern the owner maintains.
/// </summary>
public sealed record StaffShiftDayDto(
    DateOnly Date,
    string DayOfWeek,
    bool IsWorkingDay,
    IReadOnlyList<WorkingIntervalDto> Intervals,
    IReadOnlyList<StaffTimeOffDto> TimeOff);

/// <summary>
/// The schedule screen in one response: today, the days that follow it, and the
/// recurring weekly pattern those days are derived from. There is no separate
/// shift record — see <see cref="StaffShiftDayDto"/>.
/// </summary>
public sealed record StaffScheduleDto(
    DateOnly Today,
    IReadOnlyList<StaffShiftDayDto> Days,
    WeekScheduleDto WeeklyPattern);
