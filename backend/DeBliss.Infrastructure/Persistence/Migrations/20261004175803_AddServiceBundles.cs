using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeBliss.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceBundles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "BundleDiscount",
                table: "Invoices",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "BundleNameSnapshot",
                table: "Invoices",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ServiceBundleId",
                table: "Invoices",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "BundlePriceSnapshot",
                table: "Appointments",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ServiceBundleId",
                table: "Appointments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ServiceBundles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ServiceCategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Price = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceBundles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ServiceBundles_ServiceCategories_ServiceCategoryId",
                        column: x => x.ServiceCategoryId,
                        principalTable: "ServiceCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ServiceBundleItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ServiceBundleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ServiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceBundleItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ServiceBundleItems_ServiceBundles_ServiceBundleId",
                        column: x => x.ServiceBundleId,
                        principalTable: "ServiceBundles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ServiceBundleItems_Services_ServiceId",
                        column: x => x.ServiceId,
                        principalTable: "Services",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_ServiceBundleId",
                table: "Invoices",
                column: "ServiceBundleId");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_ServiceBundleId",
                table: "Appointments",
                column: "ServiceBundleId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceBundleItems_ServiceBundleId_ServiceId",
                table: "ServiceBundleItems",
                columns: new[] { "ServiceBundleId", "ServiceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServiceBundleItems_ServiceId",
                table: "ServiceBundleItems",
                column: "ServiceId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceBundles_IsActive_ServiceCategoryId",
                table: "ServiceBundles",
                columns: new[] { "IsActive", "ServiceCategoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceBundles_ServiceCategoryId",
                table: "ServiceBundles",
                column: "ServiceCategoryId");

            migrationBuilder.AddForeignKey(
                name: "FK_Appointments_ServiceBundles_ServiceBundleId",
                table: "Appointments",
                column: "ServiceBundleId",
                principalTable: "ServiceBundles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Invoices_ServiceBundles_ServiceBundleId",
                table: "Invoices",
                column: "ServiceBundleId",
                principalTable: "ServiceBundles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Appointments_ServiceBundles_ServiceBundleId",
                table: "Appointments");

            migrationBuilder.DropForeignKey(
                name: "FK_Invoices_ServiceBundles_ServiceBundleId",
                table: "Invoices");

            migrationBuilder.DropTable(
                name: "ServiceBundleItems");

            migrationBuilder.DropTable(
                name: "ServiceBundles");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_ServiceBundleId",
                table: "Invoices");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_ServiceBundleId",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "BundleDiscount",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "BundleNameSnapshot",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "ServiceBundleId",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "BundlePriceSnapshot",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "ServiceBundleId",
                table: "Appointments");
        }
    }
}
