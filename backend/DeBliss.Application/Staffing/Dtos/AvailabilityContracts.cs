namespace DeBliss.Application.Staffing.Dtos;

/// <summary>Answer to a single "can this staff member take this slot?" question.</summary>
public sealed record StaffAvailabilityCheckDto(
    Guid StaffId,
    DateTimeOffset Start,
    DateTimeOffset End,
    bool IsAvailable);
