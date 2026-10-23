using ClinicManagement.Domain.Entities;
using ClinicManagement.Infrastructure.Authentication;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ClinicManagement.IntegrationTests;

public class DemoBrandSeedTests
{
    private const string Name = "Phòng khám đa khoa ClinicCare AI – Thủ Dầu Một";
    private const string Address = "Số 20 Trần Văn Ơn, Thủ Dầu Một";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MasterSeed_SynchronizesFreshAndLegacyData_WithoutChangingCodesOrIds(bool legacy)
    {
        using var factory = new CustomWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<AppDbContext>();
        await RoleSeeder.SeedRolesAsync(services);
        if (legacy)
        {
            db.ClinicLocations.AddRange(
                new ClinicLocation { Code = "CS01", Name = "Cơ sở 1 cũ", Address = "Địa chỉ cũ", IsActive = true },
                new ClinicLocation { Code = "CS02", Name = "Cơ sở 2 cũ", IsActive = true },
                new ClinicLocation { Code = "CS03", Name = "Cơ sở 3 cũ", IsActive = true });
            db.Facilities.Add(new Facility
            {
                Code = "BV-TW-01", Name = "Bệnh viện Đa khoa Trung Ương", Address = "789 Đường Giải Phóng",
                City = "Hà Nội", Email = "contact@bvtw.vn", IsActive = true
            });
            db.Specialties.Add(new Specialty { SpecialtyCode = "SP01", Name = "Nội tổng quát", Description = "Khám nội (Dữ liệu demo)" });
            db.HealthPackages.Add(new HealthPackage { Code = "PKG01", Name = "Gói Khám Sức Khỏe Tổng Quát Tiêu Chuẩn", Description = "Kiểm tra toàn diện", Price = 123m });
            await db.SaveChangesAsync();
        }
        var originalLocations = await db.ClinicLocations.ToDictionaryAsync(l => l.Code, l => l.Id);
        var originalFacility = await db.Facilities.SingleOrDefaultAsync(f => f.Code == "BV-TW-01");
        var originalFacilityId = originalFacility?.Id;

        await DevelopmentDataSeeder.SeedMasterDataAsync(services);
        await DevelopmentDataSeeder.SeedMasterDataAsync(services);
        db.ChangeTracker.Clear();

        var location = Assert.Single(await db.ClinicLocations.Where(l => l.IsActive).ToListAsync());
        Assert.Equal("CS01", location.Code);
        Assert.Equal(Name, location.Name);
        Assert.Equal(Address, location.Address);
        Assert.Equal("Bình Dương", location.City);
        Assert.Equal("0900 000 020", location.Phone);
        Assert.Equal("07:00–17:00 (thứ 2 – thứ 7) · 07:00–11:30 (chủ nhật)", location.OpeningHours);
        Assert.Equal("[\"Khám tổng quát\",\"Xét nghiệm\",\"Siêu âm\",\"X-quang\",\"Nhà thuốc\"]", location.ServicesJson);
        Assert.Equal("Khám ngoại trú các chuyên khoa, xét nghiệm và nhà thuốc tại chỗ.", location.Description);
        var retired = await db.ClinicLocations.Where(l => l.Code == "CS02" || l.Code == "CS03").ToListAsync();
        Assert.Equal(legacy ? 2 : 0, retired.Count);
        Assert.All(retired, l => Assert.False(l.IsActive));
        foreach (var original in originalLocations)
            Assert.Equal(original.Value, (await db.ClinicLocations.SingleAsync(l => l.Code == original.Key)).Id);

        var facility = await db.Facilities.SingleAsync(f => f.Code == "BV-TW-01");
        Assert.Equal(Name, facility.Name);
        Assert.Equal(Address, facility.Address);
        Assert.Equal(location.City, facility.City);
        Assert.Equal(location.Phone, facility.Phone);
        Assert.Equal(location.Description, facility.Description);
        Assert.Equal("lienhe@cliniccare.local", facility.Email);
        Assert.Equal("Phòng khám đa khoa", facility.HospitalLevel);
        if (legacy) Assert.Equal(originalFacilityId, facility.Id);

        var specialties = await db.Specialties.OrderBy(s => s.SpecialtyCode).ToListAsync();
        Assert.Equal(Enumerable.Range(1, 11).Select(i => $"SP{i:D2}"), specialties.Select(s => s.SpecialtyCode));
        var packages = await db.HealthPackages.OrderBy(p => p.Code).ToListAsync();
        Assert.Equal(Enumerable.Range(1, 6).Select(i => $"PKG{i:D2}"), packages.Select(p => p.Code));
        Assert.Equal(new[] { "Gói khám sức khỏe tổng quát tiêu chuẩn", "Gói tầm soát tim mạch", "Gói khám sức khỏe nhi khoa", "Gói tầm soát sức khỏe phụ nữ", "Gói khám cơ xương khớp và loãng xương", "Gói tầm soát gan mật và rối loạn chuyển hóa" }, packages.Select(p => p.Name));
        if (legacy) Assert.Equal(123m, packages[0].Price);
        var texts = specialties.Select(s => s.Description).Concat(packages.Select(p => p.Description))
            .Concat(packages.Select(p => p.Name)).Concat(new[] { facility.Name, facility.Description, facility.Address, facility.Email, location.Description });
        foreach (var text in texts)
        {
            Assert.DoesNotContain("(Dữ liệu demo)", text ?? "");
            Assert.DoesNotContain("Bệnh viện Đa khoa Trung Ương", text ?? "");
            Assert.DoesNotContain("toàn diện", (text ?? "").ToLowerInvariant());
        }
    }
}
