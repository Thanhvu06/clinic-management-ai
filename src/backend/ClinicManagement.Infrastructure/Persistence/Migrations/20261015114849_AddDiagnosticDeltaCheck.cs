using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDiagnosticDeltaCheck : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "DeltaAbsoluteLimit",
                table: "DiagnosticServiceParameters",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DeltaPercentLimit",
                table: "DiagnosticServiceParameters",
                type: "decimal(9,2)",
                precision: 9,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeltaWindowDays",
                table: "DiagnosticServiceParameters",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DeltaChange",
                table: "DiagnosticResultValues",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DeltaPercent",
                table: "DiagnosticResultValues",
                type: "decimal(9,2)",
                precision: 9,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeltaFailure",
                table: "DiagnosticResultValues",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "PreviousNumericValue",
                table: "DiagnosticResultValues",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreviousOrderCode",
                table: "DiagnosticResultValues",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PreviousResultedAtUtc",
                table: "DiagnosticResultValues",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeltaAbsoluteLimit",
                table: "DiagnosticServiceParameters");

            migrationBuilder.DropColumn(
                name: "DeltaPercentLimit",
                table: "DiagnosticServiceParameters");

            migrationBuilder.DropColumn(
                name: "DeltaWindowDays",
                table: "DiagnosticServiceParameters");

            migrationBuilder.DropColumn(
                name: "DeltaChange",
                table: "DiagnosticResultValues");

            migrationBuilder.DropColumn(
                name: "DeltaPercent",
                table: "DiagnosticResultValues");

            migrationBuilder.DropColumn(
                name: "IsDeltaFailure",
                table: "DiagnosticResultValues");

            migrationBuilder.DropColumn(
                name: "PreviousNumericValue",
                table: "DiagnosticResultValues");

            migrationBuilder.DropColumn(
                name: "PreviousOrderCode",
                table: "DiagnosticResultValues");

            migrationBuilder.DropColumn(
                name: "PreviousResultedAtUtc",
                table: "DiagnosticResultValues");
        }
    }
}
