using DeBliss.Domain.Common;

namespace DeBliss.Domain.Staffing;

/// <summary>
/// A dated exception to a staff member's working pattern. Stored separately from
/// <see cref="StaffWorkingHour"/> so recurring hours and one-off absences never
/// overwrite each other.
/// </summary>
public class StaffTimeOff : AuditableEntity
{
    public Guid StaffId { get; set; }

    public Staff Staff { get; set; } = null!;

    public DateTimeOffset StartsAt { get; set; }

    public DateTimeOffset EndsAt { get; set; }

    public TimeOffType Type { get; set; }

    public string? Reason { get; set; }
}
