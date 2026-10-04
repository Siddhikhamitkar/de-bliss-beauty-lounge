using DeBliss.Application.Staffing.Dtos;
using DeBliss.Domain.Staffing;

namespace DeBliss.Application.Staffing.Ports;

/// <summary>
/// Staff read/write access for the staffing module. Instances are scoped to a
/// unit of work; entities returned by GetByIdAsync/GetByIdForScheduleAsync are
/// tracked so callers can mutate and save.
/// </summary>
public interface IStaffRepository
{
    /// <summary>Tracked staff member with services and working hours loaded, or null.</summary>
    Task<Staff?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Tracked staff member with their working hours loaded, or null.</summary>
    Task<Staff?> GetByIdForScheduleAsync(Guid id, CancellationToken ct = default);

    Task<(IReadOnlyList<Staff> Items, int TotalCount)> SearchAsync(StaffQuery query, CancellationToken ct = default);

    /// <summary>Every active staff member, ordered for display; no related data loaded.</summary>
    Task<IReadOnlyList<Staff>> GetActiveAsync(CancellationToken ct = default);

    /// <summary>Staff members with the given ids, ordered for display; no related data loaded.</summary>
    Task<IReadOnlyList<Staff>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default);

    /// <summary>Active staff assigned to the service, ordered for display.</summary>
    Task<IReadOnlyList<Staff>> GetActiveAssignedToServiceAsync(Guid serviceId, CancellationToken ct = default);

    void Add(Staff staff);
}
