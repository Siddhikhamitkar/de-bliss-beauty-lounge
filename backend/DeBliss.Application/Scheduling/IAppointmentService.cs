using DeBliss.Application.Scheduling.Dtos;

namespace DeBliss.Application.Scheduling;

public interface IAppointmentService
{
    Task<AppointmentDto> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<AppointmentDto> CreateAsync(SaveAppointmentRequest request, CancellationToken cancellationToken = default);

    Task<AppointmentDto> UpdateAsync(Guid id, SaveAppointmentRequest request, CancellationToken cancellationToken = default);

    /// <summary>Applies one drag or resize: move time, reassign staff, or change duration — any subset.</summary>
    Task<AppointmentDto> UpdateScheduleAsync(Guid id, UpdateScheduleRequest request, CancellationToken cancellationToken = default);

    Task<AppointmentDto> SetStatusAsync(Guid id, SetAppointmentStatusRequest request, CancellationToken cancellationToken = default);
}
