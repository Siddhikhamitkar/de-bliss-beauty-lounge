namespace DeBliss.Application.Scheduling.Dtos;

/// <summary>
/// A customer website booking: exactly one service, an optional staff
/// preference (absent means "any available", resolved on the server), and a
/// start time. The customer is never sent — it comes from the token — and no
/// price is sent, because the server snapshots the authoritative one.
/// </summary>
public sealed record CustomerBookingRequest(
    Guid ServiceId,
    Guid? StaffId,
    DateTimeOffset? StartsAt);

/// <summary>The confirmation a customer sees immediately after booking.</summary>
public sealed record CustomerBookingConfirmationDto(
    Guid AppointmentId,
    string ServiceName,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string Status,
    string? StaffDisplayName,
    decimal Price,
    string Source);

/// <summary>One service line on the customer's own appointment history.</summary>
public sealed record CustomerAppointmentServiceDto(
    string ServiceName,
    int DurationMinutes,
    decimal UnitPrice,
    string? StaffDisplayName);

/// <summary>
/// One of the customer's own appointments. Staff internal notes, cancellation
/// reasons and any other customer's data are deliberately absent.
/// </summary>
public sealed record CustomerAppointmentDto(
    Guid Id,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string Status,
    IReadOnlyList<CustomerAppointmentServiceDto> Services,
    decimal TotalPrice,
    int TotalDurationMinutes);
