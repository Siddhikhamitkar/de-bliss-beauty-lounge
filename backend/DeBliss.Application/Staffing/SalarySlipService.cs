using DeBliss.Application.Common.Exceptions;
using DeBliss.Application.Common.Paging;
using DeBliss.Application.Staffing.Dtos;
using DeBliss.Application.Staffing.Ports;
using DeBliss.Domain.Staffing;

namespace DeBliss.Application.Staffing;

public sealed class SalarySlipService(
    IStaffRepository staff,
    ISalarySlipRepository salarySlips,
    ISalarySlipPdf pdf,
    ISalarySlipPreviewTokens previewTokens,
    TimeProvider timeProvider) : ISalarySlipService
{
    public async Task<StaffSalaryDetails> GetStaffSalaryAsync(
        Guid staffId,
        CancellationToken cancellationToken = default)
    {
        var member = await staff.GetByIdAsync(staffId, cancellationToken)
            ?? throw new NotFoundException("Staff member was not found.");

        return new StaffSalaryDetails(
            member.TotalSalary,
            member.BasicSalary,
            member.OtherAllowances,
            member.IbanNumber,
            member.BankName,
            member.BankAccountNumber);
    }

    public async Task<PagedResult<SalarySlipSummaryDto>> GetPagedAsync(
        Guid? staffId,
        int? year,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var errors = new ValidationErrorBuilder();

        if (staffId == Guid.Empty)
        {
            errors.Add("staffId", "Select a valid staff member.");
        }

        if (year is < 2000 or > 2100)
        {
            errors.Add("year", "Year must be between 2000 and 2100.");
        }

        errors.ThrowIfInvalid();

        pageSize = pageSize <= 0 ? 50 : Math.Min(pageSize, 100);
        page = Math.Clamp(page, 1, int.MaxValue / pageSize);

        var (items, totalCount) = await salarySlips.GetPagedAsync(staffId, year, page, pageSize, cancellationToken);

        return new PagedResult<SalarySlipSummaryDto>(items, totalCount, page, pageSize);
    }

    public async Task<SalarySlipDto> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return Map(await LoadAsync(id, cancellationToken));
    }

    public async Task<(SalarySlipDto Slip, byte[] Pdf)> GetPdfAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var slip = await LoadAsync(id, cancellationToken);

        return (Map(slip), slip.Pdf);
    }

    public async Task<PagedResult<StaffSalarySlipSummaryDto>> GetForStaffAsync(
        Guid staffId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        await RequireStaffAsync(staffId, cancellationToken);

        var result = await GetPagedAsync(staffId, null, page, pageSize, cancellationToken);
        var items = result.Items.Select(slip => new StaffSalarySlipSummaryDto(
            slip.Id, slip.SalaryMonth, slip.SalaryYear, slip.PayDate, slip.CreatedAt)).ToArray();

        return new PagedResult<StaffSalarySlipSummaryDto>(items, result.TotalCount, result.Page, result.PageSize);
    }

    public async Task<(StaffSalarySlipSummaryDto Slip, byte[] Pdf)> GetPdfForStaffAsync(
        Guid staffId,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await RequireStaffAsync(staffId, cancellationToken);

        var slip = await LoadAsync(id, cancellationToken);
        if (slip.StaffId != staffId)
        {
            throw new NotFoundException("Salary slip was not found.");
        }

        return (new StaffSalarySlipSummaryDto(
            slip.Id, slip.SalaryMonth, slip.SalaryYear, slip.PayDate, slip.CreatedAt), slip.Pdf);
    }

    private async Task RequireStaffAsync(Guid staffId, CancellationToken cancellationToken)
    {
        if (staffId == Guid.Empty)
        {
            throw new ValidationFailedException(new Dictionary<string, string[]>
            {
                ["staffId"] = ["Select a valid staff member."]
            });
        }

        if (await staff.GetByIdAsync(staffId, cancellationToken) is null)
        {
            throw new NotFoundException("Staff member was not found.");
        }
    }

    public async Task<SalarySlipPreview> PreviewAsync(
        SalarySlipRequest request,
        Guid createdBy,
        CancellationToken cancellationToken = default)
    {
        ValidateCreator(createdBy);
        Validate(request);

        var member = await staff.GetByIdAsync(request.StaffId, cancellationToken)
            ?? throw new NotFoundException("Staff member was not found.");

        if (!member.IsActive)
        {
            throw new ValidationFailedException(new Dictionary<string, string[]>
            {
                ["staffId"] = ["Select an active staff member."]
            });
        }

        var employeeName = $"{member.FirstName} {member.LastName}";
        var totalEarnings = request.BasicSalary + request.HouseRentAllowance + request.OtherAllowance + request.Commission;
        var totalDeductions = request.LeaveDeduction + request.OtherDeductions;
        var slip = new SalarySlip
        {
            Id = Guid.NewGuid(),
            StaffId = member.Id,
            EmployeeName = employeeName,
            EmployeeId = string.IsNullOrWhiteSpace(request.EmployeeId) ? null : request.EmployeeId.Trim(),
            SalaryMonth = request.SalaryMonth,
            SalaryYear = request.SalaryYear,
            PayDate = request.PayDate,
            WorkingDays = request.WorkingDays,
            TotalLeave = request.TotalLeave,
            LeaveWithoutPay = request.LeaveWithoutPay,
            LateMarks = request.LateMarks,
            AgreedTotalSalary = member.TotalSalary,
            BasicSalary = request.BasicSalary,
            HouseRentAllowance = request.HouseRentAllowance,
            OtherAllowance = request.OtherAllowance,
            Commission = request.Commission,
            LeaveDeduction = request.LeaveDeduction,
            OtherDeductions = request.OtherDeductions,
            TotalEarnings = totalEarnings,
            TotalDeductions = totalDeductions,
            NetSalary = totalEarnings - totalDeductions,
            BankName = member.BankName,
            AccountName = employeeName,
            BankAccountNumber = member.BankAccountNumber,
            IbanNumber = member.IbanNumber,
            CreatedAt = timeProvider.GetUtcNow(),
            CreatedBy = createdBy,
            TemplateVersion = "1"
        };

        slip.Pdf = pdf.Render(slip);
        var token = previewTokens.Protect(slip);

        return new SalarySlipPreview(Map(slip), Convert.ToBase64String(slip.Pdf), token);
    }

    public async Task<SalarySlipDto> SaveAsync(
        SaveSalarySlipRequest request,
        Guid createdBy,
        CancellationToken cancellationToken = default)
    {
        ValidateCreator(createdBy);

        if (string.IsNullOrWhiteSpace(request.PreviewToken))
        {
            throw InvalidPreview();
        }

        var slip = previewTokens.Unprotect(request.PreviewToken);

        if (slip.CreatedBy != createdBy)
        {
            throw InvalidPreview();
        }

        if (await salarySlips.ExistsAsync(slip.StaffId, slip.SalaryYear, slip.SalaryMonth, cancellationToken))
        {
            throw new ConflictException("A salary slip already exists for this staff member and salary period.");
        }

        await salarySlips.AddAndSaveAsync(slip, cancellationToken);

        return Map(slip);
    }

    private async Task<SalarySlip> LoadAsync(Guid id, CancellationToken cancellationToken)
    {
        return await salarySlips.GetAsync(id, cancellationToken)
            ?? throw new NotFoundException("Salary slip was not found.");
    }

    private static ValidationFailedException InvalidPreview() => new(new Dictionary<string, string[]>
    {
        ["previewToken"] = ["This preview is invalid or unavailable. Generate a new preview and try again."]
    });

    private static void ValidateCreator(Guid createdBy)
    {
        if (createdBy == Guid.Empty)
        {
            throw new ValidationFailedException(new Dictionary<string, string[]>
            {
                ["createdBy"] = ["A valid authenticated user is required."]
            });
        }
    }

    private static void Validate(SalarySlipRequest request)
    {
        var errors = new ValidationErrorBuilder();

        if (request.StaffId == Guid.Empty)
        {
            errors.Add("staffId", "Select a valid staff member.");
        }

        if (request.SalaryMonth is < 1 or > 12)
        {
            errors.Add("salaryMonth", "Salary month must be between 1 and 12.");
        }

        if (request.SalaryYear is < 2000 or > 2100)
        {
            errors.Add("salaryYear", "Salary year must be between 2000 and 2100.");
        }

        if (request.PayDate == default || request.PayDate.Year is < 2000 or > 2101)
        {
            errors.Add("payDate", "Pay date must be a valid date between 2000 and 2101.");
        }

        if (request.EmployeeId?.Trim().Length > 100)
        {
            errors.Add("employeeId", "Employee ID must be 100 characters or fewer.");
        }

        var days = request.SalaryYear is >= 2000 and <= 2100 && request.SalaryMonth is >= 1 and <= 12
            ? DateTime.DaysInMonth(request.SalaryYear, request.SalaryMonth)
            : 31;

        ValidateAttendance(request.WorkingDays, "workingDays", days, errors);
        ValidateAttendance(request.TotalLeave, "totalLeave", days, errors);
        ValidateAttendance(request.LeaveWithoutPay, "leaveWithoutPay", days, errors);

        if (request.LateMarks < 0 || request.LateMarks > days)
        {
            errors.Add("lateMarks", "Late marks must be between zero and the number of days in the salary period.");
        }

        if (request.LeaveWithoutPay > request.TotalLeave)
        {
            errors.Add("leaveWithoutPay", "Leave without pay cannot exceed total leave.");
        }

        ValidateMoney(request.BasicSalary, "basicSalary", errors);
        ValidateMoney(request.HouseRentAllowance, "houseRentAllowance", errors);
        ValidateMoney(request.OtherAllowance, "otherAllowance", errors);
        ValidateMoney(request.Commission, "commission", errors);
        ValidateMoney(request.LeaveDeduction, "leaveDeduction", errors);
        ValidateMoney(request.OtherDeductions, "otherDeductions", errors);

        errors.ThrowIfInvalid();

        var totalEarnings = request.BasicSalary + request.HouseRentAllowance + request.OtherAllowance + request.Commission;
        var totalDeductions = request.LeaveDeduction + request.OtherDeductions;

        if (totalDeductions > totalEarnings)
        {
            errors.Add("netSalary", "Net salary cannot be negative.");
        }

        errors.ThrowIfInvalid();
    }

    private static void ValidateAttendance(decimal value, string field, int days, ValidationErrorBuilder errors)
    {
        if (value < 0 || value > days)
        {
            errors.Add(field, "Days must be between zero and the number of days in the salary period.");
        }
        else if (decimal.Round(value, 1) != value)
        {
            errors.Add(field, "Days must have at most one decimal place.");
        }
    }

    private static void ValidateMoney(decimal value, string field, ValidationErrorBuilder errors)
    {
        if (value is < 0 or > 999999999.99m)
        {
            errors.Add(field, "Amount must be between 0 and 999999999.99.");
        }
        else if (decimal.Round(value, 2) != value)
        {
            errors.Add(field, "Amount must have at most two decimal places.");
        }
    }

    private static SalarySlipDto Map(SalarySlip slip) => new(
        slip.Id,
        slip.StaffId,
        slip.EmployeeName,
        slip.EmployeeId,
        slip.SalaryMonth,
        slip.SalaryYear,
        slip.PayDate,
        slip.WorkingDays,
        slip.TotalLeave,
        slip.LeaveWithoutPay,
        slip.LateMarks,
        slip.AgreedTotalSalary,
        slip.BasicSalary,
        slip.HouseRentAllowance,
        slip.OtherAllowance,
        slip.Commission,
        slip.LeaveDeduction,
        slip.OtherDeductions,
        slip.TotalEarnings,
        slip.TotalDeductions,
        slip.NetSalary,
        slip.BankName,
        slip.AccountName,
        slip.BankAccountNumber,
        slip.IbanNumber,
        slip.CreatedAt,
        slip.CreatedBy,
        slip.TemplateVersion);
}
