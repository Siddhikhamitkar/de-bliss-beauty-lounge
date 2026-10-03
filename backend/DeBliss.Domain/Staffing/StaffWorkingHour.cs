using DeBliss.Domain.Common;

namespace DeBliss.Domain.Staffing;

/// <summary>
/// A recurring weekly working pattern for one staff member. Kept separate from
/// <see cref="StaffTimeOff"/> so availability can be calculated as
/// working hours minus time off minus blocked time.
/// </summary>
public class StaffWorkingHour : AuditableEntity
{
    public Guid StaffId { get; set; }

    public Staff Staff { get; set; } = null!;

    public DayOfWeek DayOfWeek { get; set; }

    /// <summary>Ignored when <see cref="IsWorkingDay"/> is false.</summary>
    public TimeSpan StartTime { get; set; }

    /// <summary>Ignored when <see cref="IsWorkingDay"/> is false.</summary>
    public TimeSpan EndTime { get; set; }

    public bool IsWorkingDay { get; set; } = true;
}
