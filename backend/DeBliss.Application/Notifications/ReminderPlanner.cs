using DeBliss.Application.Notifications.Ports;
using DeBliss.Domain.Notifications;
using DeBliss.Domain.Scheduling;

namespace DeBliss.Application.Notifications;

/// <summary>
/// Turns appointment lifecycle events into rows in the reminder work queue.
/// Every booking, change and status transition funnels through here so the
/// rules live in exactly one place and the dispatch worker only ever executes
/// rows that were planned.
/// </summary>
public class ReminderPlanner
{
    private readonly IReminderRuleRepository _rules;
    private readonly IAppointmentReminderRepository _reminders;
    private readonly TimeProvider _timeProvider;

    public ReminderPlanner(
        IReminderRuleRepository rules,
        IAppointmentReminderRepository reminders,
        TimeProvider timeProvider)
    {
        _rules = rules;
        _reminders = reminders;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Plans the messages for a newly created appointment: a confirmation and
    /// the enabled timed reminders. Appointments without a customer record
    /// cannot be messaged (consent lives on the customer), and timings that
    /// already passed are skipped rather than sent late.
    /// </summary>
    public async Task PlanForBookingAsync(Appointment appointment, CancellationToken cancellationToken = default)
    {
        if (appointment.CustomerId is null)
        {
            return;
        }

        var existing = (await _reminders.GetPendingAsync(appointment.Id, cancellationToken)).ToList();
        var enabledRules = (await _rules.ListAsync(cancellationToken))
            .Where(rule => rule.IsEnabled)
            .ToList();

        foreach (var rule in enabledRules)
        {
            if (rule.ReminderType == ReminderType.Confirmation)
            {
                AddEventRow(appointment, ReminderType.Confirmation, existing);
            }
            else if (rule.ReminderType == ReminderType.Reminder)
            {
                AddTimedRow(appointment, rule.OffsetMinutes, existing);
            }
        }
    }

    /// <summary>
    /// Re-aligns pending rows after the customer or the time of an appointment
    /// changed. A pure customer change re-points the rows in place; a time
    /// change drops the stale timed reminders and rebuilds them for the new
    /// slot, then collapses the outgoing reschedule notice with any still
    /// pending one. Attaching a customer to a walk-in booking plans the normal
    /// set, because that appointment could not be messaged before.
    /// </summary>
    public async Task ApplyChangeAsync(
        Appointment appointment,
        DateTimeOffset? previousStartsAt,
        Guid? previousCustomerId,
        CancellationToken cancellationToken = default)
    {
        var customerChanged = appointment.CustomerId != previousCustomerId;
        var timeChanged = previousStartsAt is not null && appointment.StartsAt != previousStartsAt.Value;

        if (!customerChanged && !timeChanged)
        {
            return;
        }

        var pending = (await _reminders.GetPendingAsync(appointment.Id, cancellationToken)).ToList();

        if (appointment.CustomerId is null)
        {
            CancelRows(pending, ReminderFailureReasons.CustomerUnlinked);
            return;
        }

        var enabledRules = (await _rules.ListAsync(cancellationToken))
            .Where(rule => rule.IsEnabled)
            .ToList();

        if (previousCustomerId is null)
        {
            // A walk-in booking that just gained a customer record: nothing
            // could be planned before, so plan the normal set for the current
            // state. Terminal appointments stay silent.
            if (IsTerminal(appointment.Status))
            {
                return;
            }

            if (enabledRules.Any(rule => rule.ReminderType == ReminderType.Confirmation))
            {
                AddEventRow(appointment, ReminderType.Confirmation, pending);
            }

            foreach (var rule in enabledRules.Where(rule => rule.ReminderType == ReminderType.Reminder))
            {
                AddTimedRow(appointment, rule.OffsetMinutes, pending);
            }

            return;
        }

        if (customerChanged)
        {
            foreach (var row in pending)
            {
                row.CustomerId = appointment.CustomerId.Value;
            }
        }

        if (!timeChanged)
        {
            return;
        }

        // Timed reminders keyed on the old start are no longer valid.
        var stale = pending.Where(row => row.ReminderType == ReminderType.Reminder).ToList();
        CancelRows(stale, ReminderFailureReasons.AppointmentRescheduled);
        pending.RemoveAll(row => row.ReminderType == ReminderType.Reminder);

        foreach (var rule in enabledRules.Where(rule => rule.ReminderType == ReminderType.Reminder))
        {
            AddTimedRow(appointment, rule.OffsetMinutes, pending);
        }

        if (enabledRules.Any(rule => rule.ReminderType == ReminderType.Rescheduled))
        {
            // Collapses onto a still-pending notice from an earlier move: the
            // customer only needs the final time.
            AddEventRow(appointment, ReminderType.Rescheduled, pending);
        }
    }

    /// <summary>
    /// Reacts to a status transition: confirmation (re)plans the message set,
    /// cancellation swaps the pending set for a cancellation notice, and the
    /// terminal states drop everything that is still pending.
    /// </summary>
    public async Task ApplyStatusChangeAsync(
        Appointment appointment,
        AppointmentStatus from,
        AppointmentStatus to,
        CancellationToken cancellationToken = default)
    {
        if (to == from || appointment.CustomerId is null)
        {
            return;
        }

        switch (to)
        {
            case AppointmentStatus.Confirmed:
                // Covers both Pending → Confirmed and Waitlisted → Confirmed.
                await PlanForBookingAsync(appointment, cancellationToken);
                return;

            case AppointmentStatus.Cancelled:
            {
                var pending = (await _reminders.GetPendingAsync(appointment.Id, cancellationToken)).ToList();
                CancelRows(pending, ReminderFailureReasons.AppointmentCancelled);

                var cancellationRuleEnabled = (await _rules.ListAsync(cancellationToken))
                    .Any(rule => rule.IsEnabled && rule.ReminderType == ReminderType.Cancellation);

                if (cancellationRuleEnabled)
                {
                    AddEventRow(appointment, ReminderType.Cancellation, pending);
                }

                return;
            }

            case AppointmentStatus.Completed:
            case AppointmentStatus.NoShow:
            {
                var pending = (await _reminders.GetPendingAsync(appointment.Id, cancellationToken)).ToList();
                var reason = to == AppointmentStatus.Completed
                    ? ReminderFailureReasons.AppointmentCompleted
                    : ReminderFailureReasons.AppointmentNoShow;
                CancelRows(pending, reason);
                return;
            }
        }
    }

    /// <summary>
    /// Queues a timed reminder for the appointment, unless its trigger time
    /// already passed (a booking made inside the reminder window) or an
    /// identical row is already planned.
    /// </summary>
    private void AddTimedRow(Appointment appointment, int offsetMinutes, List<AppointmentReminder> planned)
    {
        var scheduledAt = appointment.StartsAt.AddMinutes(-offsetMinutes);

        if (scheduledAt <= _timeProvider.GetUtcNow())
        {
            return;
        }

        var duplicate = planned.Any(row =>
            row.ReminderType == ReminderType.Reminder && row.ScheduledAt == scheduledAt);

        if (duplicate)
        {
            return;
        }

        var row = CreateRow(appointment, ReminderType.Reminder, scheduledAt);
        _reminders.Add(row);
        planned.Add(row);
    }

    /// <summary>
    /// Queues an event-based notice due immediately. Only one pending row of
    /// the same type is kept: re-confirming or re-rescheduling an appointment
    /// must not stack notices.
    /// </summary>
    private void AddEventRow(Appointment appointment, ReminderType reminderType, List<AppointmentReminder> planned)
    {
        var duplicate = planned.Any(row => row.ReminderType == reminderType && row.Status == ReminderStatus.Pending);

        if (duplicate)
        {
            return;
        }

        var row = CreateRow(appointment, reminderType, _timeProvider.GetUtcNow());
        _reminders.Add(row);
        planned.Add(row);
    }

    private static bool IsTerminal(AppointmentStatus status) =>
        status is AppointmentStatus.Completed or AppointmentStatus.Cancelled or AppointmentStatus.NoShow;

    private static AppointmentReminder CreateRow(
        Appointment appointment,
        ReminderType reminderType,
        DateTimeOffset scheduledAt) => new()
    {
        AppointmentId = appointment.Id,
        CustomerId = appointment.CustomerId!.Value,
        ReminderType = reminderType,
        Status = ReminderStatus.Pending,
        Channel = NotificationChannel.WhatsApp,
        ScheduledAt = scheduledAt,
        AttemptCount = 0
    };

    private static void CancelRows(IEnumerable<AppointmentReminder> rows, string reason)
    {
        foreach (var row in rows)
        {
            row.Status = ReminderStatus.Cancelled;
            row.FailureReason = reason;
            row.ClaimedBy = null;
            row.ClaimedAt = null;
        }
    }
}
