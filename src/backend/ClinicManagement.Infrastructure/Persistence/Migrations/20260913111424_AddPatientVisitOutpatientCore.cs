using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPatientVisitOutpatientCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AppointmentVitalSigns_Appointments_AppointmentId",
                table: "AppointmentVitalSigns");

            migrationBuilder.DropForeignKey(
                name: "FK_Prescriptions_Appointments_AppointmentId",
                table: "Prescriptions");

            migrationBuilder.DropForeignKey(
                name: "FK_VisitSummaries_Appointments_AppointmentId",
                table: "VisitSummaries");

            migrationBuilder.DropIndex(
                name: "IX_VisitSummaries_AppointmentId",
                table: "VisitSummaries");

            migrationBuilder.DropIndex(
                name: "IX_Prescriptions_AppointmentId",
                table: "Prescriptions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Invoices_SingleSource",
                table: "Invoices");

            migrationBuilder.DropIndex(
                name: "IX_AppointmentVitalSigns_AppointmentId",
                table: "AppointmentVitalSigns");

            migrationBuilder.AlterColumn<long>(
                name: "AppointmentId",
                table: "VisitSummaries",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AddColumn<long>(
                name: "PatientVisitId",
                table: "VisitSummaries",
                type: "bigint",
                nullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "AppointmentId",
                table: "Prescriptions",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AddColumn<long>(
                name: "PatientVisitId",
                table: "Prescriptions",
                type: "bigint",
                nullable: true);

            migrationBuilder.AlterColumn<bool>(
                name: "IsActive",
                table: "Medicines",
                type: "bit",
                nullable: false,
                defaultValue: true,
                oldClrType: typeof(bool),
                oldType: "bit");

            migrationBuilder.AddColumn<decimal>(
                name: "UnitPrice",
                table: "Medicines",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "PatientVisitId",
                table: "Invoices",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Price",
                table: "DiagnosticServices",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "AppointmentId",
                table: "DiagnosticOrders",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AddColumn<long>(
                name: "FacilityId",
                table: "DiagnosticOrders",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "PatientVisitId",
                table: "DiagnosticOrders",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "PerformingDepartmentId",
                table: "DiagnosticOrders",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SpecialtyId",
                table: "Departments",
                type: "bigint",
                nullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "AppointmentId",
                table: "AppointmentVitalSigns",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AddColumn<long>(
                name: "PatientVisitId",
                table: "AppointmentVitalSigns",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DailyQueueSequences",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FacilityId = table.Column<long>(type: "bigint", nullable: false),
                    DepartmentId = table.Column<long>(type: "bigint", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    LastNumber = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyQueueSequences", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PatientVisits",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    VisitCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    PatientId = table.Column<long>(type: "bigint", nullable: false),
                    AppointmentId = table.Column<long>(type: "bigint", nullable: true),
                    FacilityId = table.Column<long>(type: "bigint", nullable: false),
                    DepartmentId = table.Column<long>(type: "bigint", nullable: false),
                    RoomId = table.Column<long>(type: "bigint", nullable: true),
                    AssignedDoctorId = table.Column<long>(type: "bigint", nullable: true),
                    VisitDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ArrivalType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Priority = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ChiefComplaint = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    QueueNumber = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CheckedInAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ConsultationStartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancellationReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PatientVisits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PatientVisits_Appointments_AppointmentId",
                        column: x => x.AppointmentId,
                        principalTable: "Appointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PatientVisits_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "Departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PatientVisits_Doctors_AssignedDoctorId",
                        column: x => x.AssignedDoctorId,
                        principalTable: "Doctors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PatientVisits_Facilities_FacilityId",
                        column: x => x.FacilityId,
                        principalTable: "Facilities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PatientVisits_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PatientVisits_Rooms_RoomId",
                        column: x => x.RoomId,
                        principalTable: "Rooms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StaffFacilityAssignments",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FacilityId = table.Column<long>(type: "bigint", nullable: false),
                    DepartmentId = table.Column<long>(type: "bigint", nullable: true),
                    Role = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsPrimary = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    AssignedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffFacilityAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StaffFacilityAssignments_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "Departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StaffFacilityAssignments_Facilities_FacilityId",
                        column: x => x.FacilityId,
                        principalTable: "Facilities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VisitSummaries_AppointmentId",
                table: "VisitSummaries",
                column: "AppointmentId",
                unique: true,
                filter: "[AppointmentId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_VisitSummaries_PatientVisitId",
                table: "VisitSummaries",
                column: "PatientVisitId",
                unique: true,
                filter: "[PatientVisitId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Prescriptions_AppointmentId",
                table: "Prescriptions",
                column: "AppointmentId",
                unique: true,
                filter: "[AppointmentId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Prescriptions_PatientVisitId",
                table: "Prescriptions",
                column: "PatientVisitId");

            migrationBuilder.CreateIndex(
                name: "IX_Medicines_Code",
                table: "Medicines",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_PatientVisitId",
                table: "Invoices",
                column: "PatientVisitId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Invoices_SingleSource",
                table: "Invoices",
                sql: "(([AppointmentId] IS NOT NULL OR [PatientVisitId] IS NOT NULL) AND [HealthPackageRegistrationId] IS NULL) OR ([AppointmentId] IS NULL AND [PatientVisitId] IS NULL AND [HealthPackageRegistrationId] IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_DiagnosticOrders_FacilityId_PerformingDepartmentId_Status",
                table: "DiagnosticOrders",
                columns: new[] { "FacilityId", "PerformingDepartmentId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_DiagnosticOrders_PatientVisitId",
                table: "DiagnosticOrders",
                column: "PatientVisitId");

            migrationBuilder.CreateIndex(
                name: "IX_DiagnosticOrders_PerformingDepartmentId",
                table: "DiagnosticOrders",
                column: "PerformingDepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Departments_SpecialtyId",
                table: "Departments",
                column: "SpecialtyId");

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentVitalSigns_AppointmentId",
                table: "AppointmentVitalSigns",
                column: "AppointmentId",
                unique: true,
                filter: "[AppointmentId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentVitalSigns_PatientVisitId",
                table: "AppointmentVitalSigns",
                column: "PatientVisitId",
                unique: true,
                filter: "[PatientVisitId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DailyQueueSequences_FacilityId_DepartmentId_Date",
                table: "DailyQueueSequences",
                columns: new[] { "FacilityId", "DepartmentId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PatientVisits_AppointmentId",
                table: "PatientVisits",
                column: "AppointmentId",
                unique: true,
                filter: "[AppointmentId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PatientVisits_AssignedDoctorId_VisitDate_Status",
                table: "PatientVisits",
                columns: new[] { "AssignedDoctorId", "VisitDate", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PatientVisits_DepartmentId",
                table: "PatientVisits",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_PatientVisits_FacilityId_DepartmentId_VisitDate_Status",
                table: "PatientVisits",
                columns: new[] { "FacilityId", "DepartmentId", "VisitDate", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PatientVisits_PatientId",
                table: "PatientVisits",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_PatientVisits_RoomId",
                table: "PatientVisits",
                column: "RoomId");

            migrationBuilder.CreateIndex(
                name: "IX_PatientVisits_VisitCode",
                table: "PatientVisits",
                column: "VisitCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PatientVisits_VisitDate_FacilityId_DepartmentId_QueueNumber",
                table: "PatientVisits",
                columns: new[] { "VisitDate", "FacilityId", "DepartmentId", "QueueNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StaffFacilityAssignments_DepartmentId",
                table: "StaffFacilityAssignments",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_StaffFacilityAssignments_FacilityId",
                table: "StaffFacilityAssignments",
                column: "FacilityId");

            migrationBuilder.CreateIndex(
                name: "IX_StaffFacilityAssignments_UserId_FacilityId_Role",
                table: "StaffFacilityAssignments",
                columns: new[] { "UserId", "FacilityId", "Role" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_AppointmentVitalSigns_Appointments_AppointmentId",
                table: "AppointmentVitalSigns",
                column: "AppointmentId",
                principalTable: "Appointments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AppointmentVitalSigns_PatientVisits_PatientVisitId",
                table: "AppointmentVitalSigns",
                column: "PatientVisitId",
                principalTable: "PatientVisits",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Departments_Specialties_SpecialtyId",
                table: "Departments",
                column: "SpecialtyId",
                principalTable: "Specialties",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_DiagnosticOrders_Departments_PerformingDepartmentId",
                table: "DiagnosticOrders",
                column: "PerformingDepartmentId",
                principalTable: "Departments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_DiagnosticOrders_Facilities_FacilityId",
                table: "DiagnosticOrders",
                column: "FacilityId",
                principalTable: "Facilities",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_DiagnosticOrders_PatientVisits_PatientVisitId",
                table: "DiagnosticOrders",
                column: "PatientVisitId",
                principalTable: "PatientVisits",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Invoices_PatientVisits_PatientVisitId",
                table: "Invoices",
                column: "PatientVisitId",
                principalTable: "PatientVisits",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Prescriptions_Appointments_AppointmentId",
                table: "Prescriptions",
                column: "AppointmentId",
                principalTable: "Appointments",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Prescriptions_PatientVisits_PatientVisitId",
                table: "Prescriptions",
                column: "PatientVisitId",
                principalTable: "PatientVisits",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_VisitSummaries_Appointments_AppointmentId",
                table: "VisitSummaries",
                column: "AppointmentId",
                principalTable: "Appointments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_VisitSummaries_PatientVisits_PatientVisitId",
                table: "VisitSummaries",
                column: "PatientVisitId",
                principalTable: "PatientVisits",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AppointmentVitalSigns_Appointments_AppointmentId",
                table: "AppointmentVitalSigns");

            migrationBuilder.DropForeignKey(
                name: "FK_AppointmentVitalSigns_PatientVisits_PatientVisitId",
                table: "AppointmentVitalSigns");

            migrationBuilder.DropForeignKey(
                name: "FK_Departments_Specialties_SpecialtyId",
                table: "Departments");

            migrationBuilder.DropForeignKey(
                name: "FK_DiagnosticOrders_Departments_PerformingDepartmentId",
                table: "DiagnosticOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_DiagnosticOrders_Facilities_FacilityId",
                table: "DiagnosticOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_DiagnosticOrders_PatientVisits_PatientVisitId",
                table: "DiagnosticOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_Invoices_PatientVisits_PatientVisitId",
                table: "Invoices");

            migrationBuilder.DropForeignKey(
                name: "FK_Prescriptions_Appointments_AppointmentId",
                table: "Prescriptions");

            migrationBuilder.DropForeignKey(
                name: "FK_Prescriptions_PatientVisits_PatientVisitId",
                table: "Prescriptions");

            migrationBuilder.DropForeignKey(
                name: "FK_VisitSummaries_Appointments_AppointmentId",
                table: "VisitSummaries");

            migrationBuilder.DropForeignKey(
                name: "FK_VisitSummaries_PatientVisits_PatientVisitId",
                table: "VisitSummaries");

            migrationBuilder.DropTable(
                name: "DailyQueueSequences");

            migrationBuilder.DropTable(
                name: "PatientVisits");

            migrationBuilder.DropTable(
                name: "StaffFacilityAssignments");

            migrationBuilder.DropIndex(
                name: "IX_VisitSummaries_AppointmentId",
                table: "VisitSummaries");

            migrationBuilder.DropIndex(
                name: "IX_VisitSummaries_PatientVisitId",
                table: "VisitSummaries");

            migrationBuilder.DropIndex(
                name: "IX_Prescriptions_AppointmentId",
                table: "Prescriptions");

            migrationBuilder.DropIndex(
                name: "IX_Prescriptions_PatientVisitId",
                table: "Prescriptions");

            migrationBuilder.DropIndex(
                name: "IX_Medicines_Code",
                table: "Medicines");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_PatientVisitId",
                table: "Invoices");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Invoices_SingleSource",
                table: "Invoices");

            migrationBuilder.DropIndex(
                name: "IX_DiagnosticOrders_FacilityId_PerformingDepartmentId_Status",
                table: "DiagnosticOrders");

            migrationBuilder.DropIndex(
                name: "IX_DiagnosticOrders_PatientVisitId",
                table: "DiagnosticOrders");

            migrationBuilder.DropIndex(
                name: "IX_DiagnosticOrders_PerformingDepartmentId",
                table: "DiagnosticOrders");

            migrationBuilder.DropIndex(
                name: "IX_Departments_SpecialtyId",
                table: "Departments");

            migrationBuilder.DropIndex(
                name: "IX_AppointmentVitalSigns_AppointmentId",
                table: "AppointmentVitalSigns");

            migrationBuilder.DropIndex(
                name: "IX_AppointmentVitalSigns_PatientVisitId",
                table: "AppointmentVitalSigns");

            migrationBuilder.DropColumn(
                name: "PatientVisitId",
                table: "VisitSummaries");

            migrationBuilder.DropColumn(
                name: "PatientVisitId",
                table: "Prescriptions");

            migrationBuilder.DropColumn(
                name: "UnitPrice",
                table: "Medicines");

            migrationBuilder.DropColumn(
                name: "PatientVisitId",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "Price",
                table: "DiagnosticServices");

            migrationBuilder.DropColumn(
                name: "FacilityId",
                table: "DiagnosticOrders");

            migrationBuilder.DropColumn(
                name: "PatientVisitId",
                table: "DiagnosticOrders");

            migrationBuilder.DropColumn(
                name: "PerformingDepartmentId",
                table: "DiagnosticOrders");

            migrationBuilder.DropColumn(
                name: "SpecialtyId",
                table: "Departments");

            migrationBuilder.DropColumn(
                name: "PatientVisitId",
                table: "AppointmentVitalSigns");

            migrationBuilder.AlterColumn<long>(
                name: "AppointmentId",
                table: "VisitSummaries",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "AppointmentId",
                table: "Prescriptions",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AlterColumn<bool>(
                name: "IsActive",
                table: "Medicines",
                type: "bit",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "bit",
                oldDefaultValue: true);

            migrationBuilder.AlterColumn<long>(
                name: "AppointmentId",
                table: "DiagnosticOrders",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "AppointmentId",
                table: "AppointmentVitalSigns",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_VisitSummaries_AppointmentId",
                table: "VisitSummaries",
                column: "AppointmentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Prescriptions_AppointmentId",
                table: "Prescriptions",
                column: "AppointmentId",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Invoices_SingleSource",
                table: "Invoices",
                sql: "([AppointmentId] IS NOT NULL AND [HealthPackageRegistrationId] IS NULL) OR ([AppointmentId] IS NULL AND [HealthPackageRegistrationId] IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentVitalSigns_AppointmentId",
                table: "AppointmentVitalSigns",
                column: "AppointmentId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_AppointmentVitalSigns_Appointments_AppointmentId",
                table: "AppointmentVitalSigns",
                column: "AppointmentId",
                principalTable: "Appointments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Prescriptions_Appointments_AppointmentId",
                table: "Prescriptions",
                column: "AppointmentId",
                principalTable: "Appointments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_VisitSummaries_Appointments_AppointmentId",
                table: "VisitSummaries",
                column: "AppointmentId",
                principalTable: "Appointments",
                principalColumn: "Id");
        }
    }
}
