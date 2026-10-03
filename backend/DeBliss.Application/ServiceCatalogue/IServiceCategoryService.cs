using DeBliss.Application.ServiceCatalogue.Dtos;

namespace DeBliss.Application.ServiceCatalogue;

/// <summary>
/// Manages the service menu's categories. Categories are never physically
/// deleted: a category that has services attached is deactivated instead, so
/// historical services and the appointments that snapshot them stay resolvable.
/// </summary>
public interface IServiceCategoryService
{
    Task<IReadOnlyList<ServiceCategoryDto>> GetCategoriesAsync(
        ServiceStatusFilter status,
        CancellationToken cancellationToken = default);

    Task<ServiceCategoryDto> GetCategoryAsync(Guid id, CancellationToken cancellationToken = default);

    Task<ServiceCategoryDto> CreateAsync(SaveServiceCategoryRequest request, CancellationToken cancellationToken = default);

    Task<ServiceCategoryDto> UpdateAsync(Guid id, SaveServiceCategoryRequest request, CancellationToken cancellationToken = default);

    Task<ServiceCategoryDto> SetStatusAsync(Guid id, bool isActive, CancellationToken cancellationToken = default);
}
