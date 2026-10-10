using DeBliss.Application.Scheduling.Dtos;
using DeBliss.Domain.Scheduling;

namespace DeBliss.Application.Scheduling;

public interface IAppointmentService
{
    Task<AppointmentDto> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Books an appointment. The source records the channel; the front desk
    /// creates Reception bookings, and the customer website creates Online
    /// ones through the same validation.
    /// </summary>
    Task<AppointmentDto> CreateAsync(
        SaveAppointmentRequest request,
        CancellationToken cancellationToken = default,
        AppointmentSource source = AppointmentSource.Reception);

    Task<AppointmentDto> UpdateAsync(Guid id, SaveAppointmentRequest request, CancellationToken cancellationToken = default);

    /// <summary>Applies one drag or resize: move time, reassign staff, or change duration — any subset.</summary>
    Task<AppointmentDto> UpdateScheduleAsync(Guid id, UpdateScheduleRequest request, CancellationToken cancellationToken = default);

    Task<AppointmentDto> SetStatusAsync(Guid id, SetAppointmentStatusRequest request, CancellationToken cancellationToken = default);
}
