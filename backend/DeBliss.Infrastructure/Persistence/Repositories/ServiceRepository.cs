using System.Linq.Expressions;
using DeBliss.Application.ServiceCatalogue.Dtos;
using DeBliss.Application.ServiceCatalogue.Ports;
using DeBliss.Domain.Services;
using Microsoft.EntityFrameworkCore;

namespace DeBliss.Infrastructure.Persistence.Repositories;

public sealed class ServiceRepository(DeBlissDbContext context) : IServiceRepository
{
    /// <summary>
    /// The rule that decides what the appointment module may offer. Kept as an
    /// expression so it becomes part of the WHERE clause rather than a filter
    /// applied after the rows come back.
    /// </summary>
    private static readonly Expression<Func<Service, bool>> Bookable = service =>
        service.IsActive
        && service.ServiceCategory != null
        && service.ServiceCategory.IsActive
        && service.StaffServices.Any(staffService => staffService.Staff.IsActive);

    public Task<Service?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        // Tracked, with everything the application service needs in order to edit
        // and remap the service without a second round trip.
        return WithNavigations(context.Services)
            .FirstOrDefaultAsync(service => service.Id == id, cancellationToken);
    }

    public async Task<(IReadOnlyList<Service> Items, int TotalCount)> SearchAsync(
        ServiceQuery query,
        CancellationToken cancellationToken = default)
    {
        var filtered = ApplyFilters(WithNavigations(context.Services.AsNoTracking()), query);

        var totalCount = await filtered.CountAsync(cancellationToken);

        var items = await filtered
            .OrderBy(service => service.ServiceCategory!.SortOrder)
            .ThenBy(service => service.SortOrder)
            .ThenBy(service => service.Name)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<IReadOnlyList<Service>> GetBookableAsync(CancellationToken cancellationToken = default)
    {
        return await WithNavigations(context.Services.AsNoTracking())
            .Where(Bookable)
            .OrderBy(service => service.ServiceCategory!.SortOrder)
            .ThenBy(service => service.SortOrder)
            .ThenBy(service => service.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> ExistsNameInCategoryAsync(
        string name,
        Guid categoryId,
        Guid? excludingId,
        CancellationToken cancellationToken = default)
    {
        var lowered = name.ToLowerInvariant();

        Expression<Func<Service, bool>> predicate = excludingId is { } excluded
            ? service => service.ServiceCategoryId == categoryId
                && service.Name.ToLower() == lowered
                && service.Id != excluded
            : service => service.ServiceCategoryId == categoryId
                && service.Name.ToLower() == lowered;

        return await context.Services.AsNoTracking().AnyAsync(predicate, cancellationToken);
    }

    public void Add(Service service) => context.Services.Add(service);

    public async Task<IReadOnlyList<Service>> GetByIdsAsync(
        IReadOnlyList<Guid> ids,
        CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        // Tracked with categories, because the staffing module attaches these
        // instances to the StaffService rows it saves.
        return await context.Services
            .Include(service => service.ServiceCategory)
            .Where(service => ids.Contains(service.Id))
            .ToListAsync(cancellationToken);
    }

    private static IQueryable<Service> WithNavigations(IQueryable<Service> query) => query
        .Include(service => service.ServiceCategory)
        .Include(service => service.StaffServices)
        .ThenInclude(staffService => staffService.Staff);

    private static IQueryable<Service> ApplyFilters(IQueryable<Service> query, ServiceQuery normalized)
    {
        if (normalized.CategoryId is { } categoryId)
        {
            query = query.Where(service => service.ServiceCategoryId == categoryId);
        }

        if (normalized.Search is { Length: > 0 } term)
        {
            var lowered = term.ToLowerInvariant();

            query = query.Where(service =>
                service.Name.ToLower().Contains(lowered)
                || (service.Description != null && service.Description.ToLower().Contains(lowered)));
        }

        query = normalized.Status switch
        {
            ServiceStatusFilter.Active => query.Where(service => service.IsActive),
            ServiceStatusFilter.Inactive => query.Where(service => !service.IsActive),
            _ => query
        };

        return normalized.BookableOnly ? query.Where(Bookable) : query;
    }
}
