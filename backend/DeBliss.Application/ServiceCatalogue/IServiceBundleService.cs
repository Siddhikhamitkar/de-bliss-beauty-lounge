using DeBliss.Application.ServiceCatalogue.Dtos;

namespace DeBliss.Application.ServiceCatalogue;

/// <summary>
/// Manages service bundles: several existing services offered together as one
/// menu entry at its own price. Bundles are edited through the same drawer
/// experience as services and are deactivated rather than deleted.
/// </summary>
public interface IServiceBundleService
{
    Task<BundleDto> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>The bundles the booking flow may offer, with components in sequence.</summary>
    Task<IReadOnlyList<BundleDto>> GetBookableAsync(CancellationToken cancellationToken = default);

    Task<BundleDto> CreateAsync(SaveBundleRequest request, CancellationToken cancellationToken = default);

    Task<BundleDto> UpdateAsync(Guid id, SaveBundleRequest request, CancellationToken cancellationToken = default);

    Task<BundleDto> SetStatusAsync(Guid id, bool isActive, CancellationToken cancellationToken = default);
}
