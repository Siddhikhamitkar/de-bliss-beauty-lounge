using DeBliss.Domain.Common;
using DeBliss.Domain.Identity;
using DeBliss.Domain.Scheduling;
using DeBliss.Domain.Services;

namespace DeBliss.Domain.Staffing;

/// <summary>
/// A person who performs services. A staff member may or may not have a login.
/// </summary>
public class Staff : AuditableEntity
{
    public string FirstName { get; set; } = null!;

    public string LastName { get; set; } = null!;

    public string? Email { get; set; }

    public string? PhoneNumber { get; set; }

    public string? JobTitle { get; set; }

    /// <summary>Colour used to render this staff member's calendar column.</summary>
    public string? CalendarColor { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Controls column order on the calendar.</summary>
    public int SortOrder { get; set; }

    public Guid? UserId { get; set; }

    public User? User { get; set; }

    public ICollection<StaffService> StaffServices { get; set; } = new List<StaffService>();

    public ICollection<StaffWorkingHour> WorkingHours { get; set; } = new List<StaffWorkingHour>();

    public ICollection<StaffTimeOff> TimeOff { get; set; } = new List<StaffTimeOff>();

    public ICollection<AppointmentService> AppointmentServices { get; set; } = new List<AppointmentService>();

    public ICollection<BlockedTime> BlockedTimes { get; set; } = new List<BlockedTime>();
}
