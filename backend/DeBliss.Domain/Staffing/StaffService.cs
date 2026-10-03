using DeBliss.Domain.Common;
using DeBliss.Domain.Services;

namespace DeBliss.Domain.Staffing;

/// <summary>
/// Join record expressing that a staff member can perform a given service, which
/// is what makes a service offerable by multiple staff members.
/// </summary>
public class StaffService : AuditableEntity
{
    public Guid StaffId { get; set; }

    public Staff Staff { get; set; } = null!;

    public Guid ServiceId { get; set; }

    public Service Service { get; set; } = null!;
}
