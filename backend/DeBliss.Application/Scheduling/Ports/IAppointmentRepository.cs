using DeBliss.Domain.Scheduling;

namespace DeBliss.Application.Scheduling.Ports;

/// <summary>
/// Appointment persistence for the scheduling module. Instances are scoped to
/// a unit of work; GetByIdAsync returns the tracked appointment (lines
/// included) so callers can mutate and save, while the range queries do not
/// track. DateTimeOffset predicates run in memory because SQLite cannot
/// translate them; the translatable filters (ids, soft-delete) narrow first.
/// </summary>
public interface IAppointmentRepository
{
    /// <summary>Tracked appointment with its service lines, or null.</summary>
    Task<Appointment?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Non-deleted appointments overlapping [from, to), newest first, read-only, with lines and customer.</summary>
    Task<IReadOnlyList<Appointment>> GetInRangeAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);

    /// <summary>Distinct staff ids with a service line on any appointment overlapping the range.</summary>
    Task<IReadOnlySet<Guid>> GetStaffIdsInRangeAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);

    void Add(Appointment appointment);
}
