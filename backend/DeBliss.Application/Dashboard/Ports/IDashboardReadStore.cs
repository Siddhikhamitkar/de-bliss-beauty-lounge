using DeBliss.Domain.Customers;

namespace DeBliss.Application.Dashboard.Ports;

/// <summary>
/// Read-only queries the receptionist dashboard needs that no other module's
/// port exposes: birthday matching, first-visit detection and the freshest
/// reception notes. Everything else the dashboard composes from the scheduling
/// and staffing ports.
/// </summary>
public interface IDashboardReadStore
{
    /// <summary>Active customers whose birthday (month and day) falls on one of the given dates.</summary>
    Task<IReadOnlyList<Customer>> FindBirthdayCustomersAsync(
        IReadOnlyCollection<DateOnly> days,
        CancellationToken ct = default);

    /// <summary>Ids of customers whose very first appointment starts inside [dayStart, dayEnd).</summary>
    Task<IReadOnlyList<Guid>> GetFirstVisitCustomerIdsAsync(
        DateTimeOffset dayStart,
        DateTimeOffset dayEnd,
        CancellationToken ct = default);

    /// <summary>The newest reception notes for active customers, newest first.</summary>
    Task<IReadOnlyList<CustomerNote>> GetRecentNotesAsync(int take, CancellationToken ct = default);
}
