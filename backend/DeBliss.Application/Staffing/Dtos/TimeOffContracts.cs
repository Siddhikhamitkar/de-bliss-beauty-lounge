namespace DeBliss.Application.Staffing.Dtos;

/// <summary>A single time-off record as it appears on a staff profile.</summary>
public sealed record StaffTimeOffDto(
    Guid Id,
    Guid StaffId,
    string Type,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string? Reason,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

/// <summary>
/// Type is the <see cref="Domain.Staffing.TimeOffType"/> name so the wire format
/// stays stable regardless of how the enum is reordered; invalid names are
/// rejected with a field error instead of a framework 400.
/// </summary>
public sealed record SaveStaffTimeOffRequest(
    string? Type,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string? Reason);
