using DeBliss.Domain.Common;
using DeBliss.Domain.Services;
using DeBliss.Domain.Staffing;

namespace DeBliss.Domain.Scheduling;

/// <summary>
/// One service line inside an appointment. The name, duration and price are
/// snapshotted at booking time so that later edits to the service catalogue never
/// rewrite history. <see cref="StaffId"/> is nullable so a line can be booked
/// before a specific staff member is assigned.
/// </summary>
public class AppointmentService : AuditableEntity
{
    public Guid AppointmentId { get; set; }

    public Appointment Appointment { get; set; } = null!;

    public Guid ServiceId { get; set; }

    public Service Service { get; set; } = null!;

    /// <summary>The specific staff member performing this individual service.</summary>
    public Guid? StaffId { get; set; }

    public Staff? Staff { get; set; }

    public string ServiceNameSnapshot { get; set; } = null!;

    /// <summary>Booked duration. Editable without affecting Service.DurationMinutes.</summary>
    public int DurationMinutes { get; set; }

    /// <summary>Price charged at the time of booking.</summary>
    public decimal UnitPrice { get; set; }

    public int SortOrder { get; set; }

    public string? Notes { get; set; }
}
