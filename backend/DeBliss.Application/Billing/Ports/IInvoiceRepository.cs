using DeBliss.Application.Billing.Dtos;
using DeBliss.Domain.Billing;

namespace DeBliss.Application.Billing.Ports;

/// <summary>
/// Invoice persistence for the billing module. GetByIdAsync returns a tracked
/// invoice (lines, payments, customer and appointment included) because the
/// application service mutates what it loads; the list and lookup queries do
/// not track. Deleted invoices are invisible here — nothing in Phase 1 deletes
/// them, and if that ever changes the soft-delete filter is already in place.
/// </summary>
public interface IInvoiceRepository
{
    /// <summary>Tracked invoice with its lines, payments, customer and appointment, or null.</summary>
    Task<Invoice?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// The open (not void, not deleted) invoice for an appointment, read-only,
    /// with its lines — used to resume a checkout that was interrupted rather
    /// than raising a second bill for the same visit.
    /// </summary>
    Task<Invoice?> GetByAppointmentIdAsync(Guid appointmentId, CancellationToken ct = default);

    Task<(IReadOnlyList<Invoice> Items, int TotalCount)> SearchAsync(InvoiceQuery query, CancellationToken ct = default);

    /// <summary>
    /// The highest sequence number already used in the given year, or 0 when
    /// the year has no invoices yet. Invoice numbers are zero-padded, so a
    /// string MAX over the prefix is equivalent to a numeric MAX.
    /// </summary>
    Task<int> GetHighestSequenceAsync(int year, CancellationToken ct = default);

    void Add(Invoice invoice);
}
