using DeBliss.Application.Customers.Dtos;
using DeBliss.Domain.Customers;

namespace DeBliss.Application.Customers.Ports;

/// <summary>
/// Customer read/write access for the customers module. Instances are scoped to
/// a unit of work; entities returned by GetByIdAsync/GetNoteAsync are tracked
/// so callers can mutate and save, while list, history and aggregate queries do
/// not track.
/// </summary>
public interface ICustomerRepository
{
    /// <summary>Tracked customer or null.</summary>
    Task<Customer?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<(IReadOnlyList<Customer> Items, int TotalCount)> SearchAsync(CustomerQuery query, CancellationToken ct = default);

    /// <summary>
    /// Customers whose phone number matches once formatting is ignored. Used for
    /// the duplicate warning; excludeId removes the customer being edited.
    /// </summary>
    Task<IReadOnlyList<Customer>> GetByNormalizedPhoneAsync(string normalizedPhone, Guid? excludeId, CancellationToken ct = default);

    /// <summary>Tracked note belonging to the customer, or null.</summary>
    Task<CustomerNote?> GetNoteAsync(Guid customerId, Guid noteId, CancellationToken ct = default);

    /// <summary>The customer's notes, newest first, read-only.</summary>
    Task<IReadOnlyList<CustomerNote>> ListNotesAsync(Guid customerId, CancellationToken ct = default);

    /// <summary>The customer's appointments, newest first, read-only, with their service lines and staff.</summary>
    Task<IReadOnlyList<DeBliss.Domain.Scheduling.Appointment>> ListAppointmentsAsync(Guid customerId, CancellationToken ct = default);

    /// <summary>Appointment counts for the profile summary block.</summary>
    Task<(int Total, int Completed, int Cancelled, int NoShow, DateTimeOffset? LastVisitAt)> GetAppointmentSummaryAsync(
        Guid customerId,
        CancellationToken ct = default);

    void Add(Customer customer);

    void AddNote(CustomerNote note);
}
