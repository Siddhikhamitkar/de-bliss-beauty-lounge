using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeBliss.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStaffSalaryAndSalarySlips : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BankAccountNumber",
                table: "Staff",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BankName",
                table: "Staff",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "BasicSalary",
                table: "Staff",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IbanNumber",
                table: "Staff",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OtherAllowances",
                table: "Staff",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalSalary",
                table: "Staff",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SalarySlips",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StaffId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeName = table.Column<string>(type: "nvarchar(201)", maxLength: 201, nullable: false),
                    EmployeeId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SalaryMonth = table.Column<int>(type: "int", nullable: false),
                    SalaryYear = table.Column<int>(type: "int", nullable: false),
                    PayDate = table.Column<DateOnly>(type: "date", nullable: false),
                    WorkingDays = table.Column<decimal>(type: "decimal(4,1)", precision: 4, scale: 1, nullable: false),
                    TotalLeave = table.Column<decimal>(type: "decimal(4,1)", precision: 4, scale: 1, nullable: false),
                    LeaveWithoutPay = table.Column<decimal>(type: "decimal(4,1)", precision: 4, scale: 1, nullable: false),
                    LateMarks = table.Column<int>(type: "int", nullable: false),
                    AgreedTotalSalary = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    BasicSalary = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    HouseRentAllowance = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    OtherAllowance = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Commission = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    LeaveDeduction = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    OtherDeductions = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalEarnings = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalDeductions = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    NetSalary = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    BankName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    AccountName = table.Column<string>(type: "nvarchar(201)", maxLength: 201, nullable: false),
                    BankAccountNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IbanNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Pdf = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    TemplateVersion = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "1")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalarySlips", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalarySlips_Staff_StaffId",
                        column: x => x.StaffId,
                        principalTable: "Staff",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SalarySlips_StaffId_SalaryYear_SalaryMonth",
                table: "SalarySlips",
                columns: new[] { "StaffId", "SalaryYear", "SalaryMonth" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SalarySlips");

            migrationBuilder.DropColumn(
                name: "BankAccountNumber",
                table: "Staff");

            migrationBuilder.DropColumn(
                name: "BankName",
                table: "Staff");

            migrationBuilder.DropColumn(
                name: "BasicSalary",
                table: "Staff");

            migrationBuilder.DropColumn(
                name: "IbanNumber",
                table: "Staff");

            migrationBuilder.DropColumn(
                name: "OtherAllowances",
                table: "Staff");

            migrationBuilder.DropColumn(
                name: "TotalSalary",
                table: "Staff");
        }
    }
}
