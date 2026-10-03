using DeBliss.Domain.Common;
using DeBliss.Domain.Scheduling;
using DeBliss.Domain.Staffing;

namespace DeBliss.Domain.Services;

/// <summary>
/// A bookable service as it is offered today. <see cref="Price"/> and
/// <see cref="DurationMinutes"/> are the current catalogue values only; what a
/// customer actually booked is snapshotted onto AppointmentService.
/// </summary>
public class Service : AuditableEntity
{
    public Guid? ServiceCategoryId { get; set; }

    public ServiceCategory? ServiceCategory { get; set; }

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    public int DurationMinutes { get; set; }

    /// <summary>Money is always decimal, never a floating point type.</summary>
    public decimal Price { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Whether the future customer-facing website may book this service online.
    /// </summary>
    public bool IsOnlineBookable { get; set; }

    public int SortOrder { get; set; }

    public ICollection<StaffService> StaffServices { get; set; } = new List<StaffService>();

    public ICollection<AppointmentService> AppointmentServices { get; set; } = new List<AppointmentService>();
}
