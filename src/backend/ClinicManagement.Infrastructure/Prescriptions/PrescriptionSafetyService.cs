using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Mpi.DTOs;
using ClinicManagement.Application.Prescriptions;
using ClinicManagement.Application.Prescriptions.DTOs;
using ClinicManagement.Application.Prescriptions.Interfaces;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Prescriptions;

public class PrescriptionSafetyService(AppDbContext db) : IPrescriptionSafetyService
{
    private sealed record Medicine(long Id, string Name, List<string> Ingredients);

    public async Task<PrescriptionSafetyResultDto> CheckAsync(long patientId, IEnumerable<long> medicineIds)
    {
        var ids = medicineIds.Distinct().Order().ToArray();
        var allergies = await db.PatientAllergies.AsNoTracking().Where(a => a.PatientId == patientId)
            .OrderByDescending(a => a.Severity).ThenBy(a => a.Id).Select(a => new PatientAllergyDto
            {
                Id = a.Id, PatientId = a.PatientId, AllergenType = a.AllergenType, AllergenTypeName = a.AllergenType.ToString(),
                AllergenName = a.AllergenName, Severity = a.Severity, SeverityName = a.Severity.ToString(),
                ReactionDescription = a.ReactionDescription, RecordedAtUtc = a.RecordedAtUtc
            }).ToListAsync();
        var result = new PrescriptionSafetyResultDto { Allergies = allergies };
        if (ids.Length == 0) return result;
        var rows = await db.Medicines.AsNoTracking().Where(m => ids.Contains(m.Id))
            .Select(m => new { m.Id, m.Name, m.ActiveIngredient }).ToListAsync();
        if (rows.Count != ids.Length) throw new BusinessException("INVALID_MEDICINE", "Có thuốc không tồn tại trong danh sách kiểm tra.");
        var medicines = rows.OrderBy(m => m.Id).Select(m => new Medicine(m.Id, m.Name, IngredientNormalizer.SplitIngredients(m.ActiveIngredient))).ToList();
        // Five batched queries at most, independent of the number of medicines.
        var members = await db.DrugClassMembers.AsNoTracking().OrderBy(m => m.Id).ToListAsync();
        var crossReactions = await db.DrugAllergyCrossReactions.AsNoTracking().OrderBy(r => r.Id).ToListAsync();
        var rules = await db.DrugInteractionRules.AsNoTracking().Where(r => r.IsActive).OrderBy(r => r.Id).ToListAsync();
        var drugAllergies = allergies.Where(a => a.AllergenType == AllergenType.Drug)
            .GroupBy(a => IngredientNormalizer.NormalizeKey(a.AllergenName)).ToList();
        var medicineClasses = medicines.ToDictionary(m => m.Id, m => members.Where(member => Has(m, member.IngredientKey)).ToList());
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void Add(string level, string type, IEnumerable<long> affected, string title, string detail, string match)
        {
            var affectedIds = affected.Distinct().Order().ToList();
            if (seen.Add($"{type}|{string.Join(',', affectedIds)}|{match}"))
                result.Warnings.Add(new() { Level = level, Type = type, MedicineIds = affectedIds, Title = title, Detail = detail });
        }

        foreach (var medicine in medicines)
        {
            if (medicine.Ingredients.Count == 0)
                Add("Info", "MissingIngredient", new[] { medicine.Id }, "Không có hoạt chất để kiểm tra", $"{medicine.Name}: chưa có thông tin hoạt chất.", "missing");
            var classes = medicineClasses[medicine.Id];
            foreach (var allergy in drugAllergies.Where(a => a.Key.Length > 0))
            {
                var recorded = string.Join("; ", allergy.Select(a => $"{a.AllergenName} — mức độ {SeverityLabel(a.Severity)}, phản ứng: {a.ReactionDescription ?? "chưa ghi nhận"}").Distinct());
                if (medicine.Ingredients.Any(i => IngredientNormalizer.MatchesKey(i, allergy.Key) ||
                        (i.Length >= 4 && IngredientNormalizer.ContainsKeyAsWord(allergy.Key, i))) ||
                    classes.Any(c => MatchesAllergyClass(allergy.Key, c.ClassKey, c.ClassName)))
                    Add("Danger", "Allergy", new[] { medicine.Id }, "Dị ứng thuốc đã ghi nhận", $"{medicine.Name}: {recorded}.", allergy.Key);
                var allergicClassKeys = members.Where(c => MatchesAllergyClass(allergy.Key, c.ClassKey, c.ClassName))
                    .Select(c => c.ClassKey).Distinct().ToHashSet(StringComparer.Ordinal);
                foreach (var cross in crossReactions.Where(c => allergicClassKeys.Contains(c.AllergenClassKey) && classes.Any(m => m.ClassKey == c.RelatedClassKey)))
                    Add("Warning", "CrossAllergy", new[] { medicine.Id }, "Nguy cơ dị ứng chéo", $"{medicine.Name}: {cross.Note} Dị ứng đã ghi nhận: {recorded}.", $"{cross.AllergenClassKey}|{cross.RelatedClassKey}");
            }
        }

        for (var i = 0; i < medicines.Count; i++)
        for (var j = i + 1; j < medicines.Count; j++)
        {
            var a = medicines[i]; var b = medicines[j]; var affected = new[] { a.Id, b.Id };
            foreach (var rule in rules.Where(r => (Has(a, r.IngredientKeyA) && Has(b, r.IngredientKeyB)) || (Has(a, r.IngredientKeyB) && Has(b, r.IngredientKeyA))))
                Add(rule.Severity is DrugInteractionSeverity.Major or DrugInteractionSeverity.Contraindicated ? "Danger" : rule.Severity == DrugInteractionSeverity.Moderate ? "Warning" : "Info",
                    "Interaction", affected, "Tương tác thuốc", $"{a.Name} + {b.Name}: {rule.Description} {rule.Recommendation}", $"{rule.IngredientKeyA}|{rule.IngredientKeyB}");
            var duplicates = a.Ingredients.SelectMany(left => b.Ingredients.Where(right => SameIngredient(left, right))
                .Select(right => left.Length <= right.Length ? left : right)).Distinct();
            foreach (var ingredient in duplicates)
                Add("Warning", "DuplicateIngredient", affected, "Trùng hoạt chất", $"{a.Name} và {b.Name} cùng chứa {ingredient}.", ingredient);
            foreach (var group in medicineClasses[a.Id].GroupBy(c => c.ClassKey))
            {
                var other = medicineClasses[b.Id].Where(c => c.ClassKey == group.Key).ToList();
                if (group.Any(left => other.Any(right => left.IngredientKey != right.IngredientKey)))
                    Add("Warning", "DuplicateClass", affected, "Trùng nhóm", $"{a.Name} và {b.Name} cùng nhóm {group.First().ClassName}.", group.Key);
            }
        }
        result.Warnings = result.Warnings.OrderBy(w => w.Level == "Danger" ? 0 : w.Level == "Warning" ? 1 : 2)
            .ThenBy(w => w.MedicineIds[0]).ThenBy(w => w.Type, StringComparer.Ordinal).ThenBy(w => w.Detail, StringComparer.Ordinal).ToList();
        return result;
    }

    private static bool MatchesAllergyClass(string allergenKey, string classKey, string className)
    {
        var classNameKey = IngredientNormalizer.NormalizeKey(className);
        return allergenKey == classKey || allergenKey == classNameKey ||
            (classKey.Length >= 4 && IngredientNormalizer.ContainsKeyAsWord(allergenKey, classKey)) ||
            (classNameKey.Length >= 4 && IngredientNormalizer.ContainsKeyAsWord(allergenKey, classNameKey));
    }

    private static bool Has(Medicine medicine, string key) => medicine.Ingredients.Any(i => IngredientNormalizer.MatchesKey(i, key));
    private static bool SameIngredient(string a, string b) => IngredientNormalizer.MatchesKey(a, b) || IngredientNormalizer.MatchesKey(b, a);
    private static string SeverityLabel(AllergySeverity severity) => severity switch
    { AllergySeverity.Mild => "nhẹ", AllergySeverity.Moderate => "vừa", AllergySeverity.Severe => "nặng", AllergySeverity.Fatal => "đe dọa tính mạng", _ => severity.ToString() };
}
