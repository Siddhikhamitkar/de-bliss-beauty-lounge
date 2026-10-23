using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeBliss.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLoyaltyRewards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "LoyaltyDiscount",
                table: "Invoices",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "LoyaltyPointsEarned",
                table: "Invoices",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "LoyaltyPointsRedeemed",
                table: "Invoices",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "Revision",
                table: "Invoices",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "CustomerLoyaltyTransactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Points = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    EligibleSpend = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    EarningPercentage = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    PointValueAed = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ReversesTransactionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerLoyaltyTransactions", x => x.Id);
                    table.CheckConstraint("CK_CustomerLoyaltyTransactions_EarningPercentage", "CAST([EarningPercentage] AS decimal(5,2)) BETWEEN 0 AND 100");
                    table.CheckConstraint("CK_CustomerLoyaltyTransactions_EligibleSpend", "CAST([EligibleSpend] AS decimal(18,2)) >= 0");
                    table.CheckConstraint("CK_CustomerLoyaltyTransactions_PointsAndType", "([Type] = 'Earn' AND CAST([Points] AS decimal(18,2)) > 0) OR ([Type] = 'Redeem' AND CAST([Points] AS decimal(18,2)) < 0) OR ([Type] = 'Reversal' AND CAST([Points] AS decimal(18,2)) <> 0)");
                    table.CheckConstraint("CK_CustomerLoyaltyTransactions_PointValueAed", "CAST([PointValueAed] AS decimal(18,4)) BETWEEN 0.0001 AND 1000");
                    table.ForeignKey(
                        name: "FK_CustomerLoyaltyTransactions_CustomerLoyaltyTransactions_ReversesTransactionId",
                        column: x => x.ReversesTransactionId,
                        principalTable: "CustomerLoyaltyTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerLoyaltyTransactions_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerLoyaltyTransactions_Invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "Invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerLoyaltyTransactions_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LoyaltySettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    EarningPercentage = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    PointValueAed = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    MinimumRedemptionPoints = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    MaximumRedemptionPercentage = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    AllowPartialRedemption = table.Column<bool>(type: "bit", nullable: false),
                    MinimumInvoiceAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoyaltySettings", x => x.Id);
                    table.CheckConstraint("CK_LoyaltySettings_EarningPercentage", "CAST([EarningPercentage] AS decimal(5,2)) BETWEEN 0 AND 100");
                    table.CheckConstraint("CK_LoyaltySettings_MaximumRedemptionPercentage", "CAST([MaximumRedemptionPercentage] AS decimal(5,2)) BETWEEN 0 AND 100");
                    table.CheckConstraint("CK_LoyaltySettings_MinimumInvoiceAmount", "[MinimumInvoiceAmount] IS NULL OR CAST([MinimumInvoiceAmount] AS decimal(18,2)) BETWEEN 0 AND 1000000000");
                    table.CheckConstraint("CK_LoyaltySettings_MinimumRedemptionPoints", "CAST([MinimumRedemptionPoints] AS decimal(18,2)) BETWEEN 0 AND 1000000000");
                    table.CheckConstraint("CK_LoyaltySettings_PointValueAed", "CAST([PointValueAed] AS decimal(18,4)) BETWEEN 0.0001 AND 1000");
                    table.ForeignKey(
                        name: "FK_LoyaltySettings_Users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerLoyaltyTransactions_CreatedByUserId",
                table: "CustomerLoyaltyTransactions",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerLoyaltyTransactions_CustomerId_OccurredAt",
                table: "CustomerLoyaltyTransactions",
                columns: new[] { "CustomerId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerLoyaltyTransactions_IdempotencyKey",
                table: "CustomerLoyaltyTransactions",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerLoyaltyTransactions_InvoiceId_Type",
                table: "CustomerLoyaltyTransactions",
                columns: new[] { "InvoiceId", "Type" },
                unique: true,
                filter: "[Type] IN ('Earn','Redeem')");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerLoyaltyTransactions_ReversesTransactionId",
                table: "CustomerLoyaltyTransactions",
                column: "ReversesTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_LoyaltySettings_UpdatedByUserId",
                table: "LoyaltySettings",
                column: "UpdatedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomerLoyaltyTransactions");

            migrationBuilder.DropTable(
                name: "LoyaltySettings");

            migrationBuilder.DropColumn(
                name: "LoyaltyDiscount",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "LoyaltyPointsEarned",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "LoyaltyPointsRedeemed",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "Revision",
                table: "Invoices");
        }
    }
}
