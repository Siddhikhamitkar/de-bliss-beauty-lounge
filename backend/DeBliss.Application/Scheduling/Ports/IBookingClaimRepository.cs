using DeBliss.Domain.Scheduling;

namespace DeBliss.Application.Scheduling.Ports;

/// <summary>
/// Idempotency claims for customer bookings. The unique (customer, key) index
/// is the database-level guarantee that a repeated or simultaneous submit
/// resolves to one booking; the request hash detects key reuse with a
/// different payload. Implemented in Infrastructure because detecting a lost
/// claim race means catching the provider's duplicate-key exception.
/// </summary>
public interface IBookingClaimRepository
{
    /// <summary>The claim this customer previously made with this key, or null.</summary>
    Task<BookingClaim?> FindAsync(Guid customerId, string idempotencyKey, CancellationToken ct = default);

    /// <summary>
    /// Records the claim. Returns null when this attempt won the race and the
    /// claim is now held; returns the winner's existing claim when a concurrent
    /// request with the same customer and key got there first.
    /// </summary>
    Task<BookingClaim?> TryAddAsync(BookingClaim claim, CancellationToken ct = default);

    /// <summary>Points the claim at the booking it produced, once that booking is committed.</summary>
    Task LinkAppointmentAsync(Guid customerId, string idempotencyKey, Guid appointmentId, CancellationToken ct = default);
}
