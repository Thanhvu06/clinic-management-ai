using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDiagnosticResultVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ReturnCount",
                table: "DiagnosticOrders",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ReturnReason",
                table: "DiagnosticOrders",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReturnedAtUtc",
                table: "DiagnosticOrders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReturnedByUserId",
                table: "DiagnosticOrders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SubmittedForVerificationAtUtc",
                table: "DiagnosticOrders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SubmittedForVerificationByUserId",
                table: "DiagnosticOrders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VerificationNote",
                table: "DiagnosticOrders",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VerificationStatus",
                table: "DiagnosticOrders",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "NotRequired");

            migrationBuilder.AddColumn<DateTime>(
                name: "VerifiedAtUtc",
                table: "DiagnosticOrders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "VerifiedByUserId",
                table: "DiagnosticOrders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DiagnosticOrders_VerificationStatus",
                table: "DiagnosticOrders",
                column: "VerificationStatus");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DiagnosticOrders_VerificationStatus",
                table: "DiagnosticOrders");

            migrationBuilder.DropColumn(
                name: "ReturnCount",
                table: "DiagnosticOrders");

            migrationBuilder.DropColumn(
                name: "ReturnReason",
                table: "DiagnosticOrders");

            migrationBuilder.DropColumn(
                name: "ReturnedAtUtc",
                table: "DiagnosticOrders");

            migrationBuilder.DropColumn(
                name: "ReturnedByUserId",
                table: "DiagnosticOrders");

            migrationBuilder.DropColumn(
                name: "SubmittedForVerificationAtUtc",
                table: "DiagnosticOrders");

            migrationBuilder.DropColumn(
                name: "SubmittedForVerificationByUserId",
                table: "DiagnosticOrders");

            migrationBuilder.DropColumn(
                name: "VerificationNote",
                table: "DiagnosticOrders");

            migrationBuilder.DropColumn(
                name: "VerificationStatus",
                table: "DiagnosticOrders");

            migrationBuilder.DropColumn(
                name: "VerifiedAtUtc",
                table: "DiagnosticOrders");

            migrationBuilder.DropColumn(
                name: "VerifiedByUserId",
                table: "DiagnosticOrders");
        }
    }
}
