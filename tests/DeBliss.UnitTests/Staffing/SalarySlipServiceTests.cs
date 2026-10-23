using System.Text.Json;
using DeBliss.Api.Security;
using DeBliss.Application.Common.Exceptions;
using DeBliss.Application.Staffing;
using DeBliss.Application.Staffing.Dtos;
using DeBliss.Application.Staffing.Ports;
using DeBliss.Domain.Staffing;
using Microsoft.AspNetCore.DataProtection;

namespace DeBliss.UnitTests.Staffing;

public sealed class SalarySlipServiceTests
{
    [Fact]
    public async Task Preview_calculates_decimal_totals_and_keeps_agreed_salary_separate_without_saving()
    {
        var fixture = new SalarySlipTestFixture();
        var preview = await fixture.Service.PreviewAsync(fixture.Request(), fixture.Owner);
        Assert.Equal(3101m, preview.Slip.TotalEarnings);
        Assert.Equal(100.30m, preview.Slip.TotalDeductions);
        Assert.Equal(3000.70m, preview.Slip.NetSalary);
        Assert.Equal(3100m, preview.Slip.AgreedTotalSalary);
        Assert.Equal("Test Employee", preview.Slip.EmployeeName);
        Assert.Equal("Test Employee", preview.Slip.AccountName);
        Assert.Equal("000123 456", preview.Slip.BankAccountNumber);
        Assert.Equal(fixture.Clock.Now, preview.Slip.CreatedAt);
        Assert.Equal(fixture.Owner, preview.Slip.CreatedBy);
        Assert.Empty(fixture.Slips.Items);
        Assert.Equal(0, fixture.Slips.SaveCalls);
    }

    [Fact]
    public async Task Zero_commission_and_deductions_allow_zero_or_positive_net()
    {
        var fixture = new SalarySlipTestFixture();
        var request = fixture.Request() with { BasicSalary = 0, HouseRentAllowance = 0, OtherAllowance = 0, Commission = 0, LeaveDeduction = 0, OtherDeductions = 0 };
        var preview = await fixture.Service.PreviewAsync(request, fixture.Owner);
        Assert.Equal(0, preview.Slip.TotalEarnings);
        Assert.Equal(0, preview.Slip.TotalDeductions);
        Assert.Equal(0, preview.Slip.NetSalary);
        preview = await fixture.Service.PreviewAsync(request with { BasicSalary = 123.45m }, fixture.Owner);
        Assert.Equal(123.45m, preview.Slip.NetSalary);
    }

    [Fact]
    public async Task Save_copies_reviewed_snapshot_and_download_never_uses_current_staff()
    {
        var fixture = new SalarySlipTestFixture();
        var preview = await fixture.Service.PreviewAsync(fixture.Request(), fixture.Owner);
        fixture.Member.FirstName = "Changed";
        fixture.Member.TotalSalary = 999;
        fixture.Member.BankName = "Changed bank";
        fixture.Member.BankAccountNumber = "changed";
        fixture.Member.IbanNumber = "changed";
        var saved = await fixture.Service.SaveAsync(new(preview.PreviewToken), fixture.Owner);
        Assert.Equal(preview.Slip, saved);
        var detail = await fixture.Service.GetAsync(saved.Id);
        Assert.Equal(preview.Slip, detail);
        var (downloaded, bytes) = await fixture.Service.GetPdfAsync(saved.Id);
        Assert.Equal(saved, downloaded);
        Assert.Equal(Convert.FromBase64String(preview.PdfBase64), bytes);
        Assert.Equal(1, fixture.Slips.SaveCalls);
        Assert.Single(fixture.Slips.Items);
        Assert.NotSame(fixture.Member, fixture.Slips.Items[0].Staff);
    }

    [Fact]
    public async Task Same_employee_period_cannot_be_saved_twice_even_with_new_preview()
    {
        var fixture = new SalarySlipTestFixture();
        var first = await fixture.Service.PreviewAsync(fixture.Request(), fixture.Owner);
        var second = await fixture.Service.PreviewAsync(fixture.Request() with { Commission = 99 }, fixture.Owner);
        await fixture.Service.SaveAsync(new(first.PreviewToken), fixture.Owner);
        await Assert.ThrowsAsync<ConflictException>(() => fixture.Service.SaveAsync(new(second.PreviewToken), fixture.Owner));
        Assert.Single(fixture.Slips.Items);
        Assert.Equal(1, fixture.Slips.SaveCalls);
    }

    [Fact]
    public async Task Concurrent_repository_conflict_is_not_treated_as_a_successful_save()
    {
        var fixture = new SalarySlipTestFixture();
        var preview = await fixture.Service.PreviewAsync(fixture.Request(), fixture.Owner);
        fixture.Slips.ConflictOnSave = true;
        await Assert.ThrowsAsync<ConflictException>(() => fixture.Service.SaveAsync(new(preview.PreviewToken), fixture.Owner));
        Assert.Empty(fixture.Slips.Items);
    }

    [Fact]
    public async Task Different_period_or_employee_remains_independent_and_history_is_paged()
    {
        var fixture = new SalarySlipTestFixture();
        foreach (var month in new[] { 9, 10 })
        {
            var preview = await fixture.Service.PreviewAsync(fixture.Request() with { SalaryMonth = month }, fixture.Owner);
            await fixture.Service.SaveAsync(new(preview.PreviewToken), fixture.Owner);
        }
        var result = await fixture.Service.GetPagedAsync(fixture.Member.Id, 2026, 1, 1);
        Assert.Equal(2, result.TotalCount);
        Assert.Single(result.Items);
        Assert.DoesNotContain("Bank", JsonSerializer.Serialize(result));
        var normalized = await fixture.Service.GetPagedAsync(null, null, 0, 10000);
        Assert.Equal(1, normalized.Page);
        Assert.Equal(100, normalized.PageSize);
        var missing = await fixture.Service.GetPagedAsync(Guid.NewGuid(), null, 1, 20);
        Assert.Empty(missing.Items);
    }

    [Theory]
    [InlineData("basicSalary", "-1")]
    [InlineData("houseRentAllowance", "-1")]
    [InlineData("otherAllowance", "-1")]
    [InlineData("commission", "-0.01")]
    [InlineData("leaveDeduction", "-1")]
    [InlineData("otherDeductions", "-1")]
    [InlineData("basicSalary", "1.001")]
    [InlineData("commission", "1000000000")]
    [InlineData("commission", "79228162514264337593543950335")]
    public async Task Invalid_amounts_are_rejected_before_calculation_or_persistence(string field, string raw)
    {
        var fixture = new SalarySlipTestFixture();
        var value = decimal.Parse(raw, System.Globalization.CultureInfo.InvariantCulture);
        var request = field switch
        {
            "basicSalary" => fixture.Request() with { BasicSalary = value },
            "houseRentAllowance" => fixture.Request() with { HouseRentAllowance = value },
            "otherAllowance" => fixture.Request() with { OtherAllowance = value },
            "commission" => fixture.Request() with { Commission = value },
            "leaveDeduction" => fixture.Request() with { LeaveDeduction = value },
            _ => fixture.Request() with { OtherDeductions = value }
        };
        var error = await Assert.ThrowsAsync<ValidationFailedException>(() => fixture.Service.PreviewAsync(request, fixture.Owner));
        Assert.Contains(field, error.Errors.Keys);
        Assert.Empty(fixture.Slips.Items);
    }

    [Fact]
    public async Task Negative_net_is_rejected_without_imposing_attendance_deduction_policy()
    {
        var fixture = new SalarySlipTestFixture();
        var error = await Assert.ThrowsAsync<ValidationFailedException>(() => fixture.Service.PreviewAsync(fixture.Request() with { OtherDeductions = 9000 }, fixture.Owner));
        Assert.Contains("netSalary", error.Errors.Keys);
        var preview = await fixture.Service.PreviewAsync(fixture.Request() with { TotalLeave = 20, LeaveWithoutPay = 20, LeaveDeduction = 0 }, fixture.Owner);
        Assert.Equal(0, preview.Slip.LeaveDeduction);
    }

    [Theory]
    [InlineData(0, 2026)]
    [InlineData(13, 2026)]
    [InlineData(1, 1999)]
    [InlineData(1, 2101)]
    public async Task Invalid_periods_are_rejected(int month, int year)
    {
        var fixture = new SalarySlipTestFixture();
        await Assert.ThrowsAsync<ValidationFailedException>(() => fixture.Service.PreviewAsync(fixture.Request() with { SalaryMonth = month, SalaryYear = year }, fixture.Owner));
        Assert.Empty(fixture.Slips.Items);
    }

    [Theory]
    [InlineData("workingDays")]
    [InlineData("totalLeave")]
    [InlineData("leaveWithoutPay")]
    [InlineData("lateMarks")]
    [InlineData("payDate")]
    [InlineData("employeeId")]
    public async Task Attendance_date_and_employee_id_limits_are_enforced(string field)
    {
        var fixture = new SalarySlipTestFixture();
        var request = field switch
        {
            "workingDays" => fixture.Request() with { WorkingDays = 31, SalaryMonth = 2 },
            "totalLeave" => fixture.Request() with { TotalLeave = 1.25m },
            "leaveWithoutPay" => fixture.Request() with { LeaveWithoutPay = 3, TotalLeave = 2 },
            "lateMarks" => fixture.Request() with { LateMarks = -1 },
            "payDate" => fixture.Request() with { PayDate = default },
            _ => fixture.Request() with { EmployeeId = new string('x', 101) }
        };
        var error = await Assert.ThrowsAsync<ValidationFailedException>(() => fixture.Service.PreviewAsync(request, fixture.Owner));
        Assert.Contains(field, error.Errors.Keys);
    }

    [Fact]
    public async Task Inactive_missing_staff_and_invalid_creator_cannot_generate()
    {
        var fixture = new SalarySlipTestFixture();
        await Assert.ThrowsAsync<NotFoundException>(() => fixture.Service.PreviewAsync(fixture.Request() with { StaffId = Guid.NewGuid() }, fixture.Owner));
        fixture.Member.IsActive = false;
        await Assert.ThrowsAsync<ValidationFailedException>(() => fixture.Service.PreviewAsync(fixture.Request(), fixture.Owner));
        await Assert.ThrowsAsync<ValidationFailedException>(() => fixture.Service.PreviewAsync(fixture.Request(), Guid.Empty));
        Assert.Empty(fixture.Slips.Items);
    }
}

internal sealed class SalarySlipTestFixture
{
    public SalaryTestStaffRepository Staff { get; } = new();
    public SalaryTestSlipRepository Slips { get; } = new();
    public SalaryTestClock Clock { get; } = new();
    public Guid Owner { get; } = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public Staff Member { get; } = new()
    {
        Id = Guid.NewGuid(), FirstName = "Test", LastName = "Employee", IsActive = true,
        TotalSalary = 3100, BasicSalary = 2000.10m, OtherAllowances = 400.30m,
        BankName = "Test Bank", BankAccountNumber = "000123 456", IbanNumber = "AE00 00123 456"
    };
    public SalarySlipService Service { get; }

    public SalarySlipTestFixture(bool realPdf = false)
    {
        Staff.Items.Add(Member);
        Service = new(Staff, Slips, realPdf ? new DeBliss.Infrastructure.Staffing.SalarySlipPdf() : new FixedSalaryPdf(),
            new SalarySlipPreviewTokens(new EphemeralDataProtectionProvider(), Clock), Clock);
    }

    public SalarySlipRequest Request() => new(Member.Id, 10, 2026, new DateOnly(2026, 11, 1), "EMP-001", 26, 2, 1, 2,
        2000.10m, 500.20m, 400.30m, 200.40m, 100.10m, 0.20m);

    private sealed class FixedSalaryPdf : ISalarySlipPdf
    {
        public byte[] Render(SalarySlip slip) => "%PDF-1.7\nUnit-test document bytes"u8.ToArray();
    }
}

internal sealed class SalaryTestSlipRepository : ISalarySlipRepository
{
    public List<SalarySlip> Items { get; } = [];
    public int SaveCalls { get; private set; }
    public bool ConflictOnSave { get; set; }
    public Task<SalarySlip?> GetAsync(Guid id, CancellationToken ct = default) => Task.FromResult(Items.SingleOrDefault(slip => slip.Id == id));
    public Task<bool> ExistsAsync(Guid staffId, int salaryYear, int salaryMonth, CancellationToken ct = default) => Task.FromResult(Items.Any(slip => slip.StaffId == staffId && slip.SalaryYear == salaryYear && slip.SalaryMonth == salaryMonth));
    public Task<(IReadOnlyList<SalarySlipSummaryDto> Items, int TotalCount)> GetPagedAsync(Guid? staffId, int? year, int page, int pageSize, CancellationToken ct = default)
    {
        var found = Items.Where(slip => (!staffId.HasValue || slip.StaffId == staffId) && (!year.HasValue || slip.SalaryYear == year))
            .OrderByDescending(slip => slip.SalaryYear).ThenByDescending(slip => slip.SalaryMonth).ToArray();
        IReadOnlyList<SalarySlipSummaryDto> rows = found.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(slip => new SalarySlipSummaryDto(slip.Id, slip.StaffId, slip.EmployeeName, slip.SalaryMonth, slip.SalaryYear, slip.PayDate, slip.NetSalary, slip.CreatedAt)).ToArray();
        return Task.FromResult((rows, found.Length));
    }
    public Task AddAndSaveAsync(SalarySlip slip, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (ConflictOnSave || Items.Any(item => item.StaffId == slip.StaffId && item.SalaryYear == slip.SalaryYear && item.SalaryMonth == slip.SalaryMonth))
            throw new ConflictException("A salary slip already exists for this staff member and salary period.");
        SaveCalls++;
        Items.Add(slip);
        return Task.CompletedTask;
    }
}
