using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDiagnosticSpecimenTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContainerType",
                table: "DiagnosticServices",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SpecimenType",
                table: "DiagnosticServices",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CollectedAtUtc",
                table: "DiagnosticOrderItems",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CollectedByUserId",
                table: "DiagnosticOrderItems",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReceivedAtUtc",
                table: "DiagnosticOrderItems",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReceivedByUserId",
                table: "DiagnosticOrderItems",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RecollectCount",
                table: "DiagnosticOrderItems",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "RejectedAtUtc",
                table: "DiagnosticOrderItems",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RejectedByUserId",
                table: "DiagnosticOrderItems",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectionNote",
                table: "DiagnosticOrderItems",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                table: "DiagnosticOrderItems",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SpecimenBarcode",
                table: "DiagnosticOrderItems",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SpecimenStatus",
                table: "DiagnosticOrderItems",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "NotRequired");

            migrationBuilder.CreateIndex(
                name: "IX_DiagnosticOrderItems_SpecimenBarcode",
                table: "DiagnosticOrderItems",
                column: "SpecimenBarcode",
                unique: true,
                filter: "[SpecimenBarcode] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DiagnosticOrderItems_SpecimenBarcode",
                table: "DiagnosticOrderItems");

            migrationBuilder.DropColumn(
                name: "ContainerType",
                table: "DiagnosticServices");

            migrationBuilder.DropColumn(
                name: "SpecimenType",
                table: "DiagnosticServices");

            migrationBuilder.DropColumn(
                name: "CollectedAtUtc",
                table: "DiagnosticOrderItems");

            migrationBuilder.DropColumn(
                name: "CollectedByUserId",
                table: "DiagnosticOrderItems");

            migrationBuilder.DropColumn(
                name: "ReceivedAtUtc",
                table: "DiagnosticOrderItems");

            migrationBuilder.DropColumn(
                name: "ReceivedByUserId",
                table: "DiagnosticOrderItems");

            migrationBuilder.DropColumn(
                name: "RecollectCount",
                table: "DiagnosticOrderItems");

            migrationBuilder.DropColumn(
                name: "RejectedAtUtc",
                table: "DiagnosticOrderItems");

            migrationBuilder.DropColumn(
                name: "RejectedByUserId",
                table: "DiagnosticOrderItems");

            migrationBuilder.DropColumn(
                name: "RejectionNote",
                table: "DiagnosticOrderItems");

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                table: "DiagnosticOrderItems");

            migrationBuilder.DropColumn(
                name: "SpecimenBarcode",
                table: "DiagnosticOrderItems");

            migrationBuilder.DropColumn(
                name: "SpecimenStatus",
                table: "DiagnosticOrderItems");
        }
    }
}
