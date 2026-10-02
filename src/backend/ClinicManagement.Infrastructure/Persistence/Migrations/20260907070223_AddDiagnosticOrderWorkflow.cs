using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDiagnosticOrderWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DiagnosticOrders",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    AppointmentId = table.Column<long>(type: "bigint", nullable: false),
                    PatientId = table.Column<long>(type: "bigint", nullable: false),
                    OrderingDoctorId = table.Column<long>(type: "bigint", nullable: false),
                    ClinicalIndication = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(450)", nullable: false, defaultValue: "Ordered"),
                    OrderedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    StartedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompletedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReviewedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReviewedByDoctorId = table.Column<long>(type: "bigint", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiagnosticOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DiagnosticOrders_Appointments_AppointmentId",
                        column: x => x.AppointmentId,
                        principalTable: "Appointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DiagnosticOrders_Doctors_OrderingDoctorId",
                        column: x => x.OrderingDoctorId,
                        principalTable: "Doctors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DiagnosticOrders_Doctors_ReviewedByDoctorId",
                        column: x => x.ReviewedByDoctorId,
                        principalTable: "Doctors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DiagnosticOrders_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DiagnosticServices",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PreparationInstructions = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiagnosticServices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DiagnosticOrderItems",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DiagnosticOrderId = table.Column<long>(type: "bigint", nullable: false),
                    DiagnosticServiceId = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValue: "Ordered"),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiagnosticOrderItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DiagnosticOrderItems_DiagnosticOrders_DiagnosticOrderId",
                        column: x => x.DiagnosticOrderId,
                        principalTable: "DiagnosticOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DiagnosticOrderItems_DiagnosticServices_DiagnosticServiceId",
                        column: x => x.DiagnosticServiceId,
                        principalTable: "DiagnosticServices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DiagnosticResults",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DiagnosticOrderItemId = table.Column<long>(type: "bigint", nullable: false),
                    ResultText = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    Conclusion = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ReferenceRange = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Unit = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ResultedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ResultedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiagnosticResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DiagnosticResults_DiagnosticOrderItems_DiagnosticOrderItemId",
                        column: x => x.DiagnosticOrderItemId,
                        principalTable: "DiagnosticOrderItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DiagnosticOrderItems_DiagnosticOrderId_DiagnosticServiceId",
                table: "DiagnosticOrderItems",
                columns: new[] { "DiagnosticOrderId", "DiagnosticServiceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DiagnosticOrderItems_DiagnosticServiceId",
                table: "DiagnosticOrderItems",
                column: "DiagnosticServiceId");

            migrationBuilder.CreateIndex(
                name: "IX_DiagnosticOrders_AppointmentId",
                table: "DiagnosticOrders",
                column: "AppointmentId");

            migrationBuilder.CreateIndex(
                name: "IX_DiagnosticOrders_OrderCode",
                table: "DiagnosticOrders",
                column: "OrderCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DiagnosticOrders_OrderingDoctorId_Status",
                table: "DiagnosticOrders",
                columns: new[] { "OrderingDoctorId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_DiagnosticOrders_PatientId_OrderedAtUtc",
                table: "DiagnosticOrders",
                columns: new[] { "PatientId", "OrderedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_DiagnosticOrders_ReviewedByDoctorId",
                table: "DiagnosticOrders",
                column: "ReviewedByDoctorId");

            migrationBuilder.CreateIndex(
                name: "IX_DiagnosticOrders_Status_OrderedAtUtc",
                table: "DiagnosticOrders",
                columns: new[] { "Status", "OrderedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_DiagnosticResults_DiagnosticOrderItemId",
                table: "DiagnosticResults",
                column: "DiagnosticOrderItemId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DiagnosticServices_Code",
                table: "DiagnosticServices",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DiagnosticResults");

            migrationBuilder.DropTable(
                name: "DiagnosticOrderItems");

            migrationBuilder.DropTable(
                name: "DiagnosticOrders");

            migrationBuilder.DropTable(
                name: "DiagnosticServices");
        }
    }
}
