using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHealthPackageRegistration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HealthPackageRegistrations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RegistrationCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    HealthPackageId = table.Column<long>(type: "bigint", nullable: false),
                    PatientId = table.Column<long>(type: "bigint", nullable: false),
                    PreferredDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ContactPhone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HealthPackageRegistrations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HealthPackageRegistrations_HealthPackages_HealthPackageId",
                        column: x => x.HealthPackageId,
                        principalTable: "HealthPackages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HealthPackageRegistrations_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HealthPackageRegistrations_HealthPackageId",
                table: "HealthPackageRegistrations",
                column: "HealthPackageId");

            migrationBuilder.CreateIndex(
                name: "IX_HealthPackageRegistrations_PatientId",
                table: "HealthPackageRegistrations",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_HealthPackageRegistrations_RegistrationCode",
                table: "HealthPackageRegistrations",
                column: "RegistrationCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HealthPackageRegistrations_Status",
                table: "HealthPackageRegistrations",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HealthPackageRegistrations");
        }
    }
}
