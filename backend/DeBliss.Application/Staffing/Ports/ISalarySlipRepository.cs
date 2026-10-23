using DeBliss.Application.Staffing.Dtos;
using DeBliss.Domain.Staffing;

namespace DeBliss.Application.Staffing.Ports;

public interface ISalarySlipRepository
{
    Task<SalarySlip?> GetAsync(Guid id, CancellationToken ct = default);

    Task<(IReadOnlyList<SalarySlipSummaryDto> Items, int TotalCount)> GetPagedAsync(
        Guid? staffId,
        int? year,
        int page,
        int pageSize,
        CancellationToken ct = default);

    Task<bool> ExistsAsync(Guid staffId, int salaryYear, int salaryMonth, CancellationToken ct = default);

    Task AddAndSaveAsync(SalarySlip slip, CancellationToken ct = default);
}
