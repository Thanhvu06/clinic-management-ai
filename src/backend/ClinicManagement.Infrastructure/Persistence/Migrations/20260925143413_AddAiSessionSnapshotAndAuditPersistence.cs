using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAiSessionSnapshotAndAuditPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiAuditLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SessionId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    DraftId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    DraftVersion = table.Column<int>(type: "int", nullable: true),
                    FacilityId = table.Column<long>(type: "bigint", nullable: true),
                    ActionType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ErrorCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    MetadataJson = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    TimestampUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiAuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AiCancelledDraftScopes",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SessionId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    DraftId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    FacilityId = table.Column<long>(type: "bigint", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiCancelledDraftScopes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AiSelectionSnapshots",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SnapshotId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SessionId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    DraftId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    DraftVersion = table.Column<int>(type: "int", nullable: true),
                    FacilityId = table.Column<long>(type: "bigint", nullable: true),
                    SpecialtyId = table.Column<long>(type: "bigint", nullable: true),
                    DoctorId = table.Column<long>(type: "bigint", nullable: true),
                    SlotDate = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    DoctorIdsJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    SlotIdsJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsRevoked = table.Column<bool>(type: "bit", nullable: false),
                    RevokedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiSelectionSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AiSessions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SessionId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ActiveDraftId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ActiveDraftVersion = table.Column<int>(type: "int", nullable: true),
                    FacilityId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastActiveAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiSessions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiAuditLogs_ActionType",
                table: "AiAuditLogs",
                column: "ActionType");

            migrationBuilder.CreateIndex(
                name: "IX_AiAuditLogs_SessionId_TimestampUtc",
                table: "AiAuditLogs",
                columns: new[] { "SessionId", "TimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AiAuditLogs_UserId_TimestampUtc",
                table: "AiAuditLogs",
                columns: new[] { "UserId", "TimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AiCancelledDraftScopes_ExpiresAtUtc",
                table: "AiCancelledDraftScopes",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AiCancelledDraftScopes_UserId_SessionId_DraftId",
                table: "AiCancelledDraftScopes",
                columns: new[] { "UserId", "SessionId", "DraftId" },
                unique: true,
                filter: "[UserId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AiSelectionSnapshots_ExpiresAtUtc",
                table: "AiSelectionSnapshots",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AiSelectionSnapshots_SnapshotId",
                table: "AiSelectionSnapshots",
                column: "SnapshotId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiSelectionSnapshots_UserId_SessionId_DraftId",
                table: "AiSelectionSnapshots",
                columns: new[] { "UserId", "SessionId", "DraftId" });

            migrationBuilder.CreateIndex(
                name: "IX_AiSessions_ExpiresAtUtc",
                table: "AiSessions",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AiSessions_SessionId",
                table: "AiSessions",
                column: "SessionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiSessions_UserId_IsActive",
                table: "AiSessions",
                columns: new[] { "UserId", "IsActive" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiAuditLogs");

            migrationBuilder.DropTable(
                name: "AiCancelledDraftScopes");

            migrationBuilder.DropTable(
                name: "AiSelectionSnapshots");

            migrationBuilder.DropTable(
                name: "AiSessions");
        }
    }
}
