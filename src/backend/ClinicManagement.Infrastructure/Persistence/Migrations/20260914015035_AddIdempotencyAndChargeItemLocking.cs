using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIdempotencyAndChargeItemLocking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "HealthPackageRegistrationId",
                table: "PatientVisits",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsCancelled",
                table: "InvoiceItems",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsPackageCovered",
                table: "DiagnosticOrderItems",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "PackageRegistrationId",
                table: "DiagnosticOrderItems",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "IdempotencyRecords",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Scope = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    StatusCode = table.Column<int>(type: "int", nullable: false),
                    ResponseBody = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdempotencyRecords", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PatientVisits_HealthPackageRegistrationId",
                table: "PatientVisits",
                column: "HealthPackageRegistrationId",
                filter: "[HealthPackageRegistrationId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceItems_ReferenceType_ReferenceId",
                table: "InvoiceItems",
                columns: new[] { "ReferenceType", "ReferenceId" },
                unique: true,
                filter: "[ReferenceType] <> '' AND [ReferenceId] > 0 AND [IsCancelled] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_DiagnosticOrderItems_PackageRegistrationId",
                table: "DiagnosticOrderItems",
                column: "PackageRegistrationId");

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyRecords_ExpiresAtUtc",
                table: "IdempotencyRecords",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyRecords_Key_Scope",
                table: "IdempotencyRecords",
                columns: new[] { "Key", "Scope" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_DiagnosticOrderItems_HealthPackageRegistrations_PackageRegistrationId",
                table: "DiagnosticOrderItems",
                column: "PackageRegistrationId",
                principalTable: "HealthPackageRegistrations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PatientVisits_HealthPackageRegistrations_HealthPackageRegistrationId",
                table: "PatientVisits",
                column: "HealthPackageRegistrationId",
                principalTable: "HealthPackageRegistrations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DiagnosticOrderItems_HealthPackageRegistrations_PackageRegistrationId",
                table: "DiagnosticOrderItems");

            migrationBuilder.DropForeignKey(
                name: "FK_PatientVisits_HealthPackageRegistrations_HealthPackageRegistrationId",
                table: "PatientVisits");

            migrationBuilder.DropTable(
                name: "IdempotencyRecords");

            migrationBuilder.DropIndex(
                name: "IX_PatientVisits_HealthPackageRegistrationId",
                table: "PatientVisits");

            migrationBuilder.DropIndex(
                name: "IX_InvoiceItems_ReferenceType_ReferenceId",
                table: "InvoiceItems");

            migrationBuilder.DropIndex(
                name: "IX_DiagnosticOrderItems_PackageRegistrationId",
                table: "DiagnosticOrderItems");

            migrationBuilder.DropColumn(
                name: "HealthPackageRegistrationId",
                table: "PatientVisits");

            migrationBuilder.DropColumn(
                name: "IsCancelled",
                table: "InvoiceItems");

            migrationBuilder.DropColumn(
                name: "IsPackageCovered",
                table: "DiagnosticOrderItems");

            migrationBuilder.DropColumn(
                name: "PackageRegistrationId",
                table: "DiagnosticOrderItems");
        }
    }
}
