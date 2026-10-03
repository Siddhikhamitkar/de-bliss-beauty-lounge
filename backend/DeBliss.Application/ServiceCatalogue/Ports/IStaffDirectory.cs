using DeBliss.Domain.Staffing;

namespace DeBliss.Application.ServiceCatalogue.Ports;

/// <summary>
/// Read-only view of the people who can perform services. This module needs it
/// to populate the eligible-staff selector and to validate assignments; it does
/// not manage staff records. Staff management belongs to a later module.
/// </summary>
public interface IStaffDirectory
{
    Task<IReadOnlyList<Staff>> GetSelectableAsync(bool includeInactive, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Staff>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);
}
