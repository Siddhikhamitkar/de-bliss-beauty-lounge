using DeBliss.Application.Common.Interfaces;
using DeBliss.Application.Notifications.Ports;
using DeBliss.Domain.Notifications;
using DeBliss.Domain.Scheduling;
using Microsoft.Extensions.Logging;

namespace DeBliss.Application.Notifications;

/// <summary>
/// The send loop behind the hosted worker. It picks up due rows, claims them
/// so no two workers can touch the same reminder, re-validates that the
/// message is still wanted (the appointment was not cancelled, the customer
/// still consents), sends through the provider port and persists the outcome
/// with a bounded retry policy. The appointment API never talks to a provider
/// and never waits for this loop.
/// </summary>
public sealed class ReminderDispatchService(
    IAppointmentReminderRepository reminders,
    INotificationProvider provider,
    ReminderMessageComposer composer,
    ReminderWorkerIdentity worker,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    ILogger<ReminderDispatchService> logger) : IReminderDispatchService
{
    /// <summary>Total send attempts allowed per row before it stays Failed.</summary>
    public const int MaxAttempts = 3;

    /// <summary>Rows handled per run; the next run picks up the rest.</summary>
    public const int MaxBatchSize = 50;

    /// <summary>Base retry delay, multiplied by the attempt number.</summary>
    public static readonly TimeSpan RetryBackoff = TimeSpan.FromMinutes(5);

    /// <summary>A claim older than this is treated as abandoned (worker crashed) and may be taken over.</summary>
    public static readonly TimeSpan ClaimLease = TimeSpan.FromMinutes(10);

    private enum Outcome
    {
        Sent,
        Failed,
        Skipped,
        Cancelled,
        Deferred
    }

    public async Task<ReminderDispatchResult> ProcessDueAsync(CancellationToken cancellationToken = default)
    {
        if (!provider.IsConfigured)
        {
            return ReminderDispatchResult.Empty;
        }

        var now = timeProvider.GetUtcNow();

        var candidates = await reminders.ListDueCandidatesAsync(
            now,
            now - ClaimLease,
            MaxBatchSize,
            cancellationToken);

        var sent = 0;
        var failed = 0;
        var skipped = 0;
        var cancelled = 0;
        var deferred = 0;

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Wins only while the row is still Pending and either unclaimed or
            // held by the stale claim we just saw; a lost race means another
            // worker owns it now.
            var claimed = await reminders.TryClaimAsync(
                candidate.Id,
                worker.Id,
                now,
                candidate.ClaimedBy,
                cancellationToken);

            if (!claimed)
            {
                continue;
            }

            var reminder = await reminders.LoadClaimedAsync(candidate.Id, worker.Id, cancellationToken);

            if (reminder is null)
            {
                continue;
            }

            var outcome = await ProcessOneAsync(reminder, now, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            switch (outcome)
            {
                case Outcome.Sent:
                    sent++;
                    break;
                case Outcome.Failed:
                    failed++;
                    break;
                case Outcome.Skipped:
                    skipped++;
                    break;
                case Outcome.Cancelled:
                    cancelled++;
                    break;
                case Outcome.Deferred:
                    deferred++;
                    break;
            }
        }

        var processed = sent + failed + skipped + cancelled + deferred;

        if (processed > 0)
        {
            logger.LogInformation(
                "Reminder dispatch processed {Processed} rows: {Sent} sent, {Failed} failed, {Skipped} skipped, {Cancelled} cancelled, {Deferred} deferred.",
                processed, sent, failed, skipped, cancelled, deferred);
        }

        return new ReminderDispatchResult(sent, failed, skipped, cancelled, deferred);
    }

    private async Task<Outcome> ProcessOneAsync(
        AppointmentReminder reminder,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var appointment = reminder.Appointment;

        if (appointment is null || appointment.DeletedAt is not null)
        {
            return CancelRow(reminder, ReminderFailureReasons.AppointmentDeleted);
        }

        if (appointment.Status is AppointmentStatus.Cancelled
            or AppointmentStatus.Completed
            or AppointmentStatus.NoShow)
        {
            // The cancellation notice itself is the one message that is
            // supposed to go out for a cancelled appointment.
            var isCancellationNotice = reminder.ReminderType == ReminderType.Cancellation
                && appointment.Status == AppointmentStatus.Cancelled;

            if (!isCancellationNotice)
            {
                var reason = appointment.Status switch
                {
                    AppointmentStatus.Completed => ReminderFailureReasons.AppointmentCompleted,
                    AppointmentStatus.NoShow => ReminderFailureReasons.AppointmentNoShow,
                    _ => ReminderFailureReasons.AppointmentCancelled
                };

                return CancelRow(reminder, reason);
            }
        }

        if (reminder.ReminderType == ReminderType.Reminder && appointment.StartsAt <= now)
        {
            return SkipRow(reminder, ReminderFailureReasons.AppointmentStarted);
        }

        var customer = reminder.Customer;

        if (customer is null)
        {
            return FailRow(reminder, ReminderFailureReasons.CustomerRecordMissing);
        }

        if (customer.WhatsAppOptInStatus == WhatsAppOptInStatus.OptedOut)
        {
            return SkipRow(reminder, ReminderFailureReasons.CustomerOptedOut);
        }

        if (customer.WhatsAppOptInStatus != WhatsAppOptInStatus.OptedIn)
        {
            return SkipRow(reminder, ReminderFailureReasons.CustomerNotOptedIn);
        }

        var phone = PhoneNumbers.TryNormalize(customer.PhoneNumber);

        if (phone is null)
        {
            return FailRow(
                reminder,
                string.IsNullOrWhiteSpace(customer.PhoneNumber)
                    ? ReminderFailureReasons.MissingPhoneNumber
                    : ReminderFailureReasons.InvalidPhoneNumber);
        }

        var templateId = provider.ResolveTemplateId(reminder.ReminderType);

        if (templateId is null)
        {
            return FailRow(reminder, ReminderFailureReasons.TemplateNotConfigured);
        }

        var customerName = $"{customer.FirstName} {customer.LastName}".Trim();

        NotificationSendResult result;

        try
        {
            result = await provider.SendNotificationAsync(
                new NotificationMessage(reminder.Channel, phone, templateId, composer.Compose(appointment, customerName)),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            // A provider that throws instead of returning a result is treated
            // as a transient outage; the reason is for operators, and the log
            // line below never carries it.
            result = NotificationSendResult.Transient($"The provider call failed: {exception.Message}");
        }

        reminder.AttemptCount++;
        reminder.LastAttemptAt = now;
        reminder.ClaimedBy = null;
        reminder.ClaimedAt = null;

        if (result.Success)
        {
            reminder.Status = ReminderStatus.Sent;
            reminder.SentAt = now;
            reminder.ProviderMessageId = result.ProviderMessageId;
            reminder.FailureReason = null;
            reminder.NextAttemptAt = null;

            return Outcome.Sent;
        }

        var detail = result.ProviderStatus is { Length: > 0 }
            ? $"{result.FailureReason} ({result.ProviderStatus})"
            : result.FailureReason ?? "The send failed.";

        if (detail.Length > 500)
        {
            detail = detail[..500];
        }

        reminder.FailureReason = detail;

        if (result.FailureKind == NotificationFailureKind.Permanent || reminder.AttemptCount >= MaxAttempts)
        {
            reminder.Status = ReminderStatus.Failed;
            reminder.NextAttemptAt = null;

            logger.LogWarning(
                "Reminder {ReminderId} for appointment {AppointmentId} failed permanently after {Attempts} attempt(s).",
                reminder.Id, reminder.AppointmentId, reminder.AttemptCount);

            return Outcome.Failed;
        }

        reminder.NextAttemptAt = now.Add(RetryBackoff * reminder.AttemptCount);

        logger.LogWarning(
            "Reminder {ReminderId} for appointment {AppointmentId} attempt {Attempts} failed transiently; retrying at {NextAttemptAt}.",
            reminder.Id, reminder.AppointmentId, reminder.AttemptCount, reminder.NextAttemptAt);

        return Outcome.Deferred;
    }

    private static Outcome CancelRow(AppointmentReminder reminder, string reason)
    {
        reminder.Status = ReminderStatus.Cancelled;
        reminder.FailureReason = reason;
        reminder.ClaimedBy = null;
        reminder.ClaimedAt = null;
        reminder.NextAttemptAt = null;

        return Outcome.Cancelled;
    }

    private static Outcome SkipRow(AppointmentReminder reminder, string reason)
    {
        reminder.Status = ReminderStatus.Skipped;
        reminder.FailureReason = reason;
        reminder.ClaimedBy = null;
        reminder.ClaimedAt = null;
        reminder.NextAttemptAt = null;

        return Outcome.Skipped;
    }

    private static Outcome FailRow(AppointmentReminder reminder, string reason)
    {
        reminder.Status = ReminderStatus.Failed;
        reminder.FailureReason = reason;
        reminder.ClaimedBy = null;
        reminder.ClaimedAt = null;
        reminder.NextAttemptAt = null;

        return Outcome.Failed;
    }
}
