using DeBliss.Application.ServiceCatalogue.Dtos;
using DeBliss.Application.Staffing.Dtos;

namespace DeBliss.Application.Staffing;

/// <summary>
/// Answers "who can take this slot?" for the future appointment calendar. A slot
/// is available when the staff member is active, is assigned to the requested
/// service, their working pattern covers the slot (gaps between intervals are
/// breaks and exclude it), and nothing dated — time off or a blocked period —
/// overlaps it. The appointment module will add its own conflict source here
/// later; callers should depend on this interface, not on the checks inside it.
/// </summary>
public interface IStaffAvailabilityService
{
    /// <summary>
    /// Whether one member can take the window. When excludeAppointmentId is set
    /// (a reschedule), the appointment being moved is ignored as a conflict
    /// source.
    /// </summary>
    Task<StaffAvailabilityCheckDto> CheckAsync(Guid staffId, DateTimeOffset start, DateTimeOffset end, Guid? excludeAppointmentId = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StaffOptionDto>> GetAvailableStaffAsync(Guid serviceId, DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken = default);
}
