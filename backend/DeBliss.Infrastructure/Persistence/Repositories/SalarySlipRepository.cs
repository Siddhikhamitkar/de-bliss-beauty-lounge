using DeBliss.Application.Common.Exceptions;
using DeBliss.Application.Staffing.Dtos;
using DeBliss.Application.Staffing.Ports;
using DeBliss.Domain.Staffing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace DeBliss.Infrastructure.Persistence.Repositories;

public sealed class SalarySlipRepository(DeBlissDbContext context) : ISalarySlipRepository
{
    public Task<SalarySlip?> GetAsync(Guid id, CancellationToken ct = default)
    {
        return context.SalarySlips.AsNoTracking().FirstOrDefaultAsync(slip => slip.Id == id, ct);
    }

    public async Task<(IReadOnlyList<SalarySlipSummaryDto> Items, int TotalCount)> GetPagedAsync(
        Guid? staffId,
        int? year,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var query = context.SalarySlips.AsNoTracking();

        if (staffId.HasValue)
        {
            query = query.Where(slip => slip.StaffId == staffId.Value);
        }

        if (year.HasValue)
        {
            query = query.Where(slip => slip.SalaryYear == year.Value);
        }

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(slip => slip.SalaryYear)
            .ThenByDescending(slip => slip.SalaryMonth)
            .ThenByDescending(slip => slip.CreatedAt)
            .ThenBy(slip => slip.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(slip => new SalarySlipSummaryDto(
                slip.Id,
                slip.StaffId,
                slip.EmployeeName,
                slip.SalaryMonth,
                slip.SalaryYear,
                slip.PayDate,
                slip.NetSalary,
                slip.CreatedAt))
            .ToListAsync(ct);

        return (items, totalCount);
    }

    public Task<bool> ExistsAsync(Guid staffId, int salaryYear, int salaryMonth, CancellationToken ct = default)
    {
        return context.SalarySlips.AnyAsync(slip =>
            slip.StaffId == staffId && slip.SalaryYear == salaryYear && slip.SalaryMonth == salaryMonth, ct);
    }

    public async Task AddAndSaveAsync(SalarySlip slip, CancellationToken ct = default)
    {
        context.SalarySlips.Add(slip);

        try
        {
            await context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            context.Entry(slip).State = EntityState.Detached;
            throw new ConflictException("A salary slip already exists for this staff member and salary period.");
        }
    }
}
