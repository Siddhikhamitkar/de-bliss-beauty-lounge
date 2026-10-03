using DeBliss.Domain.Common;
using DeBliss.Domain.Identity;
using DeBliss.Domain.Staffing;

namespace DeBliss.Domain.Scheduling;

/// <summary>
/// A non-bookable period such as a break, meeting, leave or maintenance. When
/// <see cref="StaffId"/> is null the block applies to the whole salon rather than
/// to one staff member.
/// </summary>
public class BlockedTime : AuditableEntity
{
    public Guid? StaffId { get; set; }

    public Staff? Staff { get; set; }

    public DateTimeOffset StartsAt { get; set; }

    public DateTimeOffset EndsAt { get; set; }

    public BlockedTimeType Type { get; set; }

    public string? Reason { get; set; }

    public Guid? CreatedByUserId { get; set; }

    public User? CreatedByUser { get; set; }
}
