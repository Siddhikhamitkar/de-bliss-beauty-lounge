using DeBliss.Application.Common.Paging;
using DeBliss.Application.Staffing.Dtos;

namespace DeBliss.Application.Staffing;

public interface ISalarySlipService
{
    Task<StaffSalaryDetails> GetStaffSalaryAsync(Guid staffId, CancellationToken cancellationToken = default);

    Task<PagedResult<SalarySlipSummaryDto>> GetPagedAsync(
        Guid? staffId,
        int? year,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<SalarySlipDto> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<(SalarySlipDto Slip, byte[] Pdf)> GetPdfAsync(Guid id, CancellationToken cancellationToken = default);

    Task<PagedResult<StaffSalarySlipSummaryDto>> GetForStaffAsync(
        Guid staffId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<(StaffSalarySlipSummaryDto Slip, byte[] Pdf)> GetPdfForStaffAsync(
        Guid staffId,
        Guid id,
        CancellationToken cancellationToken = default);

    Task<SalarySlipPreview> PreviewAsync(
        SalarySlipRequest request,
        Guid createdBy,
        CancellationToken cancellationToken = default);

    Task<SalarySlipDto> SaveAsync(
        SaveSalarySlipRequest request,
        Guid createdBy,
        CancellationToken cancellationToken = default);
}
