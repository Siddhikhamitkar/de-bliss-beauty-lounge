using DeBliss.Application.Common.Exceptions;
using DeBliss.Application.Common.Interfaces;
using DeBliss.Application.ServiceCatalogue.Dtos;
using DeBliss.Application.ServiceCatalogue.Ports;
using DeBliss.Domain.Services;

namespace DeBliss.Application.ServiceCatalogue;

/// <summary>
/// Business rules for service bundles. A bundle never redefines its
/// components' duration or standard price — both are derived, so the menu,
/// the booking window and the checkout math can never disagree with the
/// catalogue. Like services, a bundle is deactivated rather than deleted so
/// appointments and invoices that reference it stay resolvable.
/// </summary>
public sealed class ServiceBundleService(
    IServiceBundleRepository bundles,
    IServiceRepository services,
    IServiceCategoryRepository categories,
    IUnitOfWork unitOfWork) : IServiceBundleService
{
    public async Task<BundleDto> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var bundle = await LoadAsync(id, cancellationToken);

        return Map(bundle);
    }

    public async Task<IReadOnlyList<BundleDto>> GetBookableAsync(CancellationToken cancellationToken = default)
    {
        var bookable = await bundles.GetBookableAsync(cancellationToken);

        return [.. bookable.Select(Map)];
    }

    public async Task<BundleDto> CreateAsync(
        SaveBundleRequest request,
        CancellationToken cancellationToken = default)
    {
        var validated = await ValidateAsync(request, excludingId: null, cancellationToken);

        var bundle = new ServiceBundle
        {
            ServiceCategoryId = validated.Category.Id,
            ServiceCategory = validated.Category,
            Name = validated.Name,
            Description = validated.Description,
            Price = request.Price,
            IsActive = request.IsActive,
            SortOrder = request.SortOrder
        };

        ReplaceItems(bundle, validated.Components);

        bundles.Add(bundle);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Re-read rather than map the in-memory bundle: bookability depends on
        // each component's staff assignments, which the component lookup does
        // not load, so the fresh copy is the one that knows the truth.
        return Map(await LoadAsync(bundle.Id, cancellationToken));
    }

    public async Task<BundleDto> UpdateAsync(
        Guid id,
        SaveBundleRequest request,
        CancellationToken cancellationToken = default)
    {
        var validated = await ValidateAsync(request, excludingId: id, cancellationToken);

        var bundle = await LoadAsync(id, cancellationToken);

        bundle.ServiceCategoryId = validated.Category.Id;
        bundle.ServiceCategory = validated.Category;
        bundle.Name = validated.Name;
        bundle.Description = validated.Description;
        bundle.Price = request.Price;
        bundle.IsActive = request.IsActive;
        bundle.SortOrder = request.SortOrder;

        ReplaceItems(bundle, validated.Components);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Map(await LoadAsync(bundle.Id, cancellationToken));
    }

    /// <summary>
    /// Activating is only allowed once every component could actually be
    /// booked; deactivating hides the bundle from new bookings while leaving
    /// every past appointment and invoice pointing at it.
    /// </summary>
    public async Task<BundleDto> SetStatusAsync(
        Guid id,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        var bundle = await LoadAsync(id, cancellationToken);

        // Assigned before the check, because bookability includes the bundle's
        // own active flag — testing the loaded (still inactive) bundle would
        // make reactivating it impossible. Throwing here skips the save, so a
        // refused activation writes nothing.
        bundle.IsActive = isActive;

        if (isActive && !bundle.CanBeBooked())
        {
            throw new ValidationFailedException(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["isActive"] =
                [
                    "Every service in the bundle must itself be active and bookable before the bundle can be activated."
                ]
            });
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Map(bundle);
    }

    private async Task<ServiceBundle> LoadAsync(Guid id, CancellationToken cancellationToken)
    {
        return await bundles.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException($"Service bundle '{id}' was not found.");
    }

    /// <summary>
    /// Brings the component set in line with the submitted list. Unchanged
    /// components keep their row — only the sequence moves — so editing a
    /// bundle never rewrites history that did not change.
    /// </summary>
    private static void ReplaceItems(ServiceBundle bundle, IReadOnlyList<Service> componentsInOrder)
    {
        var wanted = componentsInOrder.Select(service => service.Id).ToHashSet();

        foreach (var item in bundle.Items.Where(item => !wanted.Contains(item.ServiceId)).ToList())
        {
            bundle.Items.Remove(item);
        }

        var existingByServiceId = bundle.Items.ToDictionary(item => item.ServiceId);

        for (var position = 0; position < componentsInOrder.Count; position++)
        {
            var service = componentsInOrder[position];

            if (existingByServiceId.TryGetValue(service.Id, out var item))
            {
                item.DisplayOrder = position;
            }
            else
            {
                bundle.Items.Add(new ServiceBundleItem
                {
                    ServiceBundle = bundle,
                    Service = service,
                    ServiceId = service.Id,
                    DisplayOrder = position
                });
            }
        }
    }

    /// <summary>
    /// Field validation runs first so one bad request reports every problem at
    /// once. The duplicate-name check only runs once the rest is valid, which
    /// also makes 400 win over 409 when both apply.
    /// </summary>
    private async Task<ValidatedBundle> ValidateAsync(
        SaveBundleRequest request,
        Guid? excludingId,
        CancellationToken cancellationToken)
    {
        var errors = new ValidationErrorBuilder();

        var name = request.Name?.Trim() ?? string.Empty;

        if (name.Length == 0)
        {
            errors.Add("name", "Bundle name is required.");
        }
        else if (name.Length > ServiceCatalogueLimits.BundleNameMaxLength)
        {
            errors.Add(
                "name",
                $"Bundle name must be {ServiceCatalogueLimits.BundleNameMaxLength} characters or fewer.");
        }

        var description = request.Description?.Trim();

        if (string.IsNullOrEmpty(description))
        {
            description = null;
        }
        else if (description.Length > ServiceCatalogueLimits.BundleDescriptionMaxLength)
        {
            errors.Add(
                "description",
                $"Description must be {ServiceCatalogueLimits.BundleDescriptionMaxLength} characters or fewer.");
        }

        ServiceCategory? category = null;
        var requestedCategoryId = request.CategoryId is { } categoryId && categoryId != Guid.Empty ? categoryId : (Guid?)null;

        if (requestedCategoryId is null)
        {
            errors.Add("categoryId", "Choose a category for this bundle.");
        }
        else
        {
            category = await categories.GetByIdAsync(requestedCategoryId.Value, cancellationToken);

            if (category is null)
            {
                errors.Add("categoryId", "The selected category no longer exists. Choose another one.");
            }
        }

        if (request.SortOrder < 0)
        {
            errors.Add("sortOrder", "Display position cannot be negative.");
        }

        var requestedServiceIds = NormalizeServiceIds(request.Items);

        if (requestedServiceIds.Count < ServiceBundle.MinimumComponents)
        {
            errors.Add(
                "items",
                $"A bundle needs at least {ServiceBundle.MinimumComponents} different services.");
        }

        var duplicateCount = request.Items?.Count - requestedServiceIds.Count ?? 0;

        if (duplicateCount > 0)
        {
            errors.Add("items", "The same service cannot be included more than once in a bundle.");
        }

        IReadOnlyList<Service> foundServices = requestedServiceIds.Count == 0
            ? []
            : await services.GetByIdsAsync(requestedServiceIds, cancellationToken);

        var foundIds = foundServices.Select(service => service.Id).ToHashSet();
        var unknownCount = requestedServiceIds.Count(serviceId => !foundIds.Contains(serviceId));

        if (unknownCount > 0)
        {
            errors.Add(
                "items",
                $"{unknownCount} of the selected services no longer exist. Remove them and try again.");
        }

        var inactiveNames = foundServices.Where(service => !service.IsActive).Select(service => service.Name).ToList();

        if (inactiveNames.Count > 0)
        {
            errors.Add(
                "items",
                $"Only active services can go in a bundle. Deactivate these instead of removing them: {string.Join(", ", inactiveNames)}.");
        }

        var standardTotal = foundServices.Sum(service => service.Price);

        if (request.Price < 0m)
        {
            errors.Add("price", "Bundle price cannot be negative.");
        }
        else if (request.Price > ServiceCatalogueLimits.MaximumPrice)
        {
            errors.Add("price", $"Bundle price cannot be more than {ServiceCatalogueLimits.MaximumPrice:N2}.");
        }
        else if (request.Price != decimal.Round(request.Price, 2))
        {
            errors.Add("price", "Bundle price can have at most two decimal places.");
        }
        else if (unknownCount == 0 && requestedServiceIds.Count >= ServiceBundle.MinimumComponents
            && request.Price > standardTotal)
        {
            errors.Add(
                "price",
                $"Bundle price cannot exceed the combined individual price of the included services ({standardTotal:N2}).");
        }

        if (errors.IsValid && category is not null
            && await bundles.ExistsNameInCategoryAsync(name, category.Id, excludingId, cancellationToken))
        {
            throw new ConflictException(
                $"A bundle named '{name}' already exists in the '{category.Name}' category.");
        }

        errors.ThrowIfInvalid();

        // The submitted order is the delivery sequence; the repository does not
        // preserve it, so restore it here before the items are (re)written.
        var positionById = requestedServiceIds
            .Select((serviceId, index) => (serviceId, index))
            .ToDictionary(pair => pair.serviceId, pair => pair.index);

        var components = foundServices
            .OrderBy(service => positionById[service.Id])
            .ToList();

        return new ValidatedBundle(name, description, category!, components);
    }

    private static List<Guid> NormalizeServiceIds(IReadOnlyList<Guid>? serviceIds)
    {
        if (serviceIds is null)
        {
            return [];
        }

        var seen = new HashSet<Guid>();
        var result = new List<Guid>(serviceIds.Count);

        foreach (var serviceId in serviceIds)
        {
            if (serviceId != Guid.Empty && seen.Add(serviceId))
            {
                result.Add(serviceId);
            }
        }

        return result;
    }

    private static BundleDto Map(ServiceBundle bundle) => new(
        bundle.Id,
        bundle.Name,
        bundle.Description,
        bundle.ServiceCategoryId,
        bundle.ServiceCategory?.Name,
        bundle.ServiceCategory?.IsActive ?? false,
        bundle.TotalDurationMinutes,
        bundle.Price,
        bundle.StandardTotalPrice,
        bundle.SavingsAmount,
        bundle.IsActive,
        bundle.CanBeBooked(),
        bundle.SortOrder,
        [.. bundle.Items
            .OrderBy(item => item.DisplayOrder)
            .Select(item => new BundleItemDto(
                item.ServiceId,
                item.Service.Name,
                item.Service.DurationMinutes,
                item.Service.Price,
                item.DisplayOrder,
                item.Service.IsActive))],
        bundle.CreatedAt,
        bundle.UpdatedAt);

    private sealed record ValidatedBundle(
        string Name,
        string? Description,
        ServiceCategory Category,
        IReadOnlyList<Service> Components);
}
