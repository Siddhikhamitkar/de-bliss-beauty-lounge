using DeBliss.Application.Notifications.Ports;
using DeBliss.Domain.Notifications;
using Microsoft.EntityFrameworkCore;

namespace DeBliss.Infrastructure.Persistence.Repositories;

/// <summary>
/// Reminder persistence for the reminder engine. DateTimeOffset predicates and
/// ordering run in memory (SQLite cannot translate them); the translatable
/// filters (status, ids, claim checks) narrow the candidate set in the
/// database first. TryClaimAsync is the concurrency gate: a single UPDATE
/// whose WHERE clause decides the winner, so two workers can never hold the
/// same row.
/// </summary>
public sealed class AppointmentReminderRepository(DeBlissDbContext context) : IAppointmentReminderRepository
{
    public async Task<IReadOnlyList<AppointmentReminder>> GetPendingAsync(
        Guid appointmentId,
        CancellationToken ct = default)
    {
        return await context.AppointmentReminders
            .Where(reminder => reminder.AppointmentId == appointmentId
                && reminder.Status == ReminderStatus.Pending)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<AppointmentReminder>> ListForAppointmentAsync(
        Guid appointmentId,
        CancellationToken ct = default)
    {
        var rows = await context.AppointmentReminders
            .AsNoTracking()
            .Include(reminder => reminder.Customer)
            .Include(reminder => reminder.Appointment)
            .Where(reminder => reminder.AppointmentId == appointmentId)
            .ToListAsync(ct);

        return rows
            .OrderBy(reminder => reminder.ScheduledAt)
            .ToList();
    }

    public async Task<IReadOnlyList<AppointmentReminder>> QueryAsync(
        ReminderHistoryFilter filter,
        CancellationToken ct = default)
    {
        var query = context.AppointmentReminders
            .AsNoTracking()
            .Include(reminder => reminder.Customer)
            .Include(reminder => reminder.Appointment)
            .AsQueryable();

        if (filter.Status is { } status)
        {
            query = query.Where(reminder => reminder.Status == status);
        }

        if (filter.ReminderType is { } reminderType)
        {
            query = query.Where(reminder => reminder.ReminderType == reminderType);
        }

        if (filter.AppointmentId is { } appointmentId)
        {
            query = query.Where(reminder => reminder.AppointmentId == appointmentId);
        }

        if (filter.CustomerId is { } customerId)
        {
            query = query.Where(reminder => reminder.CustomerId == customerId);
        }

        var candidates = await query.ToListAsync(ct);

        return candidates
            .Where(reminder => filter.ScheduledFrom is null || reminder.ScheduledAt >= filter.ScheduledFrom.Value)
            .Where(reminder => filter.ScheduledTo is null || reminder.ScheduledAt <= filter.ScheduledTo.Value)
            .OrderByDescending(reminder => reminder.ScheduledAt)
            .Take(filter.Take)
            .ToList();
    }

    public async Task<IReadOnlyList<AppointmentReminder>> ListDueCandidatesAsync(
        DateTimeOffset now,
        DateTimeOffset leaseCutoff,
        int take,
        CancellationToken ct = default)
    {
        var candidates = await context.AppointmentReminders
            .AsNoTracking()
            .Where(reminder => reminder.Status == ReminderStatus.Pending)
            .ToListAsync(ct);

        return candidates
            .Where(reminder => reminder.ScheduledAt <= now)
            .Where(reminder => reminder.NextAttemptAt is null || reminder.NextAttemptAt <= now)
            .Where(reminder => reminder.ClaimedBy is null
                || (reminder.ClaimedAt is not null && reminder.ClaimedAt <= leaseCutoff))
            .OrderBy(reminder => reminder.ScheduledAt)
            .Take(take)
            .ToList();
    }

    public async Task<bool> TryClaimAsync(
        Guid reminderId,
        string workerId,
        DateTimeOffset claimedAt,
        string? expectedClaimedBy,
        CancellationToken ct = default)
    {
        var affected = await context.AppointmentReminders
            .Where(reminder => reminder.Id == reminderId
                && reminder.Status == ReminderStatus.Pending
                && reminder.ClaimedBy == expectedClaimedBy)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(reminder => reminder.ClaimedBy, workerId)
                    .SetProperty(reminder => reminder.ClaimedAt, claimedAt),
                ct);

        return affected == 1;
    }

    public Task<AppointmentReminder?> LoadClaimedAsync(
        Guid reminderId,
        string workerId,
        CancellationToken ct = default)
    {
        return context.AppointmentReminders
            .Include(reminder => reminder.Customer)
            .Include(reminder => reminder.Appointment!)
                .ThenInclude(appointment => appointment.Services)
                    .ThenInclude(line => line.Staff)
            .Include(reminder => reminder.Appointment!)
                .ThenInclude(appointment => appointment.ServiceBundle)
            .FirstOrDefaultAsync(
                reminder => reminder.Id == reminderId
                    && reminder.Status == ReminderStatus.Pending
                    && reminder.ClaimedBy == workerId,
                ct);
    }

    public void Add(AppointmentReminder reminder) => context.AppointmentReminders.Add(reminder);
}
