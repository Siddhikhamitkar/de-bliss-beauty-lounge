using DeBliss.Application.ServiceCatalogue.Dtos;
using DeBliss.Domain.Services;

namespace DeBliss.Application.ServiceCatalogue.Ports;

/// <summary>Persistence port for bookable services.</summary>
public interface IServiceRepository
{
    /// <summary>
    /// Loads a service tracked, with its category and its staff assignments, so
    /// the caller can modify and save it.
    /// </summary>
    Task<Service?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<Service> Items, int TotalCount)> SearchAsync(
        ServiceQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Everything the appointment module needs in order to offer a booking:
    /// active services in active categories with at least one active staff
    /// member able to perform them.
    /// </summary>
    Task<IReadOnlyList<Service>> GetBookableAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Case-insensitive duplicate check scoped to one category, optionally
    /// ignoring the service being edited.
    /// </summary>
    Task<bool> ExistsNameInCategoryAsync(
        string name,
        Guid categoryId,
        Guid? excludingId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tracked services with their categories loaded, so an assignment batch
    /// can be validated and mapped in one round trip.
    /// </summary>
    Task<IReadOnlyList<Service>> GetByIdsAsync(
        IReadOnlyList<Guid> ids,
        CancellationToken cancellationToken = default);

    void Add(Service service);
}
