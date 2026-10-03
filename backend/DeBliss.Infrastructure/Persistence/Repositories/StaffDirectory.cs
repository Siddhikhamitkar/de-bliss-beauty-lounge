using DeBliss.Application.ServiceCatalogue.Ports;
using DeBliss.Domain.Staffing;
using Microsoft.EntityFrameworkCore;

namespace DeBliss.Infrastructure.Persistence.Repositories;

/// <summary>
/// Read-only staff lookups for the service module. Tracked results are deliberate:
/// the application service attaches these instances to the StaffService rows it
/// saves, so the same context must already know about them.
/// </summary>
public sealed class StaffDirectory(DeBlissDbContext context) : IStaffDirectory
{
    public async Task<IReadOnlyList<Staff>> GetSelectableAsync(
        bool includeInactive,
        CancellationToken cancellationToken = default)
    {
        var query = context.Staff.AsQueryable();

        if (!includeInactive)
        {
            query = query.Where(member => member.IsActive);
        }

        return await query
            .OrderBy(member => member.SortOrder)
            .ThenBy(member => member.FirstName)
            .ThenBy(member => member.LastName)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Staff>> GetByIdsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        return await context.Staff
            .Where(member => ids.Contains(member.Id))
            .ToListAsync(cancellationToken);
    }
}
