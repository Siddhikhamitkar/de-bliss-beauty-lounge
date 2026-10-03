using DeBliss.Application.Common.Exceptions;
using DeBliss.Application.Common.Interfaces;
using DeBliss.Application.ServiceCatalogue.Dtos;
using DeBliss.Application.ServiceCatalogue.Ports;
using DeBliss.Domain.Services;

namespace DeBliss.Application.ServiceCatalogue;

/// <summary>
/// Business rules for the service menu categories. Nothing is ever physically
/// deleted: a category keeps its services so that historical appointments still
/// point at a real menu entry.
/// </summary>
public sealed class ServiceCategoryService(
    IServiceCategoryRepository categories,
    IUnitOfWork unitOfWork) : IServiceCategoryService
{
    public async Task<IReadOnlyList<ServiceCategoryDto>> GetCategoriesAsync(
        ServiceStatusFilter status,
        CancellationToken cancellationToken = default)
    {
        var found = await categories.QueryAsync(status, cancellationToken);
        var counts = await categories.CountServicesAsync(cancellationToken);

        return [.. found.Select(category => Map(category, counts.GetValueOrDefault(category.Id)))];
    }

    public async Task<ServiceCategoryDto> GetCategoryAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var category = await LoadAsync(id, cancellationToken);
        var counts = await categories.CountServicesAsync(cancellationToken);

        return Map(category, counts.GetValueOrDefault(category.Id));
    }

    public async Task<ServiceCategoryDto> CreateAsync(
        SaveServiceCategoryRequest request,
        CancellationToken cancellationToken = default)
    {
        var name = await ValidateAsync(request, excludingId: null, cancellationToken);

        var category = new ServiceCategory
        {
            Name = name,
            Description = NormalizeDescription(request.Description),
            SortOrder = request.SortOrder,
            IsActive = request.IsActive
        };

        categories.Add(category);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Map(category, serviceCount: 0);
    }

    public async Task<ServiceCategoryDto> UpdateAsync(
        Guid id,
        SaveServiceCategoryRequest request,
        CancellationToken cancellationToken = default)
    {
        var name = await ValidateAsync(request, excludingId: id, cancellationToken);

        var category = await LoadAsync(id, cancellationToken);

        category.Name = name;
        category.Description = NormalizeDescription(request.Description);
        category.SortOrder = request.SortOrder;
        category.IsActive = request.IsActive;

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var counts = await categories.CountServicesAsync(cancellationToken);

        return Map(category, counts.GetValueOrDefault(category.Id));
    }

    /// <summary>
    /// Deactivating a category hides it from new bookings and makes its services
    /// unbookable, but deliberately leaves every service underneath it untouched
    /// so that past appointments keep their menu structure.
    /// </summary>
    public async Task<ServiceCategoryDto> SetStatusAsync(
        Guid id,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        var category = await LoadAsync(id, cancellationToken);

        category.IsActive = isActive;

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var counts = await categories.CountServicesAsync(cancellationToken);

        return Map(category, counts.GetValueOrDefault(category.Id));
    }

    private async Task<ServiceCategory> LoadAsync(Guid id, CancellationToken cancellationToken)
    {
        return await categories.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException($"Service category '{id}' was not found.");
    }

    /// <summary>
    /// Field validation runs first so a badly formed request reports every
    /// problem at once; the duplicate-name check needs a clean name to be
    /// meaningful, so it only runs once the rest of the request is valid.
    /// </summary>
    private async Task<string> ValidateAsync(
        SaveServiceCategoryRequest request,
        Guid? excludingId,
        CancellationToken cancellationToken)
    {
        var errors = new ValidationErrorBuilder();

        var name = request.Name?.Trim() ?? string.Empty;

        if (name.Length == 0)
        {
            errors.Add("name", "Category name is required.");
        }
        else if (name.Length > ServiceCatalogueLimits.CategoryNameMaxLength)
        {
            errors.Add(
                "name",
                $"Category name must be {ServiceCatalogueLimits.CategoryNameMaxLength} characters or fewer.");
        }

        if (request.Description?.Length > ServiceCatalogueLimits.CategoryDescriptionMaxLength)
        {
            errors.Add(
                "description",
                $"Description must be {ServiceCatalogueLimits.CategoryDescriptionMaxLength} characters or fewer.");
        }

        if (request.SortOrder < 0)
        {
            errors.Add("sortOrder", "Display position cannot be negative.");
        }

        if (errors.IsValid && await categories.ExistsNameAsync(name, excludingId, cancellationToken))
        {
            throw new ConflictException($"A category named '{name}' already exists.");
        }

        errors.ThrowIfInvalid();

        return name;
    }

    private static string? NormalizeDescription(string? description)
    {
        var trimmed = description?.Trim();

        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static ServiceCategoryDto Map(ServiceCategory category, int serviceCount) => new(
        category.Id,
        category.Name,
        category.Description,
        category.SortOrder,
        category.IsActive,
        serviceCount,
        category.CreatedAt,
        category.UpdatedAt);
}
