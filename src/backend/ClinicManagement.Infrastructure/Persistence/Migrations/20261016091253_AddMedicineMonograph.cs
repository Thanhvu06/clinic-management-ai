using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMedicineMonograph : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Contraindications",
                table: "Medicines",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DosageAndAdministration",
                table: "Medicines",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Indications",
                table: "Medicines",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OverdoseManagement",
                table: "Medicines",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Precautions",
                table: "Medicines",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SideEffects",
                table: "Medicines",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Contraindications",
                table: "Medicines");

            migrationBuilder.DropColumn(
                name: "DosageAndAdministration",
                table: "Medicines");

            migrationBuilder.DropColumn(
                name: "Indications",
                table: "Medicines");

            migrationBuilder.DropColumn(
                name: "OverdoseManagement",
                table: "Medicines");

            migrationBuilder.DropColumn(
                name: "Precautions",
                table: "Medicines");

            migrationBuilder.DropColumn(
                name: "SideEffects",
                table: "Medicines");
        }
    }
}
