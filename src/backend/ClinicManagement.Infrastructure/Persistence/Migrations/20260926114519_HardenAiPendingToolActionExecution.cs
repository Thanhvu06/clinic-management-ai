using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HardenAiPendingToolActionExecution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AiPendingToolActions_UserId_ResourceType_ResourceId",
                table: "AiPendingToolActions");

            migrationBuilder.AddColumn<Guid>(
                name: "SourceAiActionId",
                table: "AppointmentChangeRequests",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ExecutionAttemptCount",
                table: "AiPendingToolActions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExecutionLeaseExpiresAtUtc",
                table: "AiPendingToolActions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ExecutionLeaseId",
                table: "AiPendingToolActions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastErrorCode",
                table: "AiPendingToolActions",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "State",
                table: "AiPendingToolActions",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            // Backfill legacy Phase 1.1 rows before making State mandatory.
            // Executed/cancelled rows remain terminal tombstones; only genuinely
            // active rows participate in the new filtered unique index.
            ExecuteSqlAfterDdl(migrationBuilder, @"
UPDATE [AiPendingToolActions]
SET [State] = CASE
    WHEN [ExecutedAtUtc] IS NOT NULL THEN 'Completed'
    WHEN [CancelledAtUtc] IS NOT NULL THEN 'Cancelled'
    WHEN [ExpiresAtUtc] <= SYSUTCDATETIME() THEN 'Expired'
    ELSE 'PendingConfirmation'
END
WHERE [State] IS NULL;");

            migrationBuilder.AlterColumn<string>(
                name: "State",
                table: "AiPendingToolActions",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(32)",
                oldMaxLength: 32,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentChangeRequests_SourceAiActionId",
                table: "AppointmentChangeRequests",
                column: "SourceAiActionId",
                unique: true,
                filter: "[SourceAiActionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AiPendingToolActions_UserId_ResourceType_ResourceId",
                table: "AiPendingToolActions",
                columns: new[] { "UserId", "ResourceType", "ResourceId" },
                unique: true,
                filter: "[State] IN ('PendingConfirmation', 'Executing', 'FailedRetryable')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new InvalidOperationException("HardenAiPendingToolActionExecution cannot be rolled back automatically because SourceAiActionId and execution state are durable idempotency data.");

#pragma warning disable CS0162
            migrationBuilder.DropIndex(
                name: "IX_AppointmentChangeRequests_SourceAiActionId",
                table: "AppointmentChangeRequests");

            migrationBuilder.DropIndex(
                name: "IX_AiPendingToolActions_UserId_ResourceType_ResourceId",
                table: "AiPendingToolActions");

            migrationBuilder.DropColumn(
                name: "SourceAiActionId",
                table: "AppointmentChangeRequests");

            migrationBuilder.DropColumn(
                name: "ExecutionAttemptCount",
                table: "AiPendingToolActions");

            migrationBuilder.DropColumn(
                name: "ExecutionLeaseExpiresAtUtc",
                table: "AiPendingToolActions");

            migrationBuilder.DropColumn(
                name: "ExecutionLeaseId",
                table: "AiPendingToolActions");

            migrationBuilder.DropColumn(
                name: "LastErrorCode",
                table: "AiPendingToolActions");

            migrationBuilder.DropColumn(
                name: "State",
                table: "AiPendingToolActions");

            migrationBuilder.CreateIndex(
                name: "IX_AiPendingToolActions_UserId_ResourceType_ResourceId",
                table: "AiPendingToolActions",
                columns: new[] { "UserId", "ResourceType", "ResourceId" },
                unique: true,
                filter: "[ExecutedAtUtc] IS NULL AND [CancelledAtUtc] IS NULL");
#pragma warning restore CS0162
        }

        private static void ExecuteSqlAfterDdl(MigrationBuilder migrationBuilder, string sql)
        {
            if (migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer")
            {
                migrationBuilder.Sql($"EXEC(N'{sql.Replace("'", "''")}');");
                return;
            }

            migrationBuilder.Sql(sql);
        }
    }
}
