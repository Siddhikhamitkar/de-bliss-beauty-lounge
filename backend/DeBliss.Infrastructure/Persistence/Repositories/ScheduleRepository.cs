using DeBliss.Application.Staffing.Ports;
using DeBliss.Domain.Scheduling;
using DeBliss.Domain.Staffing;
using Microsoft.EntityFrameworkCore;

namespace DeBliss.Infrastructure.Persistence.Repositories;

/// <summary>
/// Working-hour and time-off persistence. Working hours are read per staff
/// member; the weekly replacement removes rows through the tracked staff
/// entity, so writes here are time-off only.
/// </summary>
public sealed class ScheduleRepository(DeBlissDbContext context) : IScheduleRepository
{
    public async Task<IReadOnlyList<StaffWorkingHour>> GetWorkingHoursAsync(
        Guid staffId,
        CancellationToken ct = default)
    {
        return await context.StaffWorkingHours
            .Where(row => row.StaffId == staffId)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<StaffTimeOff>> GetTimeOffAsync(
        Guid staffId,
        CancellationToken ct = default)
    {
        var entries = await context.StaffTimeOff
            .Where(entry => entry.StaffId == staffId)
            .ToListAsync(ct);

        // SQLite cannot order DateTimeOffset columns; the per-member list is
        // small, so newest-first is applied in memory.
        return entries.OrderByDescending(entry => entry.StartsAt).ToList();
    }

    public Task<StaffTimeOff?> GetTimeOffByIdAsync(Guid id, CancellationToken ct = default)
    {
        return context.StaffTimeOff.FirstOrDefaultAsync(entry => entry.Id == id, ct);
    }

    public void AddTimeOff(StaffTimeOff timeOff) => context.StaffTimeOff.Add(timeOff);

    public void RemoveTimeOff(StaffTimeOff timeOff) => context.StaffTimeOff.Remove(timeOff);

    public async Task<IReadOnlyList<StaffTimeOff>> GetOverlappingTimeOffAsync(
        Guid staffId,
        DateTimeOffset start,
        DateTimeOffset end,
        CancellationToken ct = default)
    {
        var candidates = await context.StaffTimeOff
            .AsNoTracking()
            .Where(entry => entry.StaffId == staffId)
            .ToListAsync(ct);

        // DateTimeOffset comparisons do not translate to SQLite; overlap
        // volumes per member are small, so the predicate runs in memory.
        return candidates.Where(entry => entry.StartsAt < end && entry.EndsAt > start).ToList();
    }

    public async Task<IReadOnlyList<BlockedTime>> GetOverlappingBlockedTimesAsync(
        Guid staffId,
        DateTimeOffset start,
        DateTimeOffset end,
        CancellationToken ct = default)
    {
        var candidates = await context.BlockedTime
            .AsNoTracking()
            .Where(block => block.StaffId == staffId || block.StaffId == null)
            .ToListAsync(ct);

        return candidates.Where(block => block.StartsAt < end && block.EndsAt > start).ToList();
    }
}
