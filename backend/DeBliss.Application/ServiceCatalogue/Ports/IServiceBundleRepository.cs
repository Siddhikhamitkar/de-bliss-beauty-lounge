using DeBliss.Application.ServiceCatalogue.Dtos;
using DeBliss.Domain.Services;

namespace DeBliss.Application.ServiceCatalogue.Ports;

/// <summary>Persistence port for service bundles.</summary>
public interface IServiceBundleRepository
{
    /// <summary>
    /// Loads a bundle tracked, with its category, items and component services,
    /// so the caller can modify and save it.
    /// </summary>
    Task<ServiceBundle?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every bundle matching the menu filters, ordered like the service menu.
    /// Returned unpaged: the catalogue service merges bundles with services
    /// into one ordered, paged menu.
    /// </summary>
    Task<IReadOnlyList<ServiceBundle>> SearchAsync(
        ServiceQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>Active bundles whose every component can actually be booked right now.</summary>
    Task<IReadOnlyList<ServiceBundle>> GetBookableAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Case-insensitive duplicate check scoped to one category, optionally
    /// ignoring the bundle being edited.
    /// </summary>
    Task<bool> ExistsNameInCategoryAsync(
        string name,
        Guid categoryId,
        Guid? excludingId,
        CancellationToken cancellationToken = default);

    void Add(ServiceBundle bundle);
}
