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
            var monograph = Monograph(def.Code);
            medicine.Indications ??= monograph.Indications;
            medicine.DosageAndAdministration ??= medicine.IsPrescriptionRequired ? PrescriptionDosage :
                (def.Code == "MED01" && medicine.Strength == "500 mg" && medicine.ActiveIngredient == "Paracetamol"
                    ? "Người lớn: uống 1–2 viên/lần khi đau hoặc sốt, cách ít nhất 4–6 giờ; tối đa 8 viên (4 g) trong 24 giờ. Không dùng cùng thuốc khác chứa paracetamol. Trẻ em: hỏi ý kiến bác sĩ/dược sĩ."
                    : PrescriptionDosage + " Trẻ em: hỏi ý kiến bác sĩ/dược sĩ.");
            medicine.Contraindications ??= monograph.Contraindications;
            medicine.SideEffects ??= monograph.SideEffects;
            medicine.Precautions ??= monograph.Precautions;
            medicine.OverdoseManagement ??= monograph.OverdoseManagement;
        }
        await db.SaveChangesAsync();
    }

    private const string PrescriptionDosage = "Dùng theo đơn và chỉ định của bác sĩ. Không tự ý tăng/giảm liều hoặc ngừng thuốc.";
    private const string AllergyContraindication = "Không dùng khi dị ứng hoạt chất hoặc tá dược. ";
    private const string OverdoseAdvice = "Nếu dùng quá liều, liên hệ ngay cơ sở y tế và mang theo bao bì thuốc. Không tự gây nôn hoặc tự dùng thuốc giải độc.";
    private record ReferenceMonograph(string Indications, string Contraindications, string SideEffects, string Precautions, string OverdoseManagement = OverdoseAdvice);

    // Reference summaries for the development catalog, not approved product labeling.
    // Sources: NHS medicines; manufacturer PILs on medicines.org.uk; EMA herbal/safety monographs.
    // Product-specific labeling and a pharmacist's review take precedence.
    private static ReferenceMonograph Monograph(string code) => code switch
    {
        "MED01" => new("Giảm đau nhẹ đến vừa như đau đầu, đau răng. Hạ sốt.",
            AllergyContraindication + "Không tự dùng khi có bệnh gan nặng.",
            "Hiếm gặp phát ban hoặc phản ứng dị ứng. Quá liều có thể gây tổn thương gan nghiêm trọng.",
            "Hỏi chuyên môn nếu bệnh gan, thận, uống nhiều rượu hoặc cân nặng thấp. Không dùng chồng nhiều sản phẩm chứa paracetamol; thai kỳ/cho con bú cần dùng đúng hướng dẫn.",
            "Đến cơ sở y tế ngay cả khi chưa có triệu chứng vì tổn thương gan có thể xuất hiện muộn. Mang theo vỏ thuốc và thông tin lượng đã dùng; không tự gây nôn."),
        "MED02" => new("Điều trị nhiễm khuẩn do vi khuẩn nhạy cảm theo chẩn đoán. Không điều trị cảm cúm do virus.",
            AllergyContraindication + "Không dùng nếu từng phản ứng dị ứng nặng với penicillin hoặc kháng sinh beta-lactam.",
            "Có thể buồn nôn, tiêu chảy, phát ban. Cấp cứu nếu khó thở, phù mặt; báo bác sĩ nếu tiêu chảy nặng hoặc có máu.",
            "Báo bác sĩ về bệnh thận, tiền sử dị ứng và thuốc đang dùng. Thai kỳ/cho con bú cần hỏi bác sĩ; dùng đủ đợt được kê."),
        "MED03" => new("Giảm đau cơ, khớp, đau răng hoặc đau bụng kinh. Giảm viêm và hạ sốt ngắn hạn.",
            AllergyContraindication + "Không dùng khi dị ứng aspirin/NSAID, loét hoặc xuất huyết tiêu hóa đang tiến triển; tránh trong ba tháng cuối thai kỳ.",
            "Có thể đau bụng, khó tiêu, buồn nôn. Nguy cơ loét, xuất huyết tiêu hóa hoặc tổn thương thận.",
            "Dùng thời gian ngắn nhất theo hướng dẫn; thường uống cùng hoặc sau ăn. Hỏi bác sĩ nếu bệnh tim, thận, hen, có thai/cho con bú; không tự phối hợp NSAID khác."),
        "MED04" => new("Giảm tiết acid trong trào ngược dạ dày – thực quản. Điều trị loét hoặc phối hợp diệt H. pylori theo chỉ định.",
            AllergyContraindication + "Không phối hợp nelfinavir.",
            "Có thể đau đầu, đau bụng, buồn nôn hoặc tiêu chảy. Dùng kéo dài có thể giảm magnesi hoặc vitamin B12.",
            "Báo bác sĩ khi sụt cân, nuốt khó hoặc đi ngoài phân đen. Không tự dùng kéo dài; thai kỳ/cho con bú cần tư vấn."),
        "MED05" => new("Điều trị một số nhiễm khuẩn hô hấp hoặc tiết niệu do vi khuẩn nhạy cảm. Chỉ dùng khi bác sĩ chỉ định.",
            AllergyContraindication + "Không dùng khi đã dị ứng cephalosporin; tiền sử phản ứng nặng với beta-lactam cần bác sĩ đánh giá.",
            "Có thể tiêu chảy, đau bụng, buồn nôn. Hiếm gặp dị ứng nặng hoặc viêm đại tràng do kháng sinh.",
            "Báo bác sĩ nếu suy thận hoặc dùng thuốc chống đông. Thai kỳ/cho con bú cần tư vấn; tiêu chảy nặng phải đi khám."),
        "MED06" => new("Giảm hắt hơi, sổ mũi do viêm mũi dị ứng. Giảm ngứa, mề đay.",
            AllergyContraindication + "Không dùng chế phẩm có tá dược mà người bệnh không dung nạp theo tờ hướng dẫn.",
            "Có thể đau đầu, mệt mỏi. Một số người buồn ngủ hoặc khô miệng.",
            "Bệnh gan nặng cần bác sĩ chỉnh cách dùng. Không lái xe nếu buồn ngủ; thai kỳ/cho con bú hỏi bác sĩ."),
        "MED07" => new("Kiểm soát đường huyết trong đái tháo đường típ 2. Kết hợp chế độ ăn và vận động theo kế hoạch điều trị.",
            AllergyContraindication + "Không dùng khi suy thận nặng, nhiễm toan chuyển hóa hoặc nhiễm toan ceton.",
            "Thường gặp buồn nôn, tiêu chảy, đau bụng. Dùng lâu có thể giảm vitamin B12; hiếm gặp nhiễm toan lactic.",
            "Báo bác sĩ khi mất nước, nhiễm trùng nặng hoặc trước chụp cản quang. Theo dõi thận; thai kỳ/cho con bú cần tư vấn.",
            "Quá liều có nguy cơ nhiễm toan lactic, cần cấp cứu ngay. Mang theo vỏ thuốc; không tự xử trí tại nhà."),
        "MED08" => new("Điều trị tăng huyết áp. Dự phòng một số dạng đau thắt ngực theo đơn.",
            AllergyContraindication + "Không dùng khi tụt huyết áp nặng hoặc sốc.",
            "Có thể phù mắt cá chân, đỏ bừng, đau đầu. Có thể chóng mặt hoặc hồi hộp.",
            "Thận trọng khi bệnh gan hoặc có bệnh tim nặng. Tránh lái xe nếu chóng mặt; thai kỳ/cho con bú hỏi bác sĩ."),
        "MED09" => new("Bổ sung vitamin C khi thiếu hụt. Hỗ trợ đáp ứng nhu cầu khi chế độ ăn không đủ.",
            AllergyContraindication + "Không tự dùng liều cao khi tăng oxalat niệu hoặc có sỏi thận oxalat.",
            "Có thể đau bụng, tiêu chảy, buồn nôn. Liều cao kéo dài có thể tăng nguy cơ sỏi thận.",
            "Hỏi bác sĩ nếu bệnh thận hoặc rối loạn tích lũy sắt. Thai kỳ/cho con bú không tự dùng liều cao."),
        "MED10" => new("Giãn phế quản trong bệnh đường thở có co thắt theo chỉ định. Không tự dùng thay thuốc hít cấp cứu.",
            AllergyContraindication + "Không dùng để ngăn chuyển dạ hoặc xử trí dọa sảy thai.",
            "Có thể run tay, hồi hộp, nhịp tim nhanh. Có thể giảm kali máu hoặc chuột rút.",
            "Báo bác sĩ nếu bệnh tim, cường giáp hoặc đái tháo đường. Thai kỳ/cho con bú cần đánh giá; khó thở tăng phải đi khám ngay."),
        "MED11" => new("Hỗ trợ điều trị một số trường hợp tiêu chảy theo hướng dẫn của sản phẩm. Bù nước và điện giải vẫn là ưu tiên.",
            AllergyContraindication + "Không tự dùng cho phụ nữ có thai hoặc trẻ sơ sinh.",
            "Có thể rối loạn tiêu hóa hoặc táo bón. Có thể phát ban, phản ứng dị ứng.",
            "Tiêu chảy có máu, sốt cao hoặc mất nước phải đi khám. Người cho con bú hoặc có bệnh nền cần hỏi bác sĩ trước dùng."),
        "MED12" => new("Giảm triệu chứng do tăng acid như nóng rát, ợ chua. Hỗ trợ giảm đau vùng thượng vị liên quan acid.",
            AllergyContraindication + "Không dùng khi suy thận nặng.",
            "Có thể táo bón. Dùng nhiều hoặc kéo dài có thể ảnh hưởng cân bằng khoáng chất, nhất là khi suy thận.",
            "Hỏi dược sĩ về khoảng cách với thuốc uống khác. Thai kỳ/cho con bú, táo bón kéo dài hoặc đau bụng dai dẳng cần tư vấn."),
        "MED13" => new("Giảm triệu chứng viêm mũi dị ứng. Giảm ngứa và mề đay.",
            AllergyContraindication + "Không dùng khi suy thận giai đoạn cuối hoặc từng dị ứng hydroxyzine/dẫn chất piperazine.",
            "Có thể buồn ngủ, mệt, khô miệng. Có thể đau đầu hoặc chóng mặt.",
            "Bệnh thận cần điều chỉnh cách dùng. Tránh rượu và không lái xe nếu buồn ngủ; thai kỳ/cho con bú hỏi bác sĩ."),
        "MED14" => new("Điều trị một số nhiễm khuẩn hô hấp, da hoặc nhiễm khuẩn khác theo chẩn đoán. Không điều trị cảm cúm do virus.",
            AllergyContraindication + "Không dùng nếu dị ứng kháng sinh macrolid.",
            "Có thể tiêu chảy, buồn nôn, đau bụng. Hiếm gặp tổn thương gan hoặc rối loạn nhịp tim.",
            "Báo bác sĩ nếu có bệnh gan, kéo dài QT hoặc thuốc dùng kèm. Thai kỳ/cho con bú cần tư vấn; không tự ngừng đợt điều trị."),
        "MED15" => new("Điều trị một số nhiễm khuẩn do vi khuẩn nhạy cảm khi bác sĩ đánh giá phù hợp. Không tự dùng cho nhiễm virus.",
            AllergyContraindication + "Không phối hợp tizanidine; không dùng khi dị ứng kháng sinh quinolon.",
            "Có thể buồn nôn, tiêu chảy. Có nguy cơ viêm/đứt gân, bệnh thần kinh ngoại biên hoặc ảnh hưởng thần kinh trung ương.",
            "Báo bác sĩ ngay khi đau gân, tê bì hoặc thay đổi tâm thần. Bệnh thận, thai kỳ/cho con bú cần đánh giá; hỏi dược sĩ về khoảng cách với calci và thuốc kháng acid."),
        "MED16" => new("Điều trị tăng huyết áp. Có thể bảo vệ thận trong một số trường hợp đái tháo đường theo chỉ định.",
            AllergyContraindication + "Không dùng trong thai kỳ, đặc biệt từ ba tháng giữa; không dùng khi suy gan nặng.",
            "Có thể chóng mặt, hạ huyết áp. Có thể tăng kali máu hoặc thay đổi chức năng thận.",
            "Theo dõi huyết áp, kali và chức năng thận. Không tự dùng muối bổ sung kali; cho con bú cần hỏi bác sĩ."),
        "MED17" => new("Giảm cholesterol khi chế độ ăn và vận động chưa đủ. Giảm nguy cơ biến cố tim mạch ở người phù hợp.",
            AllergyContraindication + "Không dùng khi bệnh gan đang hoạt động, mang thai hoặc cho con bú.",
            "Có thể đau cơ, đau khớp hoặc rối loạn tiêu hóa. Hiếm gặp tổn thương cơ nặng hoặc tăng men gan.",
            "Báo bác sĩ nếu đau cơ bất thường, yếu cơ hoặc nước tiểu sẫm. Kiểm tra tương tác thuốc và hạn chế rượu."),
        "MED18" => new("Kiểm soát đường huyết trong đái tháo đường típ 2. Dùng cùng chế độ ăn phù hợp theo kế hoạch điều trị.",
            AllergyContraindication + "Không dùng cho đái tháo đường típ 1, nhiễm toan ceton hoặc suy gan/thận nặng.",
            "Có thể hạ đường huyết với đói, vã mồ hôi, run tay. Có thể rối loạn tiêu hóa hoặc phát ban.",
            "Ăn đúng bữa, theo dõi đường huyết; không nhai/nghiền viên MR. Thai kỳ/cho con bú cần hỏi bác sĩ; không lái xe khi có dấu hiệu hạ đường huyết.",
            "Quá liều có thể hạ đường huyết kéo dài, cần trợ giúp y tế ngay. Không cho ăn uống nếu bất tỉnh; mang theo thuốc khi đi cấp cứu."),
        "MED19" => new("Giảm buồn nôn và nôn khi được bác sĩ chỉ định. Chỉ dùng trong thời gian được kê.",
            AllergyContraindication + "Không dùng khi xuất huyết/tắc/thủng tiêu hóa, kéo dài QT hoặc bệnh gan vừa đến nặng.",
            "Có thể khô miệng. Hiếm gặp rối loạn nhịp tim, ngất hoặc tăng prolactin.",
            "Báo bác sĩ về bệnh tim và toàn bộ thuốc đang dùng. Thai kỳ/cho con bú cần tư vấn; hồi hộp hoặc ngất phải được đánh giá ngay."),
        "MED20" => new("Bù nước và điện giải khi tiêu chảy. Hỗ trợ xử trí mất nước nhẹ đến vừa theo hướng dẫn chuyên môn.",
            AllergyContraindication + "Mất nước nặng, sốc hoặc tắc ruột cần điều trị tại cơ sở y tế, không chỉ dùng dung dịch uống.",
            "Có thể buồn nôn nếu uống nhanh. Pha sai hoặc dùng không phù hợp có thể rối loạn điện giải.",
            "Pha đúng lượng nước ghi trên gói, không chia bột hoặc thêm đường/muối. Bệnh thận, tim hoặc không uống được cần hỏi bác sĩ; thai kỳ/cho con bú dùng theo hướng dẫn."),
        "MED21" => new("Điều trị triệu chứng tiêu chảy cấp theo hướng dẫn sản phẩm. Luôn kết hợp bù nước và điện giải.",
            AllergyContraindication + "Không tự dùng khi nghi tắc ruột hoặc có đau bụng chưa rõ nguyên nhân.",
            "Có thể táo bón. Hiếm gặp phát ban hoặc phản ứng dị ứng.",
            "Uống cách thuốc khác theo tờ hướng dẫn vì có thể giảm hấp thu. Không tự dùng cho trẻ dưới hai tuổi; không khuyến nghị dùng trong thai kỳ/cho con bú. Tiêu chảy có máu hoặc kéo dài phải khám."),
        "MED22" => new("Làm loãng đờm quánh trong bệnh hô hấp. Hỗ trợ khạc đờm dễ hơn khi có chỉ định phù hợp.",
            AllergyContraindication + "Không dùng thuốc tiêu đờm cho trẻ dưới hai tuổi.",
            "Có thể buồn nôn, đau bụng, tiêu chảy. Có thể co thắt phế quản hoặc dị ứng.",
            "Thận trọng nếu hen, tiền sử loét hoặc không khạc đờm được. Không tự phối hợp thuốc ức chế ho; thai kỳ/cho con bú hỏi bác sĩ."),
        "MED23" => new("Làm loãng đờm trong bệnh hô hấp có tăng tiết đờm. Hỗ trợ tống đờm khi ho có đờm.",
            AllergyContraindication + "Không dùng cho người có tiền sử phản ứng da nặng với ambroxol.",
            "Có thể buồn nôn hoặc rối loạn tiêu hóa. Hiếm gặp phản ứng dị ứng và phản ứng da nghiêm trọng.",
            "Ngừng dùng và đi khám khi xuất hiện tổn thương da/niêm mạc. Bệnh gan, thận, thai kỳ/cho con bú hoặc trẻ em cần tư vấn."),
        "MED24" => new("Điều trị kiểm soát và dự phòng triệu chứng hen theo đơn. Có thể dùng cho viêm mũi dị ứng khi bác sĩ đánh giá phù hợp.",
            AllergyContraindication + "Không dùng thay thuốc cắt cơn hen cấp.",
            "Có thể đau đầu hoặc đau bụng. Có thể rối loạn giấc ngủ, hành vi hoặc tâm trạng.",
            "Báo bác sĩ ngay khi có thay đổi tâm thần, kể cả ý nghĩ tự hại. Không tự ngừng thuốc kiểm soát hen khác; thai kỳ/cho con bú cần tư vấn."),
        "MED25" => new("Giảm viêm và ức chế miễn dịch trong bệnh phù hợp theo chỉ định. Không tự dùng cho triệu chứng chưa được chẩn đoán.",
            AllergyContraindication + "Không dùng khi nhiễm trùng toàn thân chưa được điều trị phù hợp; tránh vắc xin sống khi dùng liều ức chế miễn dịch.",
            "Có thể tăng đường huyết, khó ngủ, tăng thèm ăn. Dùng kéo dài có thể tăng nhiễm trùng, loãng xương hoặc suy tuyến thượng thận.",
            "Không ngừng đột ngột sau đợt dùng kéo dài; tuân thủ kế hoạch giảm liều. Báo bác sĩ về nhiễm trùng, loét, đái tháo đường, thai kỳ/cho con bú."),
        "MED26" => new("Bổ sung vitamin nhóm B khi có thiếu hụt được xác định. Không thay thế điều trị nguyên nhân của triệu chứng thần kinh.",
            AllergyContraindication + "Không dùng nếu từng dị ứng các vitamin thành phần.",
            "Có thể buồn nôn hoặc dị ứng. Dùng vitamin B6 liều cao kéo dài có thể gây tổn thương thần kinh cảm giác.",
            "Không tự dùng kéo dài, nhất là chế phẩm chứa B6 hàm lượng cao. Báo dược sĩ nếu dùng levodopa; thai kỳ/cho con bú cần tư vấn."),
        "MED27" => new("Bổ sung calci và vitamin D khi chế độ ăn thiếu hoặc có nhu cầu được đánh giá. Hỗ trợ điều trị loãng xương theo chỉ định.",
            AllergyContraindication + "Không dùng khi tăng calci máu, tăng calci niệu hoặc thừa vitamin D.",
            "Có thể táo bón, đầy bụng. Dùng quá mức có thể tăng calci máu hoặc sỏi thận.",
            "Bệnh thận, tiền sử sỏi hoặc dùng digoxin cần tư vấn. Không dùng chồng nhiều chế phẩm calci/vitamin D; thai kỳ/cho con bú cần đúng nhu cầu."),
        "MED28" => new("Điều trị đau thần kinh theo chỉ định. Điều trị một số thể động kinh theo kế hoạch của bác sĩ.",
            AllergyContraindication + "Không dùng nếu từng phản ứng quá mẫn với gabapentin.",
            "Có thể buồn ngủ, chóng mặt, phù ngoại biên. Có thể ức chế hô hấp, nhất là khi phối hợp thuốc an thần hoặc opioid.",
            "Bệnh thận cần bác sĩ điều chỉnh; không tự ngừng đột ngột. Không lái xe khi buồn ngủ; thai kỳ/cho con bú và thay đổi tâm trạng cần báo bác sĩ."),
        "MED29" => new("Hỗ trợ long đờm khi ho có đờm theo hướng dẫn chế phẩm thường xuân. Không thay thế khám bệnh khi có triệu chứng nặng.",
            AllergyContraindication + "Không dùng cho trẻ dưới hai tuổi hoặc người dị ứng họ thường xuân.",
            "Có thể buồn nôn, nôn hoặc tiêu chảy. Có thể phát ban hoặc khó thở do dị ứng.",
            "Liều phụ thuộc loại cao và nồng độ của sản phẩm; không suy từ thể tích chai. Thai kỳ/cho con bú cần tư vấn; sốt, khó thở hoặc đờm mủ phải đi khám."),
        "MED30" => new("Điều trị một số nhiễm nấm ngoài da như hắc lào, nấm kẽ chân. Chỉ dùng ngoài da theo hướng dẫn sản phẩm.",
            AllergyContraindication + "Không dùng nếu từng phản ứng dị ứng với thuốc kháng nấm cùng nhóm hoặc tá dược của kem.",
            "Có thể nóng rát, đỏ da, kích ứng tại chỗ. Ngừng dùng khi sưng, phồng rộp hoặc dị ứng.",
            "Tránh mắt, miệng và không nuốt; giữ vùng da sạch, khô. Thai kỳ/cho con bú cần tư vấn; không để trẻ tiếp xúc vùng da đã bôi.",
            "Nếu nuốt nhầm hoặc dùng quá mức gây kích ứng, liên hệ cơ sở y tế và mang theo tuýp thuốc. Không tự gây nôn."),
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown catalog medicine")
    };
}
