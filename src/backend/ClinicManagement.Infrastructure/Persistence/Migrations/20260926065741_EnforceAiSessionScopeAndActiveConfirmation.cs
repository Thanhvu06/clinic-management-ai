using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnforceAiSessionScopeAndActiveConfirmation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing deployments may contain duplicate rows from the period
            // when SessionId was not scoped. Keep the most recently active row
            // per scope before the unique indexes are created.
            migrationBuilder.Sql(@"
;WITH ranked_sessions AS
(
    SELECT Id,
           ROW_NUMBER() OVER
           (
               PARTITION BY SessionId, UserId
               ORDER BY CASE WHEN IsActive = 1 THEN 0 ELSE 1 END,
                        LastActiveAtUtc DESC,
                        Id DESC
           ) AS RowNumber
    FROM AiSessions
)
DELETE FROM AiSessions
WHERE Id IN (SELECT Id FROM ranked_sessions WHERE RowNumber > 1);");

            // There can be at most one live confirmation per scope. Preserve
            // the newest pending row and revoke older pending rows so they
            // remain auditable but cannot be used after the migration.
            migrationBuilder.Sql(@"
;WITH ranked_confirmations AS
(
    SELECT Id,
           ROW_NUMBER() OVER
           (
               PARTITION BY UserId, SessionId, DraftId
               ORDER BY CreatedAtUtc DESC, Id DESC
           ) AS RowNumber
    FROM AiBookingConfirmations
    WHERE UsedAtUtc IS NULL AND RevokedAtUtc IS NULL
)
UPDATE c
SET RevokedAtUtc = SYSUTCDATETIME()
FROM AiBookingConfirmations AS c
INNER JOIN ranked_confirmations AS r ON r.Id = c.Id
WHERE r.RowNumber > 1;");

            migrationBuilder.DropIndex(
                name: "IX_AiSessions_SessionId",
                table: "AiSessions");

            migrationBuilder.CreateIndex(
                name: "IX_AiSessions_SessionId",
                table: "AiSessions",
                column: "SessionId",
                unique: true,
                filter: "[UserId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AiSessions_SessionId_UserId",
                table: "AiSessions",
                columns: new[] { "SessionId", "UserId" },
                unique: true,
                filter: "[UserId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AiBookingConfirmations_UserId_SessionId_DraftId",
                table: "AiBookingConfirmations",
                columns: new[] { "UserId", "SessionId", "DraftId" },
                unique: true,
                filter: "[UsedAtUtc] IS NULL AND [RevokedAtUtc] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AiSessions_SessionId",
                table: "AiSessions");

            migrationBuilder.DropIndex(
                name: "IX_AiSessions_SessionId_UserId",
                table: "AiSessions");

            migrationBuilder.DropIndex(
                name: "IX_AiBookingConfirmations_UserId_SessionId_DraftId",
                table: "AiBookingConfirmations");

            migrationBuilder.CreateIndex(
                name: "IX_AiSessions_SessionId",
                table: "AiSessions",
                column: "SessionId");
        }
    }
}
