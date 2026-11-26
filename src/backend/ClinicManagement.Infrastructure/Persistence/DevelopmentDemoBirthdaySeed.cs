using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Persistence;

/// <summary>
/// Older builds of the 24/7 walk-in demo seed set every patient's birthday to the seed date itself
/// (only the year differed). Moves those birthdays to an earlier day of the same age-year so the
/// age shown on the seed date is unchanged. Patients whose birthday no longer matches that pattern
/// are left alone, so running it again changes nothing.
/// </summary>
public static class DevelopmentDemoBirthdaySeed
{
    public const string WalkInPhonePrefix = "089900";
    public const int FirstWalkInAge = 25;

    public static DateOnly WalkInBirthDate(DateOnly seedDate, int index) =>
        seedDate.AddYears(-(FirstWalkInAge + index)).AddDays(-OffsetDays(index));

    // 37 and 311 are coprime, so the first 311 indexes get distinct offsets inside one age-year.
    private static int OffsetDays(int index) => 17 + index * 37 % 311;

    public static async Task<int> EnsureAsync(AppDbContext db, CancellationToken ct = default)
    {
        var candidates = (await db.Patients
                .Where(p => p.UserId == null && p.DateOfBirth != null && p.PhoneNumber != null && p.PhoneNumber.StartsWith(WalkInPhonePrefix))
                .ToListAsync(ct))
            .Select(p => (Patient: p, Index: WalkInIndex(p.PhoneNumber!)))
            .Where(x => x.Index >= 0)
            .Select(x => (x.Patient, x.Index, SeedDate: x.Patient.DateOfBirth!.Value.AddYears(FirstWalkInAge + x.Index)))
            .ToList();

        var changes = 0;
        foreach (var group in candidates.GroupBy(x => x.SeedDate).Where(g => g.Count() > 1))
        {
            foreach (var (patient, index, seedDate) in group)
            {
                patient.DateOfBirth = WalkInBirthDate(seedDate, index);
                changes++;
            }
        }
        if (changes > 0) await db.SaveChangesAsync(ct);
        return changes;
    }

    private static int WalkInIndex(string phone) =>
        phone.Length == WalkInPhonePrefix.Length + 4 && int.TryParse(phone[WalkInPhonePrefix.Length..], out var number) && number > 0
            ? number - 1
            : -1;
}
