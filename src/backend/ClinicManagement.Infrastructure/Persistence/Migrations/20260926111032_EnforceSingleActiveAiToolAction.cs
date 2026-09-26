using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnforceSingleActiveAiToolAction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_AiPendingToolActions_UserId_ResourceType_ResourceId",
                table: "AiPendingToolActions",
                columns: new[] { "UserId", "ResourceType", "ResourceId" },
                unique: true,
                filter: "[ExecutedAtUtc] IS NULL AND [CancelledAtUtc] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AiPendingToolActions_UserId_ResourceType_ResourceId",
                table: "AiPendingToolActions");
        }
    }
}
