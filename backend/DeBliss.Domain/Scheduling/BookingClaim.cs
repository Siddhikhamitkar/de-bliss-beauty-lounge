using DeBliss.Domain.Common;
using DeBliss.Domain.Customers;

namespace DeBliss.Domain.Scheduling;

/// <summary>
/// One customer booking attempt, identified by the client-supplied idempotency
/// key. The unique (customer, key) pair is what makes a repeated or concurrent
/// submit resolve to the single booking it produced instead of a second one,
/// and the request hash detects key reuse with a different payload.
/// </summary>
public class BookingClaim : AuditableEntity
{
    public Guid CustomerId { get; set; }

    public Customer? Customer { get; set; }

    /// <summary>The client-supplied key; unique per customer.</summary>
    public string IdempotencyKey { get; set; } = null!;

    /// <summary>SHA-256 of the canonical booking payload at claim time.</summary>
    public string RequestHash { get; set; } = null!;

    /// <summary>The booking this attempt produced, once committed.</summary>
    public Guid? AppointmentId { get; set; }

    public Appointment? Appointment { get; set; }
}
