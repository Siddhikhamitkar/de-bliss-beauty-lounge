using DeBliss.Application.Common.Paging;
using DeBliss.Application.ServiceCatalogue.Dtos;

namespace DeBliss.Application.ServiceCatalogue;

/// <summary>
/// Manages the bookable services on the salon's service menu, including which
/// staff members may perform each one.
/// </summary>
public interface IServiceCatalogueService
{
    Task<PagedResult<ServiceSummaryDto>> SearchAsync(ServiceQuery query, CancellationToken cancellationToken = default);

    Task<ServiceDto> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// The projection the future appointment calendar will consume: only
    /// services that can actually be booked right now, with their duration,
    /// current price and eligible staff.
    /// </summary>
    Task<IReadOnlyList<ServiceDto>> GetBookableAsync(CancellationToken cancellationToken = default);

    Task<ServiceDto> CreateAsync(SaveServiceRequest request, CancellationToken cancellationToken = default);

    Task<ServiceDto> UpdateAsync(Guid id, SaveServiceRequest request, CancellationToken cancellationToken = default);

    Task<ServiceDto> SetStatusAsync(Guid id, bool isActive, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StaffOptionDto>> GetSelectableStaffAsync(bool includeInactive, CancellationToken cancellationToken = default);
}
