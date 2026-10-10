using DeBliss.Application.Common.Exceptions;
using DeBliss.Application.Common.Interfaces;
using DeBliss.Application.Common.Paging;
using DeBliss.Application.ServiceCatalogue.Dtos;
using DeBliss.Application.ServiceCatalogue.Ports;
using DeBliss.Domain.Services;
using DeBliss.Domain.Staffing;

namespace DeBliss.Application.ServiceCatalogue;

/// <summary>
/// Business rules for the bookable service menu: what may be created, what may be
/// edited, and what counts as bookable.
///
/// Two rules protect history. Editing <see cref="Service.Price"/> or
/// <see cref="Service.DurationMinutes"/> only changes the current catalogue
/// value — appointments store their own snapshot on AppointmentService, which
/// this service never touches. And nothing is ever physically deleted: a service
/// is deactivated so that past appointments still resolve to a real menu entry.
/// </summary>
public sealed class ServiceCatalogueService(
    IServiceRepository services,
    IServiceBundleRepository serviceBundles,
    IServiceCategoryRepository categories,
    IStaffDirectory staffDirectory,
    IUnitOfWork unitOfWork) : IServiceCatalogueService
{
    /// <summary>
    /// The menu lists services and bundles together, interleaved within their
    /// category. Both repositories return their full filtered set; the merge
    /// and the paging happen here so a page can never straddle the two
    /// halves incorrectly.
    /// </summary>
    public async Task<PagedResult<ServiceSummaryDto>> SearchAsync(
        ServiceQuery query,
        CancellationToken cancellationToken = default)
    {
        var normalized = query.Normalized();

        var serviceItems = await services.SearchAsync(normalized, cancellationToken);
        var bundleItems = await serviceBundles.SearchAsync(normalized, cancellationToken);

        var merged = serviceItems
            .Select(service => new MenuEntry(service.ServiceCategory?.SortOrder ?? int.MaxValue, MapSummary(service)))
            .Concat(bundleItems.Select(bundle => new MenuEntry(bundle.ServiceCategory?.SortOrder ?? int.MaxValue, MapBundleSummary(bundle))))
            .OrderBy(entry => entry.CategorySortOrder)
            .ThenBy(entry => entry.Summary.SortOrder)
            .ThenBy(entry => entry.Summary.Name, StringComparer.Ordinal)
            .ThenBy(entry => entry.Summary.ItemKind, StringComparer.Ordinal)
            .ToList();

        var page = merged
            .Skip((normalized.Page - 1) * normalized.PageSize)
            .Take(normalized.PageSize)
            .Select(entry => entry.Summary)
            .ToList();

        return new PagedResult<ServiceSummaryDto>(page, merged.Count, normalized.Page, normalized.PageSize);
    }

    public async Task<ServiceDto> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var service = await LoadAsync(id, cancellationToken);

        return Map(service);
    }

    public async Task<IReadOnlyList<ServiceDto>> GetBookableAsync(CancellationToken cancellationToken = default)
    {
        var bookable = await services.GetBookableAsync(cancellationToken);

        return [.. bookable.Select(Map)];
    }

    public async Task<IReadOnlyList<PublicServiceDto>> GetPublicCatalogueAsync(
        CancellationToken cancellationToken = default)
    {
        var publicServices = await services.GetPublicAsync(cancellationToken);

        return [.. publicServices.Select(MapPublic)];
    }

    public async Task<ServiceDto> CreateAsync(
        SaveServiceRequest request,
        CancellationToken cancellationToken = default)
    {
        var validated = await ValidateAsync(request, excludingId: null, cancellationToken);

        var service = new Service
        {
            ServiceCategoryId = validated.Category.Id,
            ServiceCategory = validated.Category,
            Name = validated.Name,
            Description = validated.Description,
            DurationMinutes = request.DurationMinutes,
            Price = request.Price,
            IsActive = request.IsActive,
            IsOnlineBookable = request.IsOnlineBookable,
            SortOrder = request.SortOrder
        };

        AssignStaff(service, validated.Staff);

        services.Add(service);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Map(service);
    }

    public async Task<ServiceDto> UpdateAsync(
        Guid id,
        SaveServiceRequest request,
        CancellationToken cancellationToken = default)
    {
        var validated = await ValidateAsync(request, excludingId: id, cancellationToken);

        var service = await LoadAsync(id, cancellationToken);

        service.ServiceCategoryId = validated.Category.Id;
        service.ServiceCategory = validated.Category;
        service.Name = validated.Name;
        service.Description = validated.Description;
        service.DurationMinutes = request.DurationMinutes;
        service.Price = request.Price;
        service.IsActive = request.IsActive;
        service.IsOnlineBookable = request.IsOnlineBookable;
        service.SortOrder = request.SortOrder;

        AssignStaff(service, validated.Staff);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Map(service);
    }

    /// <summary>
    /// Deactivating hides the service from new bookings while leaving every past
    /// appointment pointing at it. Activating again is only allowed once somebody
    /// can actually perform the service.
    /// </summary>
    public async Task<ServiceDto> SetStatusAsync(
        Guid id,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        var service = await LoadAsync(id, cancellationToken);

        if (isActive && !HasActiveStaff(service))
        {
            throw new ValidationFailedException(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["eligibleStaffIds"] =
                [
                    "Assign at least one active staff member who can perform this service before activating it."
                ]
            });
        }

        service.IsActive = isActive;

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Map(service);
    }

    public async Task<IReadOnlyList<StaffOptionDto>> GetSelectableStaffAsync(
        bool includeInactive,
        CancellationToken cancellationToken = default)
    {
        var staff = await staffDirectory.GetSelectableAsync(includeInactive, cancellationToken);

        return [.. staff.Select(member => new StaffOptionDto(
            member.Id,
            member.FirstName,
            member.LastName,
            member.JobTitle,
            member.IsActive))];
    }

    private async Task<Service> LoadAsync(Guid id, CancellationToken cancellationToken)
    {
        return await services.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException($"Service '{id}' was not found.");
    }

    private static bool HasActiveStaff(Service service) =>
        service.StaffServices.Any(staffService => staffService.Staff.IsActive);

    /// <summary>
    /// Replaces the whole assignment set, which is what the form submits.
    /// </summary>
    /// <summary>
    /// Brings the assignment set in line with the form submission. Existing rows
    /// that are still wanted are left alone, so editing a service never rewrites
    /// the history of an assignment that did not change.
    /// </summary>
    private static void AssignStaff(Service service, IReadOnlyList<Staff> staff)
    {
        var wanted = staff.Select(member => member.Id).ToHashSet();
        var assigned = service.StaffServices.Select(line => line.StaffId).ToHashSet();

        foreach (var line in service.StaffServices.Where(line => !wanted.Contains(line.StaffId)).ToList())
        {
            service.StaffServices.Remove(line);
        }

        foreach (var member in staff.Where(member => !assigned.Contains(member.Id)))
        {
            service.StaffServices.Add(new StaffService
            {
                Service = service,
                Staff = member
            });
        }
    }

    /// <summary>
    /// Field validation runs first so one bad request reports every problem at
    /// once. The duplicate-name check needs a clean name and an existing category
    /// before it means anything, so it only runs once the rest is valid — which
    /// also makes 400 win over 409 when both apply.
    /// </summary>
    private async Task<ValidatedService> ValidateAsync(
        SaveServiceRequest request,
        Guid? excludingId,
        CancellationToken cancellationToken)
    {
        var errors = new ValidationErrorBuilder();

        var name = request.Name?.Trim() ?? string.Empty;

        if (name.Length == 0)
        {
            errors.Add("name", "Service name is required.");
        }
        else if (name.Length > ServiceCatalogueLimits.ServiceNameMaxLength)
        {
            errors.Add(
                "name",
                $"Service name must be {ServiceCatalogueLimits.ServiceNameMaxLength} characters or fewer.");
        }

        var description = NormalizeDescription(request.Description);

        if (request.Description?.Length > ServiceCatalogueLimits.ServiceDescriptionMaxLength)
        {
            errors.Add(
                "description",
                $"Description must be {ServiceCatalogueLimits.ServiceDescriptionMaxLength} characters or fewer.");
        }

        ServiceCategory? category = null;
        var requestedCategoryId = request.CategoryId is { } id && id != Guid.Empty ? id : (Guid?)null;

        if (requestedCategoryId is null)
        {
            errors.Add("categoryId", "Choose a category for this service.");
        }
        else
        {
            category = await categories.GetByIdAsync(requestedCategoryId.Value, cancellationToken);

            if (category is null)
            {
                errors.Add("categoryId", "The selected category no longer exists. Choose another one.");
            }
        }

        if (request.DurationMinutes <= 0)
        {
            errors.Add("durationMinutes", "Duration must be greater than zero minutes.");
        }
        else if (request.DurationMinutes > ServiceCatalogueLimits.MaximumDurationMinutes)
        {
            errors.Add(
                "durationMinutes",
                $"Duration cannot be more than {ServiceCatalogueLimits.MaximumDurationMinutes} minutes.");
        }

        if (request.Price < 0m)
        {
            errors.Add("price", "Price cannot be negative.");
        }
        else if (request.Price > ServiceCatalogueLimits.MaximumPrice)
        {
            errors.Add("price", $"Price cannot be more than {ServiceCatalogueLimits.MaximumPrice:N2}.");
        }
        else if (request.Price != decimal.Round(request.Price, 2))
        {
            errors.Add("price", "Price can have at most two decimal places.");
        }

        if (request.SortOrder < 0)
        {
            errors.Add("sortOrder", "Display position cannot be negative.");
        }

        var requestedStaffIds = NormalizeStaffIds(request.EligibleStaffIds);

        IReadOnlyList<Staff> foundStaff = requestedStaffIds.Count == 0
            ? []
            : await staffDirectory.GetByIdsAsync(requestedStaffIds, cancellationToken);

        var foundIds = foundStaff.Select(member => member.Id).ToHashSet();
        var unknownCount = requestedStaffIds.Count(staffId => !foundIds.Contains(staffId));

        if (unknownCount > 0)
        {
            errors.Add(
                "eligibleStaffIds",
                $"{unknownCount} of the selected staff members no longer exist. Remove them and try again.");
        }

        var activeStaff = foundStaff.Where(member => member.IsActive).ToList();

        if (request.IsActive && activeStaff.Count == 0)
        {
            errors.Add(
                "eligibleStaffIds",
                "An active service needs at least one active staff member who can perform it.");
        }

        if (errors.IsValid && category is not null
            && await services.ExistsNameInCategoryAsync(name, category.Id, excludingId, cancellationToken))
        {
            throw new ConflictException(
                $"A service named '{name}' already exists in the '{category.Name}' category.");
        }

        errors.ThrowIfInvalid();

        return new ValidatedService(name, description, category!, foundStaff);
    }

    private static string? NormalizeDescription(string? description)
    {
        var trimmed = description?.Trim();

        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static List<Guid> NormalizeStaffIds(IReadOnlyList<Guid>? staffIds)
    {
        if (staffIds is null)
        {
            return [];
        }

        var seen = new HashSet<Guid>();
        var result = new List<Guid>(staffIds.Count);

        foreach (var staffId in staffIds)
        {
            if (staffId != Guid.Empty && seen.Add(staffId))
            {
                result.Add(staffId);
            }
        }

        return result;
    }

    private static ServiceSummaryDto MapSummary(Service service) => new(
        service.Id,
        service.Name,
        service.ServiceCategoryId,
        service.ServiceCategory?.Name,
        service.DurationMinutes,
        service.Price,
        service.IsActive,
        service.IsOnlineBookable,
        service.CanBeBooked(),
        service.StaffServices.Count(staffService => staffService.Staff.IsActive),
        service.SortOrder,
        service.CreatedAt,
        service.UpdatedAt,
        "Service",
        null,
        null);

    private static ServiceSummaryDto MapBundleSummary(ServiceBundle bundle) => new(
        bundle.Id,
        bundle.Name,
        bundle.ServiceCategoryId,
        bundle.ServiceCategory?.Name,
        bundle.TotalDurationMinutes,
        bundle.Price,
        bundle.IsActive,
        IsOnlineBookable: false,
        bundle.CanBeBooked(),
        EligibleStaffCount: 0,
        bundle.SortOrder,
        bundle.CreatedAt,
        bundle.UpdatedAt,
        "Bundle",
        bundle.Items.Count,
        bundle.StandardTotalPrice);

    private sealed record MenuEntry(int CategorySortOrder, ServiceSummaryDto Summary);

    private static ServiceDto Map(Service service) => new(
        service.Id,
        service.Name,
        service.Description,
        service.ServiceCategoryId,
        service.ServiceCategory?.Name,
        service.ServiceCategory?.IsActive ?? false,
        service.DurationMinutes,
        service.Price,
        service.IsActive,
        service.IsOnlineBookable,
        service.CanBeBooked(),
        service.SortOrder,
        [.. service.StaffServices
            .Select(staffService => staffService.Staff)
            .OrderBy(member => member.FirstName, StringComparer.Ordinal)
            .ThenBy(member => member.LastName, StringComparer.Ordinal)
            .Select(member => new EligibleStaffDto(
                member.Id,
                member.FirstName,
                member.LastName,
                member.JobTitle,
                member.IsActive))],
        service.CreatedAt,
        service.UpdatedAt);

    private static PublicServiceDto MapPublic(Service service) => new(
        service.Id,
        service.Name,
        service.Description,
        service.ServiceCategoryId,
        service.ServiceCategory?.Name,
        service.DurationMinutes,
        service.Price,
        service.IsOnlineBookable,
        service.SortOrder);

    private sealed record ValidatedService(
        string Name,
        string? Description,
        ServiceCategory Category,
        IReadOnlyList<Staff> Staff);
}
