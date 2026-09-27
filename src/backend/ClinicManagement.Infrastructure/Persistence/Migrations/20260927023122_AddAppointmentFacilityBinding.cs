using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAppointmentFacilityBinding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "FacilityId",
                table: "Appointments",
                type: "bigint",
                nullable: true);

            // Backfill only unambiguous legacy rows. A visit is authoritative;
            // otherwise a facility is selected only if the appointment's
            // doctor has exactly one active, specialty-compatible facility.
            // Ambiguous rows intentionally remain NULL and the runtime fails
            // their cross-facility actions closed.
            migrationBuilder.Sql("""
                UPDATE appointment
                SET FacilityId = visit.FacilityId
                FROM Appointments AS appointment
                INNER JOIN PatientVisits AS visit ON visit.AppointmentId = appointment.Id
                WHERE appointment.FacilityId IS NULL;

                ;WITH CandidateFacilities AS
                (
                    SELECT appointment.Id, assignment.FacilityId
                    FROM Appointments AS appointment
                    INNER JOIN Doctors AS doctor ON doctor.Id = appointment.DoctorId
                    INNER JOIN StaffFacilityAssignments AS assignment
                        ON assignment.UserId = doctor.UserId
                        AND assignment.IsActive = CAST(1 AS bit)
                        AND assignment.Role = N'Doctor'
                    INNER JOIN Departments AS department
                        ON department.FacilityId = assignment.FacilityId
                        AND department.IsActive = CAST(1 AS bit)
                        AND department.SpecialtyId = appointment.SpecialtyId
                        AND (assignment.DepartmentId IS NULL OR assignment.DepartmentId = department.Id)
                    INNER JOIN Facilities AS facility
                        ON facility.Id = assignment.FacilityId
                        AND facility.IsActive = CAST(1 AS bit)
                    WHERE appointment.FacilityId IS NULL
                    GROUP BY appointment.Id, assignment.FacilityId
                ),
                UniqueFacilities AS
                (
                    SELECT Id, MIN(FacilityId) AS FacilityId
                    FROM CandidateFacilities
                    GROUP BY Id
                    HAVING COUNT(*) = 1
                )
                UPDATE appointment
                SET FacilityId = uniqueFacility.FacilityId
                FROM Appointments AS appointment
                INNER JOIN UniqueFacilities AS uniqueFacility ON uniqueFacility.Id = appointment.Id
                WHERE appointment.FacilityId IS NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_FacilityId_AppointmentDate",
                table: "Appointments",
                columns: new[] { "FacilityId", "AppointmentDate" });

            migrationBuilder.AddForeignKey(
                name: "FK_Appointments_Facilities_FacilityId",
                table: "Appointments",
                column: "FacilityId",
                principalTable: "Facilities",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Appointments_Facilities_FacilityId",
                table: "Appointments");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_FacilityId_AppointmentDate",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "FacilityId",
                table: "Appointments");
        }
    }
}
