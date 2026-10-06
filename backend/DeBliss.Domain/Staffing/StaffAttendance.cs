using DeBliss.Domain.Common;

namespace DeBliss.Domain.Staffing;

/// <summary>
/// One staff member's clock-in/clock-out pair for a single work date, linked to
/// the existing <see cref="Staff"/> master record. Both timestamps are written by
/// the server; no worked-hours total is derived or stored here.
/// </summary>
public class StaffAttendance : AuditableEntity
{
    public Guid StaffId { get; set; }

    public Staff Staff { get; set; } = null!;

    public DateOnly WorkDate { get; set; }

    public DateTimeOffset CheckInAt { get; set; }

    public DateTimeOffset? CheckOutAt { get; set; }

    public AttendanceStatus Status { get; set; }
}
