using DeBliss.Application.ServiceCatalogue.Dtos;
using DeBliss.Domain.Services;

namespace DeBliss.Application.ServiceCatalogue.Ports;

/// <summary>
/// Persistence port for service menu categories. Implemented in Infrastructure
/// so this layer never sees EF Core.
/// </summary>
public interface IServiceCategoryRepository
{
    Task<ServiceCategory?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ServiceCategory>> QueryAsync(
        ServiceStatusFilter status,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Case-insensitive check against the trimmed name, optionally ignoring one
    /// category so an edit does not collide with itself.
    /// </summary>
    Task<bool> ExistsNameAsync(string name, Guid? excludingId, CancellationToken cancellationToken = default);

    /// <summary>Number of services per category, for every category, in one query.</summary>
    Task<IReadOnlyDictionary<Guid, int>> CountServicesAsync(CancellationToken cancellationToken = default);

    void Add(ServiceCategory category);
}
