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

/// <summary>
/// The confirmation a customer sees immediately after booking. The VAT quartet
/// (<paramref name="TaxRate"/>, <paramref name="PricesIncludeTax"/>,
/// <paramref name="TaxAmount"/>, <paramref name="TotalPayable"/>) is computed
/// by the application from the salon's settings with the same
/// <c>TaxMath</c> the checkout charges with, so what the customer confirms is
/// exactly what an invoice for this visit would total.
/// </summary>
public sealed record CustomerBookingConfirmationDto(
    Guid AppointmentId,
    string ServiceName,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string Status,
    string? StaffDisplayName,
    decimal Price,
    decimal TaxRate,
    bool PricesIncludeTax,
    decimal TaxAmount,
    decimal TotalPayable,
    string Source);

/// <summary>One service line on the customer's own appointment history.</summary>
public sealed record CustomerAppointmentServiceDto(
    string ServiceName,
    int DurationMinutes,
    decimal UnitPrice,
    string? StaffDisplayName);

/// <summary>
/// One of the customer's own appointments. Staff internal notes, cancellation
/// reasons and any other customer's data are deliberately absent. The VAT
/// fields mirror <see cref="CustomerBookingConfirmationDto"/> and are computed
/// from the salon's settings, not from the appointment record — nothing here
/// claims the visit is paid.
/// </summary>
public sealed record CustomerAppointmentDto(
    Guid Id,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string Status,
    IReadOnlyList<CustomerAppointmentServiceDto> Services,
    decimal TotalPrice,
    decimal TaxRate,
    bool PricesIncludeTax,
    decimal TaxAmount,
    decimal TotalPayable,
    int TotalDurationMinutes);
