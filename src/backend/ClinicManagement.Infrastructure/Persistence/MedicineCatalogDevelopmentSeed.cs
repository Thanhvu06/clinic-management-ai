using ClinicManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Persistence;

internal static class MedicineCatalogDevelopmentSeed
{
    private record SeedMedicine(string Code, string Name, string ActiveIngredient, string Strength, string DosageForm, string Unit, string CategoryName, bool Rx, decimal Price);
    public static async Task SeedAsync(AppDbContext db)
    {
        string[] categoryNames = ["Giảm đau – hạ sốt", "Kháng sinh", "Tiêu hóa", "Tim mạch – huyết áp – mỡ máu", "Đái tháo đường", "Dị ứng", "Hô hấp", "Kháng viêm", "Vitamin – khoáng chất", "Thần kinh", "Da liễu"];
        string[] descriptions = ["Thuốc dùng để giảm đau và hạ sốt.", "Thuốc kháng sinh dùng trong điều trị nhiễm khuẩn phù hợp.", "Thuốc hỗ trợ điều trị các vấn đề tiêu hóa.", "Thuốc dùng trong điều trị các bệnh tim mạch phù hợp.", "Thuốc dùng trong điều trị đái tháo đường.", "Thuốc dùng để giảm các triệu chứng dị ứng.", "Thuốc hỗ trợ điều trị các vấn đề hô hấp.", "Thuốc dùng để giảm tình trạng viêm.", "Sản phẩm bổ sung vitamin hoặc khoáng chất.", "Thuốc dùng trong điều trị các bệnh thần kinh phù hợp.", "Thuốc dùng trong điều trị các bệnh da liễu phù hợp."];
        var categories = await db.MedicineCategories.ToListAsync();
        for (var i = 0; i < categoryNames.Length; i++)
        {
            if (categories.Any(c => c.Name == categoryNames[i])) continue;
            var category = new MedicineCategory { Name = categoryNames[i], SortOrder = i + 1 };
            categories.Add(category);
            db.MedicineCategories.Add(category);
        }
        await db.SaveChangesAsync();
        SeedMedicine[] definitions = [
            new SeedMedicine("MED01", "Paracetamol 500mg", "Paracetamol", "500 mg", "Viên nén", "Viên", "Giảm đau – hạ sốt", false, 2000m),
            new SeedMedicine("MED02", "Amoxicillin 500mg", "Amoxicillin", "500 mg", "Viên nang", "Viên", "Kháng sinh", true, 5000m),
            new SeedMedicine("MED03", "Ibuprofen 400mg", "Ibuprofen", "400 mg", "Viên nén bao phim", "Viên", "Giảm đau – hạ sốt", false, 3500m),
            new SeedMedicine("MED04", "Omeprazole 20mg", "Omeprazole", "20 mg", "Viên nang", "Viên", "Tiêu hóa", true, 4500m),
            new SeedMedicine("MED05", "Cefixime 200mg", "Cefixime", "200 mg", "Viên nang", "Viên", "Kháng sinh", true, 12000m),
            new SeedMedicine("MED06", "Loratadine 10mg", "Loratadine", "10 mg", "Viên nén", "Viên", "Dị ứng", false, 3000m),
            new SeedMedicine("MED07", "Metformin 500mg", "Metformin hydrochloride", "500 mg", "Viên nén", "Viên", "Đái tháo đường", true, 2500m),
            new SeedMedicine("MED08", "Amlodipine 5mg", "Amlodipine", "5 mg", "Viên nén", "Viên", "Tim mạch – huyết áp – mỡ máu", true, 4000m),
            new SeedMedicine("MED09", "Vitamin C 500mg", "Acid ascorbic", "500 mg", "Viên nén", "Viên", "Vitamin – khoáng chất", false, 1500m),
            new SeedMedicine("MED10", "Salbutamol 2mg", "Salbutamol", "2 mg", "Viên nén", "Viên", "Hô hấp", true, 2000m),
            new SeedMedicine("MED11", "Berberin 100mg", "Berberin clorid", "100 mg", "Viên nén", "Viên", "Tiêu hóa", false, 1000m),
            new SeedMedicine("MED12", "Phosphalugel 20g", "Nhôm phosphat dạng gel", "20 g", "Hỗn dịch uống", "Gói", "Tiêu hóa", false, 8000m),
            new SeedMedicine("MED13", "Cetirizine 10mg", "Cetirizine dihydrochloride", "10 mg", "Viên nén bao phim", "Viên", "Dị ứng", false, 2500m),
            new SeedMedicine("MED14", "Azithromycin 500mg", "Azithromycin", "500 mg", "Viên nén bao phim", "Viên", "Kháng sinh", true, 15000m),
            new SeedMedicine("MED15", "Ciprofloxacin 500mg", "Ciprofloxacin", "500 mg", "Viên nén bao phim", "Viên", "Kháng sinh", true, 6000m),
            new SeedMedicine("MED16", "Losartan 50mg", "Losartan kali", "50 mg", "Viên nén bao phim", "Viên", "Tim mạch – huyết áp – mỡ máu", true, 5000m),
            new SeedMedicine("MED17", "Atorvastatin 20mg", "Atorvastatin", "20 mg", "Viên nén bao phim", "Viên", "Tim mạch – huyết áp – mỡ máu", true, 6500m),
            new SeedMedicine("MED18", "Gliclazide 30mg MR", "Gliclazide", "30 mg", "Viên phóng thích kéo dài", "Viên", "Đái tháo đường", true, 4000m),
            new SeedMedicine("MED19", "Domperidone 10mg", "Domperidone", "10 mg", "Viên nén", "Viên", "Tiêu hóa", true, 2000m),
            new SeedMedicine("MED20", "Oresol 245", "Muối bù nước và điện giải", "4,1 g", "Bột pha uống", "Gói", "Tiêu hóa", false, 3000m),
            new SeedMedicine("MED21", "Diosmectite 3g", "Diosmectite", "3 g", "Bột pha hỗn dịch uống", "Gói", "Tiêu hóa", false, 4500m),
            new SeedMedicine("MED22", "Acetylcysteine 200mg", "Acetylcysteine", "200 mg", "Thuốc cốm", "Gói", "Hô hấp", false, 2500m),
            new SeedMedicine("MED23", "Ambroxol 30mg", "Ambroxol hydrochloride", "30 mg", "Viên nén", "Viên", "Hô hấp", false, 1500m),
            new SeedMedicine("MED24", "Montelukast 10mg", "Montelukast", "10 mg", "Viên nén bao phim", "Viên", "Hô hấp", true, 8000m),
            new SeedMedicine("MED25", "Prednisolone 5mg", "Prednisolone", "5 mg", "Viên nén", "Viên", "Kháng viêm", true, 1000m),
            new SeedMedicine("MED26", "Vitamin 3B", "Vitamin B1, B6, B12", "100/200/0,2 mg", "Viên nén bao phim", "Viên", "Vitamin – khoáng chất", false, 2000m),
            new SeedMedicine("MED27", "Calci + Vitamin D3", "Calci carbonat, Cholecalciferol", "1250 mg/200 IU", "Viên nén", "Viên", "Vitamin – khoáng chất", false, 3500m),
            new SeedMedicine("MED28", "Gabapentin 300mg", "Gabapentin", "300 mg", "Viên nang", "Viên", "Thần kinh", true, 7000m),
            new SeedMedicine("MED29", "Siro ho thảo dược 100ml", "Cao lá thường xuân", "100 ml", "Siro", "Chai", "Hô hấp", false, 85000m),
            new SeedMedicine("MED30", "Clotrimazole 1% 20g", "Clotrimazole", "1%", "Kem bôi da", "Tuýp", "Da liễu", false, 25000m),
        ];
        var existing = await db.Medicines.ToDictionaryAsync(m => m.Code);
        foreach (var def in definitions)
        {
            if (!existing.TryGetValue(def.Code, out var medicine))
            {
                medicine = new Medicine {
                    Code = def.Code, Name = def.Name, Unit = def.Unit, UnitPrice = def.Price,
                    StockQuantity = def.Code == "MED10" ? 25 : 200,
                    ReorderLevel = def.Code == "MED10" ? 50 : 40,
                    IsPrescriptionRequired = def.Rx
                };
                db.Medicines.Add(medicine);
            }
            // Apply prescription classification on first categorization; preserve later pharmacist edits.
            if (medicine.CategoryId == null)
                medicine.IsPrescriptionRequired = def.Rx;
            medicine.ActiveIngredient ??= def.ActiveIngredient;
            medicine.Strength ??= def.Strength;
            medicine.DosageForm ??= def.DosageForm;
            medicine.CategoryId ??= categories.Single(c => c.Name == def.CategoryName).Id;
            medicine.Description ??= descriptions[Array.IndexOf(categoryNames, def.CategoryName)];
            medicine.StorageInstructions ??= "Bảo quản nơi khô, dưới 30°C, tránh ánh sáng.";
        }
        await db.SaveChangesAsync();
    }
}
