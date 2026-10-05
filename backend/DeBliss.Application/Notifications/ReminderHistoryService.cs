using DeBliss.Application.Common.Exceptions;
using DeBliss.Application.Notifications.Dtos;
using DeBliss.Application.Notifications.Ports;
using DeBliss.Domain.Notifications;

namespace DeBliss.Application.Notifications;

/// <summary>
/// Read-only reminder history for the operations screens. Status and type
/// filters are validated against the enums so a typo is a 400 rather than a
/// silently empty list.
/// </summary>
public sealed class ReminderHistoryService(IAppointmentReminderRepository reminders) : IReminderHistoryService
{
    public const int DefaultTake = 100;

    public const int MaxTake = 500;

    public async Task<IReadOnlyList<AppointmentReminderDto>> QueryAsync(
        ReminderHistoryQuery query,
        CancellationToken cancellationToken = default)
    {
        var status = ParseEnum<ReminderStatus>(query.Status, "status");
        var reminderType = ParseEnum<ReminderType>(query.ReminderType, "reminderType");

        var filter = new ReminderHistoryFilter(
            status,
            reminderType,
            query.AppointmentId,
            query.CustomerId,
            query.ScheduledFrom,
            query.ScheduledTo,
            Math.Clamp(query.Take ?? DefaultTake, 1, MaxTake));

        var rows = await reminders.QueryAsync(filter, cancellationToken);

        return rows.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<AppointmentReminderDto>> ListForAppointmentAsync(
        Guid appointmentId,
        CancellationToken cancellationToken = default)
    {
        var rows = await reminders.ListForAppointmentAsync(appointmentId, cancellationToken);

        return rows.Select(Map).ToList();
    }

    private static TEnum? ParseEnum<TEnum>(string? value, string field)
        where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!Enum.TryParse<TEnum>(value.Trim(), ignoreCase: true, out var parsed))
        {
            throw new ValidationFailedException(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [field] = [$"'{value}' is not a recognised {typeof(TEnum).Name}."]
            });
        }

        return parsed;
    }

    private static AppointmentReminderDto Map(AppointmentReminder row)
    {
        var customerName = row.Customer is { } customer
            ? $"{customer.FirstName} {customer.LastName}".Trim()
            : "Customer";

        return new AppointmentReminderDto(
            row.Id,
            row.AppointmentId,
            row.CustomerId,
            customerName,
            row.Appointment?.StartsAt,
            row.ReminderType.ToString(),
            row.Channel.ToString(),
            row.Status.ToString(),
            row.ScheduledAt,
            row.SentAt,
            row.AttemptCount,
            row.LastAttemptAt,
            row.ProviderMessageId,
            row.FailureReason,
            row.CreatedAt);
    }
}
