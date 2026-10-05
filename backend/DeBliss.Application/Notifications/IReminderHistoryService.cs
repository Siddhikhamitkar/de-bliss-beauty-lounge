using DeBliss.Application.Notifications.Dtos;

namespace DeBliss.Application.Notifications;

/// <summary>Read-only reminder history for the reminders page and the appointment panel.</summary>
public interface IReminderHistoryService
{
    Task<IReadOnlyList<AppointmentReminderDto>> QueryAsync(
        ReminderHistoryQuery query,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AppointmentReminderDto>> ListForAppointmentAsync(
        Guid appointmentId,
        CancellationToken cancellationToken = default);
}
