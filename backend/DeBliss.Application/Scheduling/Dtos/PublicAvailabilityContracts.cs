namespace DeBliss.Application.Scheduling.Dtos;

/// <summary>
/// One bookable start time for the public day view, with the staff members who
/// can take it. Only professional identity is exposed — no contact details.
/// </summary>
public sealed record PublicStaffOptionDto(Guid StaffId, string DisplayName);

public sealed record PublicAvailabilitySlotDto(
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    IReadOnlyList<PublicStaffOptionDto> Staff);

/// <summary>Every bookable slot for one service on one salon-local day, in salon-local wall-clock terms.</summary>
public sealed record PublicAvailabilityDto(
    Guid ServiceId,
    DateOnly Date,
    IReadOnlyList<PublicAvailabilitySlotDto> Slots);
