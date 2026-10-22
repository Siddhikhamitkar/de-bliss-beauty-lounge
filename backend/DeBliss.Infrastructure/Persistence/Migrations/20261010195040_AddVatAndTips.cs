using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeBliss.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVatAndTips : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "PricesIncludeTax",
                table: "Invoices",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "TaxRate",
                table: "Invoices",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TipAmount",
                table: "Invoices",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TipPercentage",
                table: "Invoices",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TipStaffId",
                table: "Invoices",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SalonSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaxRate = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    PricesIncludeTax = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalonSettings", x => x.Id);
                });

            // The salon's standard configuration from day one: 5% VAT added at
            // the till. The Id is the fixed singleton identity SalonSettings.SingletonId.
            migrationBuilder.InsertData(
                table: "SalonSettings",
                columns: new[] { "Id", "TaxRate", "PricesIncludeTax", "CreatedAt", "UpdatedAt" },
                values: new object[]
                {
                    new Guid("6d2f8b41-9c57-4e3a-b8d4-1f0a7e5c9b26"),
                    5m,
                    false,
                    new DateTimeOffset(new DateTime(2026, 10, 10, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)),
                    null
                });

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_TipStaffId",
                table: "Invoices",
                column: "TipStaffId");

            migrationBuilder.AddForeignKey(
                name: "FK_Invoices_Staff_TipStaffId",
                table: "Invoices",
                column: "TipStaffId",
                principalTable: "Staff",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Invoices_Staff_TipStaffId",
                table: "Invoices");

            migrationBuilder.DropTable(
                name: "SalonSettings");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_TipStaffId",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "PricesIncludeTax",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "TaxRate",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "TipAmount",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "TipPercentage",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "TipStaffId",
                table: "Invoices");
        }
    }
}
