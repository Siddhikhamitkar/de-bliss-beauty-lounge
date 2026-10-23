using System.Text.Json;
using DeBliss.Application.Common.Exceptions;
using DeBliss.Application.Common.Interfaces;
using DeBliss.Application.Staffing;
using DeBliss.Application.Staffing.Dtos;
using DeBliss.Application.Staffing.Ports;
using DeBliss.Domain.Staffing;
using DeBliss.UnitTests.Imports;
using StaffApplicationService = DeBliss.Application.Staffing.StaffService;

namespace DeBliss.UnitTests.Staffing;

public sealed class SalaryStaffTests
{
    [Fact]
    public async Task Create_and_reload_preserves_independent_salary_and_formatted_bank_text()
    {
        var staff = new SalaryTestStaffRepository();
        var work = new SalaryTestUnitOfWork();
        var service = new StaffApplicationService(staff, new ServiceListRepository(), work);
        var created = await service.CreateAsync(Request(Finance()));
        var member = await staff.GetByIdAsync(created.Id);
        Assert.NotNull(member);
        Assert.Equal(3100m, member.TotalSalary);
        Assert.Equal(2000m, member.BasicSalary);
        Assert.Equal(1000m, member.OtherAllowances);
        Assert.Equal("0012 0345", member.BankAccountNumber);
        Assert.Equal("AE00 0012 0345", member.IbanNumber);
        Assert.Equal("Test Bank", member.BankName);
        Assert.Equal(1, work.Saves);
        AssertPrivate(await service.GetAsync(created.Id));
        AssertPrivate(created);
        AssertPrivate(await service.SearchAsync(new StaffQuery(null, StaffStatusFilter.All, 1, 50)));
    }

    [Fact]
    public async Task Update_can_edit_and_clear_finance_but_ordinary_updates_preserve_it()
    {
        var staff = new SalaryTestStaffRepository();
        var service = new StaffApplicationService(staff, new ServiceListRepository(), new SalaryTestUnitOfWork());
        var created = await service.CreateAsync(Request(Finance()));
        await service.UpdateAsync(created.Id, Request(null) with { FirstName = "Changed" });
        var member = (await staff.GetByIdAsync(created.Id))!;
        Assert.Equal(3100m, member.TotalSalary);
        Assert.Equal("0012 0345", member.BankAccountNumber);
        await service.UpdateAsync(created.Id, Request(Finance() with { TotalSalary = 0, BasicSalary = 0, OtherAllowances = 0, BankAccountNumber = "0000" }));
        Assert.Equal(0m, member.TotalSalary);
        Assert.Equal("0000", member.BankAccountNumber);
        await service.UpdateAsync(created.Id, Request(new StaffSalaryDetails(null, null, null, null, null, null)));
        Assert.Null(member.TotalSalary);
        Assert.Null(member.BasicSalary);
        Assert.Null(member.OtherAllowances);
        Assert.Null(member.IbanNumber);
        Assert.Null(member.BankName);
        Assert.Null(member.BankAccountNumber);
    }

    [Theory]
    [InlineData("totalSalary", "-0.01")]
    [InlineData("basicSalary", "-1")]
    [InlineData("otherAllowances", "-1")]
    [InlineData("totalSalary", "0.001")]
    [InlineData("basicSalary", "1000000000")]
    [InlineData("otherAllowances", "79228162514264337593543950335")]
    public async Task Invalid_salary_is_rejected_before_any_write(string field, string raw)
    {
        var staff = new SalaryTestStaffRepository();
        var work = new SalaryTestUnitOfWork();
        var service = new StaffApplicationService(staff, new ServiceListRepository(), work);
        var value = decimal.Parse(raw, System.Globalization.CultureInfo.InvariantCulture);
        var finance = field switch
        {
            "totalSalary" => Finance() with { TotalSalary = value },
            "basicSalary" => Finance() with { BasicSalary = value },
            _ => Finance() with { OtherAllowances = value }
        };
        var error = await Assert.ThrowsAsync<ValidationFailedException>(() => service.CreateAsync(Request(finance)));
        Assert.Contains("salary." + field, error.Errors.Keys);
        Assert.Empty(staff.Items);
        Assert.Equal(0, work.Saves);
    }

    [Theory]
    [InlineData("ibanNumber", 51)]
    [InlineData("bankName", 201)]
    [InlineData("bankAccountNumber", 101)]
    public async Task Oversized_bank_fields_are_rejected(string field, int length)
    {
        var staff = new SalaryTestStaffRepository();
        var service = new StaffApplicationService(staff, new ServiceListRepository(), new SalaryTestUnitOfWork());
        var value = new string('x', length);
        var finance = field switch
        {
            "ibanNumber" => Finance() with { IbanNumber = value },
            "bankName" => Finance() with { BankName = value },
            _ => Finance() with { BankAccountNumber = value }
        };
        var error = await Assert.ThrowsAsync<ValidationFailedException>(() => service.CreateAsync(Request(finance)));
        Assert.Contains("salary." + field, error.Errors.Keys);
        Assert.Empty(staff.Items);
    }

    [Fact]
    public async Task Failed_financial_update_does_not_mutate_profile_or_saved_finance()
    {
        var staff = new SalaryTestStaffRepository();
        var work = new SalaryTestUnitOfWork();
        var service = new StaffApplicationService(staff, new ServiceListRepository(), work);
        var created = await service.CreateAsync(Request(Finance()));
        await Assert.ThrowsAsync<ValidationFailedException>(() => service.UpdateAsync(created.Id,
            Request(Finance() with { BasicSalary = -1 }) with { FirstName = "Must not change" }));
        var member = (await staff.GetByIdAsync(created.Id))!;
        Assert.Equal("Test", member.FirstName);
        Assert.Equal(2000m, member.BasicSalary);
        Assert.Equal(1, work.Saves);
    }

    [Fact]
    public void Ordinary_staff_wire_types_have_no_financial_properties()
    {
        foreach (var type in new[] { typeof(StaffDto), typeof(StaffSummaryDto), typeof(StaffMobileProfileDto) })
        {
            var fields = type.GetProperties().Select(property => property.Name).ToArray();
            Assert.DoesNotContain(fields, field => field.Contains("Salary") || field.Contains("Bank") || field.Contains("Iban") || field.Contains("Allowance"));
        }
    }

    internal static SaveStaffRequest Request(StaffSalaryDetails? finance) => new("Test", "Employee", null, null, "Stylist", null, true, 0, finance);
    internal static StaffSalaryDetails Finance() => new(3100m, 2000m, 1000m, " AE00 0012 0345 ", " Test Bank ", " 0012 0345 ");
    private static void AssertPrivate(object value)
    {
        var json = JsonSerializer.Serialize(value);
        Assert.DoesNotContain("Salary", json);
        Assert.DoesNotContain("Bank", json);
        Assert.DoesNotContain("Iban", json);
        Assert.DoesNotContain("0012", json);
    }
}

internal sealed class SalaryTestStaffRepository : IStaffRepository
{
    public List<Staff> Items { get; } = [];
    public Task<Staff?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult(Items.SingleOrDefault(member => member.Id == id));
    public Task<Staff?> GetByIdForScheduleAsync(Guid id, CancellationToken ct = default) => GetByIdAsync(id, ct);
    public Task<(IReadOnlyList<Staff> Items, int TotalCount)> SearchAsync(StaffQuery query, CancellationToken ct = default) => Task.FromResult(((IReadOnlyList<Staff>)Items, Items.Count));
    public Task<IReadOnlyList<Staff>> GetActiveAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Staff>>(Items.Where(member => member.IsActive).ToArray());
    public Task<IReadOnlyList<Staff>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Staff>>(Items.Where(member => ids.Contains(member.Id)).ToArray());
    public Task<IReadOnlyList<Staff>> GetActiveAssignedToServiceAsync(Guid serviceId, CancellationToken ct = default) => throw new NotSupportedException();
    public void Add(Staff staff)
    {
        staff.Id = Guid.NewGuid();
        Items.Add(staff);
    }
}

internal sealed class SalaryTestUnitOfWork : IUnitOfWork
{
    public int Saves { get; private set; }
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(++Saves);
    public Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
}
