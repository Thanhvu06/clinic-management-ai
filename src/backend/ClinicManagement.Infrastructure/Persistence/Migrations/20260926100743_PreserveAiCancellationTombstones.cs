using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PreserveAiCancellationTombstones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Cancellation tombstones are terminal anti-replay records. This
            // update is safe to run repeatedly and never deletes any row.
            migrationBuilder.Sql(@"
UPDATE [AiCancelledDraftScopes]
SET [ExpiresAtUtc] = CONVERT(datetime2(7), '9999-12-31T23:59:59.9999999')
WHERE [ExpiresAtUtc] <> CONVERT(datetime2(7), '9999-12-31T23:59:59.9999999');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Rollback restores the former seven-day value without removing
            // tombstones or touching any patient data.
            migrationBuilder.Sql(@"
UPDATE [AiCancelledDraftScopes]
SET [ExpiresAtUtc] = DATEADD(day, 7, [CancelledAtUtc])
WHERE [ExpiresAtUtc] = CONVERT(datetime2(7), '9999-12-31T23:59:59.9999999');");
        }
    }
}
