using System.Text.RegularExpressions;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Identity;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ClinicManagement.IntegrationTests;

public sealed class ShortDisplayCodeMigrationTests
{
    [Fact]
    public async Task Idempotent_migration_keeps_legacy_data_and_concurrent_inserts_get_distinct_codes()
    {
        Assert.True(await SqlServerTestHelper.IsSqlServerAvailableAsync());
        var database = $"ClinicShortCodes_{Guid.NewGuid():N}";
        await SqlServerTestHelper.CreateDatabaseAsync(database);
        try
        {
            var options = SqlServerTestHelper.CreateSqlServerOptions(SqlServerTestHelper.GetTestDatabaseConnectionString(database));
            await using var db = new AppDbContext(options);
            await db.Database.MigrateAsync();
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(), FullName = "Bác sĩ kiểm thử", IsActive = true,
                Email = "shortcodes@test.invalid", PhoneNumber = "0900000011"
            };
            var doctor = new Doctor { UserId = user.Id, IsActive = true };
            var specialty = new Specialty { SpecialtyCode = "SSC", Name = "Kiểm thử", IsActive = true };
            var patient = new Patient { FullName = "Bệnh nhân kiểm thử", MedicalRecordNumber = "BN-2026-001234" };
            db.AddRange(user, doctor, specialty, patient);
            var day = new DateOnly(2026, 11, 1);
            var slots = Enumerable.Range(0, 22).Select(i => new AppointmentSlot
            {
                Doctor = doctor, SlotDate = day.AddDays(i), StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(9, 30)
            }).ToArray();
            db.AddRange(slots);
            await db.SaveChangesAsync();

            var later = AppointmentAt(slots[0], doctor.Id, patient.Id, specialty.Id);
            var earlier = AppointmentAt(slots[1], doctor.Id, patient.Id, specialty.Id);
            db.AddRange(later, earlier);
            db.AppointmentHistories.AddRange(
                new AppointmentHistory { Appointment = later, Action = AppointmentHistoryAction.Created, CreatedAt = new DateTime(2026, 10, 2) },
                new AppointmentHistory { Appointment = earlier, Action = AppointmentHistoryAction.Created, CreatedAt = new DateTime(2026, 10, 1) });
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var migrations = db.Database.GetMigrations().ToArray();
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync(migrations[^2]);
            var script = migrator.GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent);
            await db.Database.OpenConnectionAsync();
            try
            {
                for (var pass = 0; pass < 2; pass++)
                {
                    foreach (var batch in Regex.Split(script, @"^GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
                        if (!string.IsNullOrWhiteSpace(batch)) await db.Database.ExecuteSqlRawAsync(batch);

                    var appointments = await db.Appointments.AsNoTracking().ToDictionaryAsync(a => a.Id);
                    Assert.Equal(2, appointments.Count);
                    Assert.Equal((later.AppointmentCode, "LH0002"), (appointments[later.Id].AppointmentCode, appointments[later.Id].ShortCode));
                    Assert.Equal((earlier.AppointmentCode, "LH0001"), (appointments[earlier.Id].AppointmentCode, appointments[earlier.Id].ShortCode));
                    var oldPatient = await db.Patients.AsNoTracking().SingleAsync();
                    Assert.Equal((patient.Id, "BN-2026-001234", "BN0001"), (oldPatient.Id, oldPatient.MedicalRecordNumber, oldPatient.ShortCode));
                }
            }
            finally { await db.Database.CloseConnectionAsync(); }

            var codes = await Task.WhenAll(slots.Skip(2).Select(async slot =>
            {
                await using var booking = new AppDbContext(options);
                var appointment = AppointmentAt(slot, doctor.Id, patient.Id, specialty.Id);
                booking.Appointments.Add(appointment);
                await booking.SaveChangesAsync();
                return appointment.ShortCode;
            }));
            Assert.Equal(Enumerable.Range(3, 20).Select(number => $"LH{number:D4}"), codes.OrderBy(code => code));
        }
        finally { await SqlServerTestHelper.DropDatabaseAsync(database); }
    }

    private static Appointment AppointmentAt(AppointmentSlot slot, long doctorId, long patientId, long specialtyId) => new()
    {
        AppointmentCode = $"APT-OLD-{slot.Id}", AppointmentSlotId = slot.Id,
        AppointmentDate = slot.SlotDate, StartTime = slot.StartTime, EndTime = slot.EndTime,
        DoctorId = doctorId, PatientId = patientId, SpecialtyId = specialtyId
    };
}
