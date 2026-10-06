using DeBliss.Application.Staffing.Ports;
using DeBliss.Domain.Staffing;
using Microsoft.EntityFrameworkCore;

namespace DeBliss.Infrastructure.Persistence.Repositories;

/// <summary>
/// Attendance persistence. Both reads use the (StaffId, WorkDate) key the unique
/// index is built on — a single day by seek, a history by one bounded range scan.
/// A day is returned tracked so that check-out can close it through the unit of
/// work like every other write.
/// </summary>
public sealed class StaffAttendanceRepository(DeBlissDbContext context) : IStaffAttendanceRepository
{
    public Task<StaffAttendance?> GetForDateAsync(Guid staffId, DateOnly workDate, CancellationToken ct = default)
    {
        return context.StaffAttendances.FirstOrDefaultAsync(
            entry => entry.StaffId == staffId && entry.WorkDate == workDate,
            ct);
    }

    public async Task<IReadOnlyList<StaffAttendance>> GetInRangeAsync(
        Guid staffId,
        DateOnly from,
        DateOnly to,
        CancellationToken ct = default)
    {
        return await context.StaffAttendances
            .AsNoTracking()
            .Where(entry => entry.StaffId == staffId && entry.WorkDate >= from && entry.WorkDate <= to)
            .OrderByDescending(entry => entry.WorkDate)
            .ToListAsync(ct);
    }

    public void Add(StaffAttendance attendance) => context.StaffAttendances.Add(attendance);
}
