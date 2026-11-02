using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Persistence;

internal static class TwentyFourSevenDemoSeed
{
    internal static async Task SeedAsync(AppDbContext db, IDateTimeProvider clock, Guid actor)
    {
        string[] names = ["Võ Minh Anh", "Lê Ngọc Mai", "Trần Thu Thảo", "Phạm Thanh Hà", "Nguyễn Bảo Trâm", "Đặng Thùy Linh", "Bùi Kim Ngân", "Đỗ Quỳnh Hoa",
            "Hoàng Gia Huy", "Phan Đức Minh", "Huỳnh Quốc An", "Vũ Thành Long", "Trịnh Tuấn Kiệt", "Cao Minh Quân", "Hồ Thanh Sơn", "Mai Nhật Nam"];
        for (var index = 0; index < 32; index++)
        {
            var mrn = $"BN-{clock.VietnamToday.Year}-{3001 + index:D6}";
            if (await db.Patients.AnyAsync(p => p.MedicalRecordNumber == mrn)) continue;
            db.Patients.Add(new Patient
            {
                FullName = names[index % names.Length], MedicalRecordNumber = mrn,
                PhoneNumber = $"089900{index + 1:D4}", Gender = index % 16 < 8 ? Gender.Female : Gender.Male,
                DateOfBirth = clock.VietnamToday.AddYears(-25 - index)
            });
        }
        await db.SaveChangesAsync();
        var emails = new[] { "doctor@cliniccare.local", "bacsi.02@cliniccare.local", "bacsi.03@cliniccare.local" };
        var users = await db.Users.Where(u => emails.Contains(u.Email)).ToListAsync();
        var userIds = users.Select(u => u.Id).ToList();
        var doctors = await db.Doctors.Include(d => d.DoctorSpecialties).ThenInclude(ds => ds.Specialty)
            .Where(d => userIds.Contains(d.UserId)).ToListAsync();
        doctors = emails.Select(email => doctors.Single(d => d.UserId == users.Single(u => u.Email == email).Id)).ToList();
        var patients = await db.Patients.OrderByDescending(p => p.Email == "patient@cliniccare.local").ThenBy(p => p.Id).ToListAsync();
        var used = (await db.Appointments.Select(a => new { a.PatientId, a.AppointmentDate }).ToListAsync())
            .Select(a => (a.PatientId, a.AppointmentDate)).ToHashSet();
        var earliest = clock.VietnamNow.AddMinutes(30);
        var start = new DateTime(earliest.Year, earliest.Month, earliest.Day, earliest.Hour, earliest.Minute < 30 ? 0 : 30, 0);
        if (start < earliest) start = start.AddMinutes(30);
        for (var index = 0; index < 24; index++)
        {
            var at = start.AddHours(index);
            if (TimeOnly.FromDateTime(at) == new TimeOnly(23, 30)) at = at.AddMinutes(30);
            var date = DateOnly.FromDateTime(at);
            var time = TimeOnly.FromDateTime(at);
            var doctor = doctors[index % doctors.Count];
            var specialty = doctor.DoctorSpecialties.Single(ds => ds.IsPrimary).Specialty;
            var patient = patients.First(p => !used.Contains((p.Id, date)) && DemoPatientDevelopmentSeed.Allows(p, specialty.SpecialtyCode, date));
            used.Add((patient.Id, date));
            var slot = await db.AppointmentSlots.SingleAsync(s => s.DoctorId == doctor.Id && s.SlotDate == date && s.StartTime == time && !s.IsBooked);
            var facility = await db.StaffFacilityAssignments.Where(a => a.UserId == doctor.UserId && a.IsActive && a.Role == "Doctor")
                .OrderByDescending(a => a.IsPrimary).Select(a => a.FacilityId).FirstAsync();
            slot.IsBooked = true;
            var appointment = new Appointment
            {
                AppointmentCode = $"APT-{date:yyMMdd}-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}",
                PatientId = patient.Id, DoctorId = doctor.Id, SpecialtyId = specialty.Id, FacilityId = facility,
                AppointmentSlotId = slot.Id, AppointmentDate = date, StartTime = slot.StartTime, EndTime = slot.EndTime,
                Status = AppointmentStatus.Confirmed, Reason = RealisticDevelopmentSeed.Reason(specialty.SpecialtyCode, index),
                Histories = [new AppointmentHistory { Action = AppointmentHistoryAction.Created, NewStatus = AppointmentStatus.Pending,
                    PerformedByUserId = patient.UserId ?? actor, CreatedAt = clock.UtcNow.AddMinutes(-20) },
                    new AppointmentHistory { Action = AppointmentHistoryAction.Confirmed, OldStatus = AppointmentStatus.Pending,
                        NewStatus = AppointmentStatus.Confirmed, PerformedByUserId = actor, CreatedAt = clock.UtcNow.AddMinutes(-10) }]
            };
            db.Appointments.Add(appointment);
        }
        var main = doctors[0];
        var mainSpecialtyId = main.DoctorSpecialties.Single(s => s.IsPrimary).SpecialtyId;
        var department = await db.Departments.SingleAsync(d => d.IsActive && d.SpecialtyId == mainSpecialtyId);
        var waitingPatient = patients.First(p => !used.Contains((p.Id, clock.VietnamToday)) && DemoPatientDevelopmentSeed.Allows(p, "SP01", clock.VietnamToday));
        db.PatientVisits.Add(new PatientVisit
        {
            VisitCode = $"VIS-{clock.VietnamToday:yyyyMMdd}-1000", PatientId = waitingPatient.Id,
            FacilityId = department.FacilityId, DepartmentId = department.Id, AssignedDoctorId = main.Id,
            VisitDate = clock.VietnamToday, QueueNumber = 1000, Status = VisitStatus.WaitingForDoctor,
            CheckedInAtUtc = clock.UtcNow, CreatedAtUtc = clock.UtcNow, CreatedByUserId = actor,
            ChiefComplaint = "Khám nội tổng quát"
        });
        await db.SaveChangesAsync();
    }
}
