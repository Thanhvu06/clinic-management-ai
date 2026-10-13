using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDiagnosticResultParameters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CriticalNotifiedAtUtc",
                table: "DiagnosticResults",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HasCriticalValue",
                table: "DiagnosticResults",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "DiagnosticServiceParameters",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DiagnosticServiceId = table.Column<long>(type: "bigint", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    RefLow = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    RefHigh = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    RefLowFemale = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    RefHighFemale = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    CriticalLow = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    CriticalHigh = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiagnosticServiceParameters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DiagnosticServiceParameters_DiagnosticServices_DiagnosticServiceId",
                        column: x => x.DiagnosticServiceId,
                        principalTable: "DiagnosticServices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DiagnosticResultValues",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DiagnosticResultId = table.Column<long>(type: "bigint", nullable: false),
                    DiagnosticServiceParameterId = table.Column<long>(type: "bigint", nullable: false),
                    ParameterCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ParameterName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    RefLow = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    RefHigh = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    CriticalLow = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    CriticalHigh = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    NumericValue = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Flag = table.Column<int>(type: "int", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiagnosticResultValues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DiagnosticResultValues_DiagnosticResults_DiagnosticResultId",
                        column: x => x.DiagnosticResultId,
                        principalTable: "DiagnosticResults",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DiagnosticResultValues_DiagnosticServiceParameters_DiagnosticServiceParameterId",
                        column: x => x.DiagnosticServiceParameterId,
                        principalTable: "DiagnosticServiceParameters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DiagnosticResultValues_DiagnosticResultId_DiagnosticServiceParameterId",
                table: "DiagnosticResultValues",
                columns: new[] { "DiagnosticResultId", "DiagnosticServiceParameterId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DiagnosticResultValues_DiagnosticServiceParameterId",
                table: "DiagnosticResultValues",
                column: "DiagnosticServiceParameterId");

            migrationBuilder.CreateIndex(
                name: "IX_DiagnosticServiceParameters_DiagnosticServiceId_Code",
                table: "DiagnosticServiceParameters",
                columns: new[] { "DiagnosticServiceId", "Code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DiagnosticResultValues");

            migrationBuilder.DropTable(
                name: "DiagnosticServiceParameters");

            migrationBuilder.DropColumn(
                name: "CriticalNotifiedAtUtc",
                table: "DiagnosticResults");

            migrationBuilder.DropColumn(
                name: "HasCriticalValue",
                table: "DiagnosticResults");
        }
    }
}
