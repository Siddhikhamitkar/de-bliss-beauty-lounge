using DeBliss.Domain.Common;

namespace DeBliss.Domain.Staffing;

public sealed class SalarySlip : Entity
{
    public Guid StaffId { get; set; }

    public Staff Staff { get; set; } = null!;

    public string EmployeeName { get; set; } = null!;

    public string? EmployeeId { get; set; }

    public int SalaryMonth { get; set; }

    public int SalaryYear { get; set; }

    public DateOnly PayDate { get; set; }

    public decimal WorkingDays { get; set; }

    public decimal TotalLeave { get; set; }

    public decimal LeaveWithoutPay { get; set; }

    public int LateMarks { get; set; }

    public decimal? AgreedTotalSalary { get; set; }

    public decimal BasicSalary { get; set; }

    public decimal HouseRentAllowance { get; set; }

    public decimal OtherAllowance { get; set; }

    public decimal Commission { get; set; }

    public decimal LeaveDeduction { get; set; }

    public decimal OtherDeductions { get; set; }

    public decimal TotalEarnings { get; set; }

    public decimal TotalDeductions { get; set; }

    public decimal NetSalary { get; set; }

    public string? BankName { get; set; }

    public string AccountName { get; set; } = null!;

    public string? BankAccountNumber { get; set; }

    public string? IbanNumber { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid CreatedBy { get; set; }

    public byte[] Pdf { get; set; } = [];

    public string TemplateVersion { get; set; } = "1";
}
