using ClinicManagement.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Mpi.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Persistence;

internal static class DemoPatientDevelopmentSeed
{
    internal static bool Allows(Patient patient, string specialty, DateOnly date) => specialty switch
    {
        "SP02" => patient.DateOfBirth is { } dob && dob <= date && dob > date.AddYears(-16),
        "SP03" => patient.Gender == Gender.Female,
        _ => true
    };

    internal static async Task SeedAsync(AppDbContext db, IDateTimeProvider clock, IMrnGenerator? generator, UserManager<ApplicationUser> users, string password)
    {
        for (var i = 0; i < 16; i++)
        {
            var phone = $"098310{i:D4}";
            if (await db.Patients.AnyAsync(p => p.PhoneNumber == phone)) continue;
            var name = $"{(i % 2 == 0 ? "Nguyễn Minh" : "Trần Ngọc")} {new[] { "An", "Bình", "Châu", "Dũng", "Giang", "Hà", "Khánh", "Linh" }[i / 2]}";
            var email = $"patient.child.{i + 1:D2}@cliniccare.local";
            var user = new ApplicationUser { Email = email, UserName = email, FullName = name, PhoneNumber = phone,
                IsActive = true, CreatedAt = RealisticDevelopmentSeed.At(clock, clock.VietnamToday.AddDays(-90), 8) };
            if (!(await users.CreateAsync(user, password)).Succeeded)
                throw new InvalidOperationException("Không tạo được tài khoản bệnh nhân demo trẻ em.");
            await users.AddToRoleAsync(user, "Patient");
            db.Patients.Add(new Patient
            {
                UserId = user.Id, FullName = name, Email = email,
                PhoneNumber = phone, Gender = i % 2 == 0 ? Gender.Male : Gender.Female,
                DateOfBirth = clock.VietnamToday.AddYears(-3 - i % 12).AddDays(-60),
                MedicalRecordNumber = generator != null ? await generator.GenerateNextMrnAsync() : $"BN-{clock.VietnamToday.Year}-{2001 + i:D6}"
            });
            await db.SaveChangesAsync();
        }
    }
}
