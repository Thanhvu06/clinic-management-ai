using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAiBookingConfirmation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiBookingConfirmations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ConfirmationId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SessionId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    DraftId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    DraftVersion = table.Column<int>(type: "int", nullable: false),
                    ContextSnapshotId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    SpecialtyId = table.Column<long>(type: "bigint", nullable: false),
                    DoctorId = table.Column<long>(type: "bigint", nullable: false),
                    SlotId = table.Column<long>(type: "bigint", nullable: false),
                    SlotDate = table.Column<DateOnly>(type: "date", nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time", nullable: false),
                    EndTime = table.Column<TimeOnly>(type: "time", nullable: false),
                    ReasonHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UsedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UsedAppointmentId = table.Column<long>(type: "bigint", nullable: true),
                    UsedIdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    RevokedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiBookingConfirmations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiBookingConfirmations_ConfirmationId",
                table: "AiBookingConfirmations",
                column: "ConfirmationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiBookingConfirmations_ExpiresAtUtc",
                table: "AiBookingConfirmations",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AiBookingConfirmations_UserId_SessionId_DraftId_RevokedAtUtc",
                table: "AiBookingConfirmations",
                columns: new[] { "UserId", "SessionId", "DraftId", "RevokedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiBookingConfirmations");
        }
    }
}
