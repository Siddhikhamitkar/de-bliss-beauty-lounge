using System.Linq.Expressions;
using DeBliss.Application.ServiceCatalogue.Dtos;
using DeBliss.Application.ServiceCatalogue.Ports;
using DeBliss.Domain.Services;
using Microsoft.EntityFrameworkCore;

namespace DeBliss.Infrastructure.Persistence.Repositories;

public sealed class ServiceCategoryRepository(DeBlissDbContext context) : IServiceCategoryRepository
{
    public Task<ServiceCategory?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        // Tracked: the application service mutates the returned entity and saves.
        return context.ServiceCategories.FirstOrDefaultAsync(category => category.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<ServiceCategory>> QueryAsync(
        ServiceStatusFilter status,
        CancellationToken cancellationToken = default)
    {
        var query = context.ServiceCategories.AsNoTracking();

        query = status switch
        {
            ServiceStatusFilter.Active => query.Where(category => category.IsActive),
            ServiceStatusFilter.Inactive => query.Where(category => !category.IsActive),
            _ => query
        };

        return await query
            .OrderBy(category => category.SortOrder)
            .ThenBy(category => category.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> ExistsNameAsync(
        string name,
        Guid? excludingId,
        CancellationToken cancellationToken = default)
    {
        // Lowercased on both sides instead of relying on the column collation, so
        // the rule reads the same on SQL Server as it does on the SQLite test provider.
        var lowered = name.ToLowerInvariant();

        Expression<Func<ServiceCategory, bool>> predicate = excludingId is { } excluded
            ? category => category.Name.ToLower() == lowered && category.Id != excluded
            : category => category.Name.ToLower() == lowered;

        return await context.ServiceCategories.AsNoTracking().AnyAsync(predicate, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, int>> CountServicesAsync(CancellationToken cancellationToken = default)
    {
        var counts = await context.Services
            .AsNoTracking()
            .Where(service => service.ServiceCategoryId != null)
            .GroupBy(service => service.ServiceCategoryId!.Value)
            .Select(group => new { CategoryId = group.Key, Total = group.Count() })
            .ToListAsync(cancellationToken);

        return counts.ToDictionary(count => count.CategoryId, count => count.Total);
    }

    public void Add(ServiceCategory category) => context.ServiceCategories.Add(category);
}
