using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveDiagnosticStatusDefaultValues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "DiagnosticOrders",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldDefaultValue: "Ordered");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "DiagnosticOrderItems",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldDefaultValue: "Ordered");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "DiagnosticOrders",
                type: "nvarchar(450)",
                nullable: false,
                defaultValue: "Ordered",
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "DiagnosticOrderItems",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "Ordered",
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");
        }
    }
}
