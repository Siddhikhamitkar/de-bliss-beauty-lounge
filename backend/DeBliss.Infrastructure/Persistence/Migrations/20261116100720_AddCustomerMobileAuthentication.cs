using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeBliss.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerMobileAuthentication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_Email",
                table: "Users");

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "Users",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(256)",
                oldMaxLength: 256);

            migrationBuilder.AddColumn<string>(
                name: "SecurityStamp",
                table: "Users",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AccountStatus",
                table: "Customers",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "PendingActivation");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "MobileVerifiedAt",
                table: "Customers",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NormalizedPhoneNumber",
                table: "Customers",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CustomerVerificationCodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Purpose = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    PhoneNumber = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CodeHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    VerifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    TokenHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    TokenExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ConsumedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerVerificationCodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerVerificationCodes_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true,
                filter: "[Email] IS NOT NULL");

            // Backfill, before the unique index exists. Non-destructive: only the
            // new columns are written; PhoneNumber and every other column keep
            // their values.
            //
            // 1. NormalizedPhoneNumber mirrors MobileNumber.Normalize (E.164 with
            //    the UAE's local forms understood). A number that cannot be read
            //    stays NULL. Should two existing records share a number, only one
            //    gets it — the one with a login, else the oldest — and the others
            //    stay NULL until the front desk merges them; nothing is deleted.
            migrationBuilder.Sql(
                """
                WITH Cleaned AS (
                    SELECT c.Id, c.UserId, c.CreatedAt,
                           CASE WHEN LEFT(LTRIM(c.PhoneNumber), 1) = '+' THEN 1 ELSE 0 END AS Intl,
                           REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                               LTRIM(RTRIM(c.PhoneNumber)), '+', ''), ' ', ''), '-', ''), '(', ''), ')', ''), '.', ''), '/', '') AS Digits
                    FROM Customers c
                    WHERE c.PhoneNumber IS NOT NULL
                ),
                Prefixed AS (
                    SELECT Id, UserId, CreatedAt,
                           CASE WHEN Intl = 0 AND Digits LIKE '00%' THEN 1 ELSE Intl END AS Intl,
                           CASE WHEN Intl = 0 AND Digits LIKE '00%' THEN SUBSTRING(Digits, 3, 100) ELSE Digits END AS Digits
                    FROM Cleaned
                    WHERE Digits <> '' AND Digits NOT LIKE '%[^0-9]%'
                ),
                Local AS (
                    SELECT Id, UserId, CreatedAt,
                           CASE
                               WHEN Intl = 0 AND LEN(Digits) = 10 AND Digits LIKE '05%' THEN '971' + SUBSTRING(Digits, 2, 100)
                               WHEN Intl = 0 AND LEN(Digits) = 9 AND Digits LIKE '5%' THEN '971' + Digits
                               ELSE Digits
                           END AS Digits
                    FROM Prefixed
                ),
                Trunk AS (
                    SELECT Id, UserId, CreatedAt,
                           CASE WHEN Digits LIKE '9710%' THEN '971' + SUBSTRING(Digits, 5, 100) ELSE Digits END AS Digits
                    FROM Local
                ),
                Valid AS (
                    SELECT Id, UserId, CreatedAt, '+' + Digits AS Normalized
                    FROM Trunk
                    WHERE LEN(Digits) BETWEEN 8 AND 15
                      AND Digits NOT LIKE '0%'
                      AND (Digits NOT LIKE '971%' OR LEN(Digits) IN (11, 12))
                ),
                Ranked AS (
                    SELECT Id, Normalized,
                           ROW_NUMBER() OVER (
                               PARTITION BY Normalized
                               ORDER BY CASE WHEN UserId IS NULL THEN 1 ELSE 0 END, CreatedAt, Id) AS Position
                    FROM Valid
                )
                UPDATE c
                SET c.NormalizedPhoneNumber = r.Normalized
                FROM Customers c
                INNER JOIN Ranked r ON r.Id = c.Id
                WHERE r.Position = 1;
                """);

            // 2. Customers who already have a login keep signing in exactly as
            //    before, so their accounts are Active. Their mobile number is not
            //    yet verified: mobile sign-in opens once they prove it with a code
            //    (Forgot password, or a number check from their profile).
            migrationBuilder.Sql("UPDATE Customers SET AccountStatus = 'Active' WHERE UserId IS NOT NULL;");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_NormalizedPhoneNumber",
                table: "Customers",
                column: "NormalizedPhoneNumber",
                unique: true,
                filter: "[NormalizedPhoneNumber] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerVerificationCodes_CustomerId",
                table: "CustomerVerificationCodes",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerVerificationCodes_PhoneNumber_Purpose_CreatedAt",
                table: "CustomerVerificationCodes",
                columns: new[] { "PhoneNumber", "Purpose", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerVerificationCodes_TokenHash",
                table: "CustomerVerificationCodes",
                column: "TokenHash",
                unique: true,
                filter: "[TokenHash] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomerVerificationCodes");

            migrationBuilder.DropIndex(
                name: "IX_Users_Email",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Customers_NormalizedPhoneNumber",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "SecurityStamp",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "AccountStatus",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "MobileVerifiedAt",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "NormalizedPhoneNumber",
                table: "Customers");

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "Users",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(256)",
                oldMaxLength: 256,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);
        }
    }
}
