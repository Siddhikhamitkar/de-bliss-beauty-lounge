namespace DeBliss.Application.Staffing.Dtos;

public sealed record StaffSalaryDetails(
    decimal? TotalSalary,
    decimal? BasicSalary,
    decimal? OtherAllowances,
    string? IbanNumber,
    string? BankName,
    string? BankAccountNumber);

public sealed record SalarySlipRequest(
    Guid StaffId,
    int SalaryMonth,
    int SalaryYear,
    DateOnly PayDate,
    string? EmployeeId,
    decimal WorkingDays,
    decimal TotalLeave,
    decimal LeaveWithoutPay,
    int LateMarks,
    decimal BasicSalary,
    decimal HouseRentAllowance,
    decimal OtherAllowance,
    decimal Commission,
    decimal LeaveDeduction,
    decimal OtherDeductions);

public sealed record SalarySlipDto(
    Guid Id,
    Guid StaffId,
    string EmployeeName,
    string? EmployeeId,
    int SalaryMonth,
    int SalaryYear,
    DateOnly PayDate,
    decimal WorkingDays,
    decimal TotalLeave,
    decimal LeaveWithoutPay,
    int LateMarks,
    decimal? AgreedTotalSalary,
    decimal BasicSalary,
    decimal HouseRentAllowance,
    decimal OtherAllowance,
    decimal Commission,
    decimal LeaveDeduction,
    decimal OtherDeductions,
    decimal TotalEarnings,
    decimal TotalDeductions,
    decimal NetSalary,
    string? BankName,
    string AccountName,
    string? BankAccountNumber,
    string? IbanNumber,
    DateTimeOffset CreatedAt,
    Guid CreatedBy,
    string TemplateVersion);

public sealed record SalarySlipPreview(SalarySlipDto Slip, string PdfBase64, string PreviewToken);

public sealed record SaveSalarySlipRequest(string PreviewToken);

public sealed record SalarySlipSummaryDto(
    Guid Id,
    Guid StaffId,
    string EmployeeName,
    int SalaryMonth,
    int SalaryYear,
    DateOnly PayDate,
    decimal NetSalary,
    DateTimeOffset CreatedAt);

public sealed record StaffSalarySlipSummaryDto(
    Guid Id,
    int SalaryMonth,
    int SalaryYear,
    DateOnly PayDate,
    DateTimeOffset CreatedAt);
