using DeBliss.Domain.Common;
using DeBliss.Domain.Identity;

namespace DeBliss.Domain.Scheduling;

/// <summary>
/// Append-only audit trail of appointment status transitions. Rows are never
/// updated or deleted.
/// </summary>
public class AppointmentStatusHistory : Entity
{
    public Guid AppointmentId { get; set; }

    public Appointment Appointment { get; set; } = null!;

    /// <summary>Null for the first recorded status of an appointment.</summary>
    public AppointmentStatus? FromStatus { get; set; }

    public AppointmentStatus ToStatus { get; set; }

    public string? Reason { get; set; }

    public Guid? ChangedByUserId { get; set; }

    public User? ChangedByUser { get; set; }

    public DateTimeOffset ChangedAt { get; set; }
}
