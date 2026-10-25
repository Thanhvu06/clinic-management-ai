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
        "SP03" => patient.Gender == Gender.Female && patient.DateOfBirth is { } birth && birth <= date.AddYears(-16),
        _ => patient.DateOfBirth is { } dob && dob <= date.AddYears(-16)
    };

    internal static async Task SeedAsync(AppDbContext db, IDateTimeProvider clock, IMrnGenerator? generator, UserManager<ApplicationUser> users, string password)
    {
        (string Name, Gender Gender, string Phone)[] identities =
        [
            ("Võ Gia Bảo", Gender.Male, "0937184265"), ("Lê Khánh Vy", Gender.Female, "0864529173"),
            ("Đặng Đức Huy", Gender.Male, "0973625814"), ("Phạm Thảo Nhi", Gender.Female, "0918753642"),
            ("Bùi Quốc Hưng", Gender.Male, "0386914275"), ("Nguyễn Bảo Ngân", Gender.Female, "0792546318"),
            ("Đỗ Anh Kiệt", Gender.Male, "0856179342"), ("Huỳnh Ngọc Diệp", Gender.Female, "0328497165"),
            ("Trịnh Hải Đăng", Gender.Male, "0945261837"), ("Dương Hà My", Gender.Female, "0703819654"),
            ("Mai Tuấn Tú", Gender.Male, "0967542183"), ("Hồ Quỳnh Anh", Gender.Female, "0831926475"),
            ("Phan Thành Đạt", Gender.Male, "0375861924"), ("Vũ Minh Thư", Gender.Female, "0887294531"),
            ("Cao Nhật Nam", Gender.Male, "0904671825"), ("Tạ Ngọc Hân", Gender.Female, "0819352746")
        ];
        for (var i = 0; i < identities.Length; i++)
        {
            var (name, gender, phone) = identities[i];
            if (await db.Patients.AnyAsync(p => p.PhoneNumber == phone)) continue;
            var email = $"patient.child.{i + 1:D2}@cliniccare.local";
            var user = new ApplicationUser { Email = email, UserName = email, FullName = name, PhoneNumber = phone,
                IsActive = true, CreatedAt = RealisticDevelopmentSeed.At(clock, clock.VietnamToday.AddDays(-90), 8) };
            if (!(await users.CreateAsync(user, password)).Succeeded)
                throw new InvalidOperationException("Không tạo được tài khoản bệnh nhi.");
            await users.AddToRoleAsync(user, "Patient");
            db.Patients.Add(new Patient
            {
                UserId = user.Id, FullName = name, Email = email,
                PhoneNumber = phone, Gender = gender,
                DateOfBirth = clock.VietnamToday.AddYears(-3 - i % 12).AddDays(-60),
                MedicalRecordNumber = generator != null ? await generator.GenerateNextMrnAsync() : $"BN-{clock.VietnamToday.Year}-{2001 + i:D6}"
            });
            await db.SaveChangesAsync();
        }
    }
}
