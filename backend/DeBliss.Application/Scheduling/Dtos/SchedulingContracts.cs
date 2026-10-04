namespace DeBliss.Application.Scheduling.Dtos;

/// <summary>One service line inside a booking request: which service, performed by whom.</summary>
public sealed record AppointmentServiceLineInput(Guid ServiceId, Guid StaffId);

/// <summary>
/// Payload for creating and editing an appointment. Exactly one of
/// <see cref="CustomerId"/> (an existing customer) or
/// <see cref="WalkInCustomerName"/> (a walk-in with no record) must be set.
/// The end instant is never sent: the server computes it from the snapshot
/// durations, so the stored window and the lines can never disagree.
/// </summary>
public sealed record SaveAppointmentRequest(
    Guid? CustomerId,
    string? WalkInCustomerName,
    string? WalkInCustomerPhone,
    DateTimeOffset? StartsAt,
    IReadOnlyList<AppointmentServiceLineInput>? Services,
    string? Notes);

/// <summary>
/// Partial schedule change from a calendar drag or resize. Any subset may be
/// sent; the server re-validates availability for the resulting window before
/// committing. <see cref="StaffId"/> reassigns every line to one member (the
/// horizontal drag); <see cref="DurationMinutes"/> is the new total, applied
/// to the last line (the resize); <see cref="StartsAt"/> moves the booking
/// (the vertical drag).
/// </summary>
public sealed record UpdateScheduleRequest(
    DateTimeOffset? StartsAt,
    Guid? StaffId,
    int? DurationMinutes);

/// <summary>Body of the status transition endpoint. Status is the AppointmentStatus name.</summary>
public sealed record SetAppointmentStatusRequest(string? Status, string? Reason);

/// <summary>One service line on a booked appointment, with its booking-time snapshots.</summary>
public sealed record AppointmentServiceLineDto(
    Guid ServiceId,
    string ServiceName,
    int DurationMinutes,
    decimal UnitPrice,
    Guid StaffId,
    string StaffFirstName,
    string StaffLastName,
    string? StaffColor);

/// <summary>One appointment as the calendar and the details panel render it.</summary>
public sealed record AppointmentDto(
    Guid Id,
    Guid? CustomerId,
    string CustomerName,
    string? CustomerPhone,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string Status,
    string Source,
    string? Notes,
    string? CancellationReason,
    IReadOnlyList<AppointmentServiceLineDto> Services,
    decimal TotalPrice,
    int TotalDurationMinutes,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

/// <summary>The range the calendar asks for. Normalised before it reaches the database.</summary>
public sealed record CalendarQuery(DateTimeOffset? From, DateTimeOffset? To)
{
    /// <summary>No range may span more than a month-plus padding, whatever the view.</summary>
    public const int MaximumRangeDays = 62;
}

/// <summary>
/// One staff column of the calendar: identity plus the recurring week the
/// grid shades by. Inactive members appear only while they still have
/// bookings in range, flagged so the grid can render their column as
/// historical.
/// </summary>
public sealed record CalendarStaffDto(
    Guid Id,
    string FirstName,
    string LastName,
    string? JobTitle,
    string? CalendarColor,
    int SortOrder,
    Staffing.Dtos.WeekScheduleDto WorkingHours,
    bool IsActive);

/// <summary>
/// A dated, non-bookable period on the grid: time off, a break or blocked
/// period (staff-specific or salon-wide when StaffId is null). Recurring
/// working hours are not markers — the client derives them from each
/// column's week pattern, so breaks are the gaps between its intervals.
/// Kind is the coarse "TimeOff" | "Blocked" discriminator; Type is the
/// specific enum name ("Holiday", "Break", "Meeting", …) so the grid can
/// style them differently; Label is the free-text reason when one was set.
/// </summary>
public sealed record CalendarMarkerDto(
    Guid Id,
    Guid? StaffId,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string Kind,
    string? Type,
    string? Label);

/// <summary>Everything one calendar range query renders: columns, bookings and non-bookable periods.</summary>
public sealed record CalendarDto(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<CalendarStaffDto> Staff,
    IReadOnlyList<AppointmentDto> Appointments,
    IReadOnlyList<CalendarMarkerDto> Markers);
