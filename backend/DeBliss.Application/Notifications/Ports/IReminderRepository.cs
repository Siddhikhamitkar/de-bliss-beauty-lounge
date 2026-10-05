using DeBliss.Domain.Notifications;

namespace DeBliss.Application.Notifications.Ports;

/// <summary>
/// Typed filter for the reminder history query. The enum and id parts are
/// applied in the database; the DateTimeOffset window runs in memory, which is
/// the house rule because SQLite cannot translate DateTimeOffset comparisons.
/// </summary>
public sealed record ReminderHistoryFilter(
    ReminderStatus? Status,
    ReminderType? ReminderType,
    Guid? AppointmentId,
    Guid? CustomerId,
    DateTimeOffset? ScheduledFrom,
    DateTimeOffset? ScheduledTo,
    int Take);

/// <summary>
/// Reminder persistence for the notifications module. The dispatch side is
/// concurrency-critical: the due-window and lease comparisons are
/// DateTimeOffset based and therefore run in memory (SQLite cannot translate
/// them), while the translatable Status filter narrows the candidate set in
/// the database first. Claims are taken atomically so two workers can never
/// hold the same row.
/// </summary>
public interface IAppointmentReminderRepository
{
    /// <summary>Tracked Pending rows for one appointment, so the planner can cancel or re-point them.</summary>
    Task<IReadOnlyList<AppointmentReminder>> GetPendingAsync(Guid appointmentId, CancellationToken ct = default);

    /// <summary>Read-only history for one appointment, oldest first, with customer and appointment attached.</summary>
    Task<IReadOnlyList<AppointmentReminder>> ListForAppointmentAsync(Guid appointmentId, CancellationToken ct = default);

    /// <summary>Read-only filtered history, newest first, with customer and appointment attached.</summary>
    Task<IReadOnlyList<AppointmentReminder>> QueryAsync(ReminderHistoryFilter filter, CancellationToken ct = default);

    /// <summary>
    /// Due Pending rows — unclaimed, or claimed and leased out longer than the
    /// caller's staleness window — oldest first. The caller passes the lease
    /// cut-off so lease length stays a dispatcher policy rather than storage.
    /// </summary>
    Task<IReadOnlyList<AppointmentReminder>> ListDueCandidatesAsync(
        DateTimeOffset now,
        DateTimeOffset leaseCutoff,
        int take,
        CancellationToken ct = default);

    /// <summary>
    /// Atomically claims a row: succeeds only while the row is still Pending
    /// and either unclaimed or still held by <paramref name="expectedClaimedBy"/>
    /// (reclaiming a stale lease). False means another worker won the race.
    /// </summary>
    Task<bool> TryClaimAsync(
        Guid reminderId,
        string workerId,
        DateTimeOffset claimedAt,
        string? expectedClaimedBy,
        CancellationToken ct = default);

    /// <summary>Tracked row (appointment with lines and customer included) if still claimed by this worker.</summary>
    Task<AppointmentReminder?> LoadClaimedAsync(Guid reminderId, string workerId, CancellationToken ct = default);

    void Add(AppointmentReminder reminder);
}

/// <summary>Rule persistence for the reminder settings screen and the planner.</summary>
public interface IReminderRuleRepository
{
    /// <summary>All rules; both the settings screen and the planner read the whole set.</summary>
    Task<IReadOnlyList<ReminderRule>> ListAsync(CancellationToken ct = default);

    /// <summary>Tracked rule, or null for an unknown id.</summary>
    Task<ReminderRule?> GetByIdAsync(Guid id, CancellationToken ct = default);
}
