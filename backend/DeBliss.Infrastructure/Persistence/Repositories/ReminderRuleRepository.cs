using DeBliss.Application.Notifications.Ports;
using DeBliss.Domain.Notifications;
using Microsoft.EntityFrameworkCore;

namespace DeBliss.Infrastructure.Persistence.Repositories;

/// <summary>
/// Rule persistence for the reminder settings screen and the planner. The
/// rule set is tiny and read on every planning decision, so reads are
/// untracked except when the settings screen edits one.
/// </summary>
public sealed class ReminderRuleRepository(DeBlissDbContext context) : IReminderRuleRepository
{
    public async Task<IReadOnlyList<ReminderRule>> ListAsync(CancellationToken ct = default)
    {
        return await context.ReminderRules
            .AsNoTracking()
            .OrderBy(rule => rule.ReminderType)
            .ThenBy(rule => rule.OffsetMinutes)
            .ToListAsync(ct);
    }

    public Task<ReminderRule?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return context.ReminderRules.FirstOrDefaultAsync(rule => rule.Id == id, ct);
    }
}
