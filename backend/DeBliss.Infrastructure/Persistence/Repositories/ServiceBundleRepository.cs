using System.Linq.Expressions;
using DeBliss.Application.ServiceCatalogue.Dtos;
using DeBliss.Application.ServiceCatalogue.Ports;
using DeBliss.Domain.Services;
using Microsoft.EntityFrameworkCore;

namespace DeBliss.Infrastructure.Persistence.Repositories;

public sealed class ServiceBundleRepository(DeBlissDbContext context) : IServiceBundleRepository
{
    /// <summary>
    /// Mirrors <see cref="Service.CanBeBooked"/> across every component: a
    /// bundle is only offered when it is active, its category is active, and
    /// each component is active, categorised and staffed.
    /// </summary>
    private static readonly Expression<Func<ServiceBundle, bool>> Bookable = bundle =>
        bundle.IsActive
        && bundle.ServiceCategory != null
        && bundle.ServiceCategory.IsActive
        && bundle.Items.Count >= ServiceBundle.MinimumComponents
        && bundle.Items.All(item =>
            item.Service.IsActive
            && item.Service.ServiceCategory != null
            && item.Service.ServiceCategory.IsActive
            && item.Service.StaffServices.Any(staffService => staffService.Staff.IsActive));

    public Task<ServiceBundle?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return WithNavigations(context.ServiceBundles)
            .FirstOrDefaultAsync(bundle => bundle.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<ServiceBundle>> SearchAsync(
        ServiceQuery query,
        CancellationToken cancellationToken = default)
    {
        return await ApplyFilters(WithNavigations(context.ServiceBundles.AsNoTracking()), query)
            .OrderBy(bundle => bundle.ServiceCategory!.SortOrder)
            .ThenBy(bundle => bundle.SortOrder)
            .ThenBy(bundle => bundle.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ServiceBundle>> GetBookableAsync(CancellationToken cancellationToken = default)
    {
        return await WithNavigations(context.ServiceBundles.AsNoTracking())
            .Where(Bookable)
            .OrderBy(bundle => bundle.ServiceCategory!.SortOrder)
            .ThenBy(bundle => bundle.SortOrder)
            .ThenBy(bundle => bundle.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> ExistsNameInCategoryAsync(
        string name,
        Guid categoryId,
        Guid? excludingId,
        CancellationToken cancellationToken = default)
    {
        var lowered = name.ToLowerInvariant();

        Expression<Func<ServiceBundle, bool>> predicate = excludingId is { } excluded
            ? bundle => bundle.ServiceCategoryId == categoryId
                && bundle.Name.ToLower() == lowered
                && bundle.Id != excluded
            : bundle => bundle.ServiceCategoryId == categoryId
                && bundle.Name.ToLower() == lowered;

        return await context.ServiceBundles.AsNoTracking().AnyAsync(predicate, cancellationToken);
    }

    public void Add(ServiceBundle bundle) => context.ServiceBundles.Add(bundle);

    private static IQueryable<ServiceBundle> WithNavigations(IQueryable<ServiceBundle> query) => query
        .Include(bundle => bundle.ServiceCategory)
        .Include(bundle => bundle.Items)
        .ThenInclude(item => item.Service)
        .ThenInclude(service => service.ServiceCategory)
        .Include(bundle => bundle.Items)
        .ThenInclude(item => item.Service)
        .ThenInclude(service => service.StaffServices)
        .ThenInclude(staffService => staffService.Staff);

    private static IQueryable<ServiceBundle> ApplyFilters(IQueryable<ServiceBundle> query, ServiceQuery normalized)
    {
        if (normalized.CategoryId is { } categoryId)
        {
            query = query.Where(bundle => bundle.ServiceCategoryId == categoryId);
        }

        if (normalized.Search is { Length: > 0 } term)
        {
            var lowered = term.ToLowerInvariant();

            query = query.Where(bundle =>
                bundle.Name.ToLower().Contains(lowered)
                || (bundle.Description != null && bundle.Description.ToLower().Contains(lowered)));
        }

        query = normalized.Status switch
        {
            ServiceStatusFilter.Active => query.Where(bundle => bundle.IsActive),
            ServiceStatusFilter.Inactive => query.Where(bundle => !bundle.IsActive),
            _ => query
        };

        return normalized.BookableOnly ? query.Where(Bookable) : query;
    }
}
