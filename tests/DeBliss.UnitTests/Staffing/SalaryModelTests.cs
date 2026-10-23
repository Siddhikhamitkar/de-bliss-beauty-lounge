using DeBliss.Domain.Staffing;
using DeBliss.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace DeBliss.UnitTests.Staffing;

public sealed class SalaryModelTests
{
    [Fact]
    public void Financial_columns_are_nullable_decimal_or_text_and_snapshots_are_restrictive()
    {
        using var context = ModelOnlyContext();
        var staff = context.Model.FindEntityType(typeof(Staff))!;
        foreach (var name in new[] { "TotalSalary", "BasicSalary", "OtherAllowances" })
        {
            var property = staff.FindProperty(name)!;
            Assert.True(property.IsNullable);
            Assert.Equal(18, property.GetPrecision());
            Assert.Equal(2, property.GetScale());
            Assert.Equal(typeof(decimal?), property.ClrType);
        }
        foreach (var name in new[] { "IbanNumber", "BankName", "BankAccountNumber" })
        {
            var property = staff.FindProperty(name)!;
            Assert.True(property.IsNullable);
            Assert.Equal(typeof(string), property.ClrType);
        }
        var slip = context.Model.FindEntityType(typeof(SalarySlip))!;
        Assert.Contains(slip.GetIndexes(), index => index.IsUnique && index.Properties.Select(property => property.Name)
            .SequenceEqual(new[] { "StaffId", "SalaryYear", "SalaryMonth" }));
        Assert.Contains(slip.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(Staff)
            && key.DeleteBehavior == DeleteBehavior.Restrict);
        Assert.Equal("varbinary(max)", slip.FindProperty("Pdf")!.GetColumnType());
        foreach (var name in new[] { "BasicSalary", "HouseRentAllowance", "OtherAllowance", "Commission", "LeaveDeduction", "OtherDeductions", "TotalEarnings", "TotalDeductions", "NetSalary" })
        {
            Assert.Equal(18, slip.FindProperty(name)!.GetPrecision());
            Assert.Equal(2, slip.FindProperty(name)!.GetScale());
        }
    }

    [Fact]
    public void Salary_migration_only_adds_six_nullable_columns_and_snapshot_table()
    {
        var type = typeof(DeBlissDbContext).Assembly.GetTypes().Single(type => type.Name == "AddStaffSalaryAndSalarySlips");
        var migration = (Migration)Activator.CreateInstance(type)!;
        var operations = migration.UpOperations;
        Assert.All(operations, operation => Assert.True(operation is AddColumnOperation or CreateTableOperation or CreateIndexOperation));
        var columns = operations.OfType<AddColumnOperation>().ToArray();
        Assert.Equal(6, columns.Length);
        Assert.All(columns, column =>
        {
            Assert.Equal("Staff", column.Table);
            Assert.True(column.IsNullable);
            Assert.Null(column.DefaultValue);
            Assert.Null(column.DefaultValueSql);
        });
        var table = Assert.Single(operations.OfType<CreateTableOperation>());
        Assert.Equal("SalarySlips", table.Name);
        Assert.Contains(table.Columns, column => column.Name == "Pdf");
    }

    private static DeBlissDbContext ModelOnlyContext() => new(new DbContextOptionsBuilder<DeBlissDbContext>()
        .UseSqlServer("Server=(local);Database=DeBlissDb-Demo;Integrated Security=true;TrustServerCertificate=true")
        .Options);
}
