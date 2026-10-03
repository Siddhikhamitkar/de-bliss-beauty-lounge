using DeBliss.Application.Common.Paging;
using DeBliss.Application.Staffing.Dtos;

namespace DeBliss.Application.Staffing;

/// <summary>
/// Manages staff profiles: who works at the salon, whether they are active, and
/// which services they can perform. Staff are never physically deleted — a
/// leaver is deactivated so history (appointments, schedules, time off) keeps
/// resolving to a real person.
/// </summary>
public interface IStaffService
{
    Task<PagedResult<StaffSummaryDto>> SearchAsync(StaffQuery query, CancellationToken cancellationToken = default);

    Task<StaffDto> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<StaffDto> CreateAsync(SaveStaffRequest request, CancellationToken cancellationToken = default);

    Task<StaffDto> UpdateAsync(Guid id, SaveStaffRequest request, CancellationToken cancellationToken = default);

    Task<StaffDto> SetStatusAsync(Guid id, bool isActive, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StaffServiceDto>> GetServicesAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Replaces the whole set of services the staff member can perform.</summary>
    Task<IReadOnlyList<StaffServiceDto>> SetServicesAsync(Guid id, SetStaffServicesRequest request, CancellationToken cancellationToken = default);
}
