using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDiagnosticAttachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DiagnosticResultAttachments",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DiagnosticOrderItemId = table.Column<long>(type: "bigint", nullable: false),
                    StoredFileName = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    OriginalFileName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    UploadedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UploadedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiagnosticResultAttachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DiagnosticResultAttachments_DiagnosticOrderItems_DiagnosticOrderItemId",
                        column: x => x.DiagnosticOrderItemId,
                        principalTable: "DiagnosticOrderItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DiagnosticResultAttachments_DiagnosticOrderItemId",
                table: "DiagnosticResultAttachments",
                column: "DiagnosticOrderItemId");

            migrationBuilder.CreateIndex(
                name: "IX_DiagnosticResultAttachments_StoredFileName",
                table: "DiagnosticResultAttachments",
                column: "StoredFileName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DiagnosticResultAttachments");
        }
    }
}
