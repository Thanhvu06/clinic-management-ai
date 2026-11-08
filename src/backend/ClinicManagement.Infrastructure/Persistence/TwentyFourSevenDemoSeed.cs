using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Persistence;

internal static class TwentyFourSevenDemoSeed
{
    internal static async Task EnsurePatientsAsync(AppDbContext db, IDateTimeProvider clock, CancellationToken ct)
    {
        string[] names = ["Võ Minh Anh", "Lê Ngọc Mai", "Trần Thu Thảo", "Phạm Thanh Hà", "Nguyễn Bảo Trâm", "Đặng Thùy Linh", "Bùi Kim Ngân", "Đỗ Quỳnh Hoa",
            "Hoàng Gia Huy", "Phan Đức Minh", "Huỳnh Quốc An", "Vũ Thành Long", "Trịnh Tuấn Kiệt", "Cao Minh Quân", "Hồ Thanh Sơn", "Mai Nhật Nam"];
        for (var index = 0; index < 32; index++)
        {
            var mrn = $"BN-{clock.VietnamToday.Year}-{3001 + index:D6}";
            var phone = $"089900{index + 1:D4}";
            if (await db.Patients.AnyAsync(p => p.MedicalRecordNumber == mrn || p.PhoneNumber == phone, ct)) continue;
            db.Patients.Add(new Patient
            {
                FullName = names[index % names.Length], MedicalRecordNumber = mrn,
                PhoneNumber = phone, Gender = index % 16 < 8 ? Gender.Female : Gender.Male,
                DateOfBirth = clock.VietnamToday.AddYears(-25 - index)
            });
        }
        await db.SaveChangesAsync(ct);
    }

    internal static async Task SeedAsync(AppDbContext db, IDateTimeProvider clock, Guid actor)
    {
        await TwentyFourSevenDemoTopUp.ApplyAsync(db, clock,
            new DemoTopUpOptions { MinUpcomingConfirmed = 24, MinPendingConfirmations = 0 },
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, initialSeed: true, seedActor: actor);
    }
}
