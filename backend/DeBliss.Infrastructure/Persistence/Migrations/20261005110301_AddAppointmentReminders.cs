using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace DeBliss.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAppointmentReminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "WhatsAppOptInDate",
                table: "Customers",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WhatsAppOptInSource",
                table: "Customers",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WhatsAppOptInStatus",
                table: "Customers",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Unknown");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "WhatsAppOptOutDate",
                table: "Customers",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AppointmentReminders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppointmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReminderType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Channel = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ScheduledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    LastAttemptAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ProviderMessageId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    FailureReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ClaimedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ClaimedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppointmentReminders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppointmentReminders_Appointments_AppointmentId",
                        column: x => x.AppointmentId,
                        principalTable: "Appointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AppointmentReminders_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReminderRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReminderType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    OffsetMinutes = table.Column<int>(type: "int", nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReminderRules", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "ReminderRules",
                columns: new[] { "Id", "CreatedAt", "IsEnabled", "OffsetMinutes", "ReminderType", "UpdatedAt" },
                values: new object[,]
                {
                    { new Guid("5eed0000-0000-4000-8000-000000000001"), new DateTimeOffset(new DateTime(2026, 10, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, 0, "Confirmation", null },
                    { new Guid("5eed0000-0000-4000-8000-000000000002"), new DateTimeOffset(new DateTime(2026, 10, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, 1440, "Reminder", null },
                    { new Guid("5eed0000-0000-4000-8000-000000000003"), new DateTimeOffset(new DateTime(2026, 10, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, 120, "Reminder", null },
                    { new Guid("5eed0000-0000-4000-8000-000000000004"), new DateTimeOffset(new DateTime(2026, 10, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, 0, "Cancellation", null },
                    { new Guid("5eed0000-0000-4000-8000-000000000005"), new DateTimeOffset(new DateTime(2026, 10, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, 0, "Rescheduled", null }
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentReminders_AppointmentId_ReminderType_ScheduledAt",
                table: "AppointmentReminders",
                columns: new[] { "AppointmentId", "ReminderType", "ScheduledAt" },
                unique: true,
                filter: "[Status] = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentReminders_CustomerId",
                table: "AppointmentReminders",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentReminders_ProviderMessageId",
                table: "AppointmentReminders",
                column: "ProviderMessageId",
                unique: true,
                filter: "[ProviderMessageId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentReminders_Status_ScheduledAt",
                table: "AppointmentReminders",
                columns: new[] { "Status", "ScheduledAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ReminderRules_ReminderType_OffsetMinutes",
                table: "ReminderRules",
                columns: new[] { "ReminderType", "OffsetMinutes" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppointmentReminders");

            migrationBuilder.DropTable(
                name: "ReminderRules");

            migrationBuilder.DropColumn(
                name: "WhatsAppOptInDate",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "WhatsAppOptInSource",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "WhatsAppOptInStatus",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "WhatsAppOptOutDate",
                table: "Customers");
        }
    }
}
