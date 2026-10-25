using ClinicManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Persistence;

internal static class ClinicBrandDevelopmentSeed
{
    private const string Name = "Phòng khám đa khoa ClinicCare AI – Thủ Dầu Một";
    private const string Address = "Số 20 Trần Văn Ơn, Thủ Dầu Một";
    private const string City = "Bình Dương";
    private const string Phone = "0900 000 020";
    private const string Description = "Khám ngoại trú các chuyên khoa, xét nghiệm và nhà thuốc tại chỗ.";

    internal static async Task SeedLocationAsync(AppDbContext db)
    {
        var location = await db.ClinicLocations.SingleOrDefaultAsync(l => l.Code == "CS01");
        if (location == null)
        {
            location = new ClinicLocation { Code = "CS01" };
            db.ClinicLocations.Add(location);
        }
        location.Name = Name;
        location.Address = Address;
        location.City = City;
        location.Phone = Phone;
        location.Description = Description;
        location.OpeningHours = "07:00–17:00 (thứ 2 – thứ 7) · Chủ nhật nghỉ";
        location.ServicesJson = "[\"Khám tổng quát\",\"Xét nghiệm\",\"Siêu âm\",\"X-quang\",\"Nhà thuốc\"]";
        location.IsActive = true;
        var retired = await db.ClinicLocations.Where(l => l.Code == "CS02" || l.Code == "CS03").ToListAsync();
        foreach (var item in retired)
        {
            item.IsActive = false;
        }
        await db.SaveChangesAsync();
    }

    internal static async Task<Facility> SeedFacilityAsync(AppDbContext db)
    {
        var facility = await db.Facilities.SingleOrDefaultAsync(f => f.Code == "CS01" || f.Code == "BV-TW-01");
        if (facility == null)
        {
            facility = new Facility { Code = "CS01", CreatedAtUtc = DateTime.UtcNow, IsActive = true };
            db.Facilities.Add(facility);
        }
        facility.Code = "CS01";
        facility.Name = Name;
        facility.Address = Address;
        facility.City = City;
        facility.Phone = Phone;
        facility.Email = "lienhe@cliniccare.local";
        facility.HospitalLevel = "Phòng khám đa khoa";
        facility.Description = Description;
        await db.SaveChangesAsync();
        return facility;
    }

    internal static async Task SeedPackagesAsync(AppDbContext db)
    {
        var packageDefs = new[]
        {
            new HealthPackage
            {
                Code = "PKG01",
                Name = "Gói khám sức khỏe tổng quát tiêu chuẩn",
                TargetAudience = "Mọi độ tuổi từ 18 trở lên",
                Description = "Kiểm tra các chỉ số huyết học, chức năng gan, thận, đường huyết, mỡ máu, X-quang phổi và siêu âm bụng tổng quát.",
                Price = 1250000m,
                IncludedServicesJson = "[\"Khám nội tổng quát\",\"Công thức máu 18 chỉ số\",\"Đo đường huyết Glucose\",\"Men gan AST/ALT\",\"Chức năng thận Ure/Creatinine\",\"X-quang tim phổi thẳng\",\"Siêu âm bụng tổng quát\"]",
                IsActive = true
            },
            new HealthPackage
            {
                Code = "PKG02",
                Name = "Gói tầm soát tim mạch",
                TargetAudience = "Người trưởng thành, trung niên và có tiền sử tim mạch",
                Description = "Tầm soát bệnh lý mạch vành, huyết áp, rối loạn nhịp tim và xơ vữa động mạch.",
                Price = 2800000m,
                IncludedServicesJson = "[\"Khám chuyên khoa Tim mạch\",\"Điện tâm đồ ECG 12 chuyển đạo\",\"Siêu âm tim Doppler màu\",\"Bộ mỡ máu toàn phần (Cholesterol, Triglyceride, HDL, LDL)\",\"Đo chỉ số xơ vữa ABI\",\"Tư vấn chế độ dinh dưỡng tim mạch\"]",
                IsActive = true
            },
            new HealthPackage
            {
                Code = "PKG03",
                Name = "Gói khám sức khỏe nhi khoa",
                TargetAudience = "Trẻ em từ 0 - 15 tuổi",
                Description = "Đánh giá phát triển thể chất, dinh dưỡng, tầm soát thiếu máu, vi chất và kiểm tra tai mũi họng tổng quát.",
                Price = 950000m,
                IncludedServicesJson = "[\"Khám chuyên khoa Nhi\",\"Đánh giá chỉ số phát triển chiều cao - cân nặng\",\"Tổng phân tích tế bào máu\",\"Kiểm tra vi chất kẽm, canxi, sắt\",\"Nội soi tai mũi họng\",\"Tư vấn lịch tiêm chủng\"]",
                IsActive = true
            },
            new HealthPackage
            {
                Code = "PKG04",
                Name = "Gói tầm soát sức khỏe phụ nữ",
                TargetAudience = "Nữ giới từ 18 tuổi trở lên",
                Description = "Tầm soát bệnh lý phụ khoa, ung thư cổ tử cung, tầm soát tuyến vú và các rối loạn nội tiết.",
                Price = 1950000m,
                IncludedServicesJson = "[\"Khám Sản phụ khoa\",\"Soi tươi dịch âm đạo\",\"Siêu âm đầu dò tử cung buồng trứng\",\"Siêu âm tuyến vú 2 bên\",\"Xét nghiệm Pap smear tầm soát sớm\",\"Định lượng hormon nội tiết\"]",
                IsActive = true
            },
            new HealthPackage
            {
                Code = "PKG05",
                Name = "Gói khám cơ xương khớp và loãng xương",
                TargetAudience = "Người cao tuổi, nhân viên văn phòng, người vận động thể thao",
                Description = "Tầm soát thoái hóa khớp, thoát vị đĩa đệm, viêm khớp dạng thấp và đo mật độ xương toàn thân.",
                Price = 1650000m,
                IncludedServicesJson = "[\"Khám chuyên khoa Cơ xương khớp\",\"Đo mật độ xương DEXA\",\"X-quang khớp gối / cột sống thắt lưng\",\"Xét nghiệm Axit Uric (gút)\",\"Định lượng Canxi và Vitamin D3\"]",
                IsActive = true
            },
            new HealthPackage
            {
                Code = "PKG06",
                Name = "Gói tầm soát gan mật và rối loạn chuyển hóa",
                TargetAudience = "Người có nguy cơ gan nhiễm mỡ, viêm gan, đái tháo đường",
                Description = "Đánh giá chức năng gan mật, tầm soát virus viêm gan B/C, men gan và các chỉ số rối loạn chuyển hóa.",
                Price = 1750000m,
                IncludedServicesJson = "[\"Khám chuyên khoa Nội\",\"Xét nghiệm HBsAg, Anti-HCV\",\"Men gan AST, ALT, GGT\",\"Siêu âm Doppler gan mật tụy lách\",\"Chỉ số đường huyết HbA1c\"]",
                IsActive = true
            }
        };
        foreach (var definition in packageDefs)
        {
            var package = await db.HealthPackages.SingleOrDefaultAsync(p => p.Code == definition.Code);
            if (package == null)
            {
                db.HealthPackages.Add(definition);
                continue;
            }
            package.Name = definition.Name;
            package.Description = definition.Description;
            package.TargetAudience = definition.TargetAudience;
            package.IncludedServicesJson = definition.IncludedServicesJson;
        }
        await db.SaveChangesAsync();
    }
}
