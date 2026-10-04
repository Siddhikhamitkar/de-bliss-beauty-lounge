using DeBliss.Application.Staffing.Dtos;
using DeBliss.Application.Staffing.Ports;
using DeBliss.Domain.Staffing;
using Microsoft.EntityFrameworkCore;

namespace DeBliss.Infrastructure.Persistence.Repositories;

/// <summary>
/// Staff persistence for the staffing module. GetByIdAsync and
/// GetByIdForScheduleAsync return tracked entities because the application
/// service mutates what it loads; the read-only list and availability queries
/// do not track.
/// </summary>
public sealed class StaffRepository(DeBlissDbContext context) : IStaffRepository
{
    public Task<Staff?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return context.Staff
            .Include(member => member.StaffServices)
            .ThenInclude(line => line.Service)
            .ThenInclude(service => service.ServiceCategory)
            .Include(member => member.WorkingHours)
            .FirstOrDefaultAsync(member => member.Id == id, ct);
    }

    public Task<Staff?> GetByIdForScheduleAsync(Guid id, CancellationToken ct = default)
    {
        return context.Staff
            .Include(member => member.WorkingHours)
            .FirstOrDefaultAsync(member => member.Id == id, ct);
    }

    public async Task<(IReadOnlyList<Staff> Items, int TotalCount)> SearchAsync(
        StaffQuery query,
        CancellationToken ct = default)
    {
        var filtered = ApplyFilters(context.Staff.AsNoTracking(), query);

        var totalCount = await filtered.CountAsync(ct);

        var items = await filtered
            .Include(member => member.StaffServices)
            .ThenInclude(line => line.Service)
            .OrderBy(member => member.SortOrder)
            .ThenBy(member => member.FirstName)
            .ThenBy(member => member.LastName)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);

        return (items, totalCount);
    }

    public async Task<IReadOnlyList<Staff>> GetActiveAsync(CancellationToken ct = default)
    {
        return await context.Staff
            .AsNoTracking()
            .Where(member => member.IsActive)
            .OrderBy(member => member.SortOrder)
            .ThenBy(member => member.FirstName)
            .ThenBy(member => member.LastName)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Staff>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        return await context.Staff
            .AsNoTracking()
            .Where(member => ids.Contains(member.Id))
            .OrderBy(member => member.SortOrder)
            .ThenBy(member => member.FirstName)
            .ThenBy(member => member.LastName)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Staff>> GetActiveAssignedToServiceAsync(
        Guid serviceId,
        CancellationToken ct = default)
    {
        return await context.Staff
            .AsNoTracking()
            .Where(member => member.IsActive && member.StaffServices.Any(line => line.ServiceId == serviceId))
            .Include(member => member.StaffServices)
            .Include(member => member.WorkingHours)
            .OrderBy(member => member.SortOrder)
            .ThenBy(member => member.FirstName)
            .ThenBy(member => member.LastName)
            .ToListAsync(ct);
    }

    public void Add(Staff staff) => context.Staff.Add(staff);

    private static IQueryable<Staff> ApplyFilters(IQueryable<Staff> query, StaffQuery normalized)
    {
        if (normalized.Search is { Length: > 0 } term)
        {
            var lowered = term.ToLowerInvariant();

            query = query.Where(member =>
                member.FirstName.ToLower().Contains(lowered)
                || member.LastName.ToLower().Contains(lowered)
                || (member.JobTitle != null && member.JobTitle.ToLower().Contains(lowered))
                || (member.Email != null && member.Email.ToLower().Contains(lowered)));
        }

        return normalized.Status switch
        {
            StaffStatusFilter.Active => query.Where(member => member.IsActive),
            StaffStatusFilter.Inactive => query.Where(member => !member.IsActive),
            _ => query
        };
    }
}
