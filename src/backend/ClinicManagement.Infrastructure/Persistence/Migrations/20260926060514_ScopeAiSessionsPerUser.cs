using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ScopeAiSessionsPerUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AiSessions_SessionId",
                table: "AiSessions");

            migrationBuilder.CreateIndex(
                name: "IX_AiSessions_SessionId",
                table: "AiSessions",
                column: "SessionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AiSessions_SessionId",
                table: "AiSessions");

            migrationBuilder.CreateIndex(
                name: "IX_AiSessions_SessionId",
                table: "AiSessions",
                column: "SessionId",
                unique: true);
        }
    }
}
