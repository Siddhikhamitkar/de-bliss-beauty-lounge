using DeBliss.Domain.Billing;
using DeBliss.Domain.Common;
using DeBliss.Domain.Customers;
using DeBliss.Domain.Identity;

namespace DeBliss.Domain.Scheduling;

/// <summary>
/// A booking occupying a time range. Supports multiple services, each of which can
/// be performed by a different staff member, and may belong to no customer record
/// at all when it is a walk-in.
/// </summary>
public class Appointment : AuditableEntity, ISoftDeletable
{
    /// <summary>Null for a true walk-in where no customer record was created.</summary>
    public Guid? CustomerId { get; set; }

    public Customer? Customer { get; set; }

    /// <summary>Name captured for a walk-in when no customer record exists.</summary>
    public string? WalkInCustomerName { get; set; }

    public string? WalkInCustomerPhone { get; set; }

    public DateTimeOffset StartsAt { get; set; }

    /// <summary>
    /// Stored rather than derived so that calendar range queries can be served by
    /// a single indexed column pair.
    /// </summary>
    public DateTimeOffset EndsAt { get; set; }

    public AppointmentStatus Status { get; set; }

    public AppointmentSource Source { get; set; }

    /// <summary>Internal notes for staff. Not shown to the customer.</summary>
    public string? Notes { get; set; }

    public string? CancellationReason { get; set; }

    /// <summary>
    /// Points at the first occurrence of a recurring series. Reserved so recurring
    /// appointments can be added later without restructuring this table.
    /// </summary>
    public Guid? RecurringSeriesId { get; set; }

    public Appointment? RecurringSeries { get; set; }

    public Guid? CreatedByUserId { get; set; }

    public User? CreatedByUser { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public ICollection<AppointmentService> Services { get; set; } = new List<AppointmentService>();

    public ICollection<AppointmentStatusHistory> StatusHistory { get; set; } = new List<AppointmentStatusHistory>();

    public ICollection<Invoice> Invoices { get; set; } = new List<Invoice>();
}
