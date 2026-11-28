using System.Net.Http.Json;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicManagement.IntegrationTests;

[Collection(AiPhase12AcceptanceCollection.Name)]
public sealed class ChatbotSchedulePricingCoverageTests(TypedWizardFactory factory)
    : IntegrationTestBase(factory), IClassFixture<TypedWizardFactory>
{
    [Theory]
    [InlineData("bác sĩ Đinh Thị Yến có khám thứ hai không", "30/11/2026", true, false)]
    [InlineData("bs Dinh Thi Yen co kham ngay 30/11 khong", "30/11/2026", true, false)]
    [InlineData("bác sĩ Đinh Thị Yến có khám chủ nhật không", "29/11/2026", false, false)]
    [InlineData("bác sĩ Đinh Thị Yến có khám thứ bảy không", "28/11/2026", true, false)]
    [InlineData("bác sĩ Đinh Thị Yến có khám sáng mai không", "29/11/2026", false, false)]
    [InlineData("bác sĩ Đinh Thị Yến có khám chiều mai không", "29/11/2026", true, true)]
    [InlineData("bác sĩ Đinh Thị Yến có khám ngày 31/11 không", null, false, false)]
    public async Task Named_doctor_schedule_uses_the_requested_vietnam_date_and_part(
        string message, string? expectedDate, bool hasSlots, bool afternoon)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var doctor = await db.Users.SingleAsync(user => user.Id == DoctorId);
        doctor.FullName = "Đinh Thị Yến";
        await db.SaveChangesAsync();
        factory.Clock.UtcNow = new DateTime(2026, 11, 27, 19, 0, 0, DateTimeKind.Utc);
        var today = factory.Clock.VietnamToday;
        factory.Slots.Clear();
        foreach (var (day, hour) in new[] { (0, 9), (1, 15), (2, 11), (2, 8), (2, 9), (2, 10), (2, 8) })
        {
            factory.Slots.Add(new()
            {
                SlotId = factory.Slots.Count + 1, DoctorId = DoctorEntityId,
                SlotDate = today.AddDays(day), StartTime = new(hour, 0), EndTime = new(hour, 30)
            });
        }
        if (message.Contains("chủ nhật", StringComparison.Ordinal))
            factory.Slots.RemoveAll(slot => slot.SlotDate == today.AddDays(1));

        var response = await ChatAsync(message, hasOpenBookingWizard: true);
        Assert.Equal("DoctorInformation", response.DialogueOutcome);
        Assert.Contains("Đinh Thị Yến", response.Message);
        Assert.DoesNotContain("chưa xác định được chuyên khoa", response.Message);
        Assert.False(response.ProviderWasCalled);
        if (expectedDate is null)
            Assert.Contains("Ngày hỏi lịch không hợp lệ", response.Message);
        else
        {
            Assert.Contains(expectedDate, response.Message);
            Assert.Contains(hasSlots ? "Có lịch trống" : "Không có lịch trống", response.Message);
            var times = response.Message.Split('\n').Where(line => line.StartsWith("- ")).ToArray();
            Assert.Equal(hasSlots ? expectedDate == "30/11/2026" ? 3 : 1 : 0, times.Length);
            Assert.All(times, line => Assert.Contains(expectedDate, line));
            if (times.Length == 3)
                Assert.Equal(new[] { "08:00", "09:00", "10:00" }, times.Select(line => line.Substring(2, 5)));
            if (afternoon)
                Assert.Contains("15:00–15:30", Assert.Single(times));
        }
        var book = Assert.Single(response.Actions);
        Assert.Equal("Đặt lịch với bác sĩ này", book.Label);
        Assert.Equal(DoctorEntityId, book.Payload.DoctorId);
        Assert.Equal(SpecialtyEntityId, book.Payload.SpecialtyId);
    }

    [Theory]
    [InlineData("pin điện thoại bao nhiêu tiền")]
    [InlineData("pin dien thoai bao nhieu tien")]
    [InlineData("giá xe đạp ở đây thế nào")]
    [InlineData("vé máy bay hết nhiu")]
    [InlineData("một hộp bánh quy giá bao nhiêu")]
    public async Task Outside_prices_offer_the_two_clinic_price_paths(string message)
    {
        var response = await ChatAsync(message, hasOpenBookingWizard: true, pendingSpecialtyId: SpecialtyEntityId);
        Assert.Equal("OutsideClinicPricing", response.DialogueOutcome);
        Assert.Contains("chỉ có thông tin về phí khám và giá thuốc", response.Message);
        Assert.DoesNotContain("Bảng giá chi phí khám", response.Message);
        var medicines = Assert.Single(response.Actions);
        Assert.Equal("Mở Bảng giá thuốc", medicines.Label);
        Assert.Equal(SafeRoutes.Medicines, medicines.Payload.TargetUrl);
        Assert.False(response.ProviderWasCalled);
    }

    [Fact]
    public async Task General_fees_include_every_active_priced_specialty_in_display_order()
    {
        using var scope = Factory.Services.CreateScope();
        await DevelopmentDataSeeder.SeedMasterDataAsync(scope.ServiceProvider);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var disabledForBooking = await db.Specialties.SingleAsync(specialty => specialty.SpecialtyCode == "SP08");
        disabledForBooking.AiEnabled = false;
        db.Specialties.AddRange(
            new Specialty { Name = "Chuyên khoa đã ngừng", SpecialtyCode = "ZZ01", ConsultationFee = 60000 },
            new Specialty { Name = "Chuyên khoa chưa có phí", SpecialtyCode = "ZZ02", IsActive = true, AiEnabled = true });
        await db.SaveChangesAsync();
        var expected = await db.Specialties.Where(specialty => specialty.IsActive && specialty.ConsultationFee > 0)
            .OrderBy(specialty => specialty.Id).Select(specialty => specialty.Name).ToListAsync();

        var table = await ChatAsync("cho mình xem bảng giá phí khám theo từng chuyên khoa", pendingSpecialtyId: SpecialtyEntityId);
        Assert.Equal("PricingInquiryResolved", table.DialogueOutcome);
        var entries = table.Message.Split('\n').Where(line => line.StartsWith("- **Khoa ")).ToList();
        Assert.Equal(expected, entries.Select(line => line[9..line.IndexOf("**:", StringComparison.Ordinal)]));
        foreach (var specialty in new[] { "Cơ xương khớp", "Thần kinh", "Nội tiết", "Nhãn khoa", "Tiêu hóa" })
            Assert.Contains(specialty, expected);
        var individual = await ChatAsync("khám thần kinh bao nhiêu tiền");
        Assert.Contains("Thần kinh", individual.Message);
        Assert.Contains("250.000", individual.Message);
        Assert.Contains(entries, line => line.Contains("Thần kinh") && line.Contains("250.000"));
    }

    private async Task<AiChatResponseDto> ChatAsync(string message, bool hasOpenBookingWizard = false, long? pendingSpecialtyId = null)
    {
        using var client = await CreateAuthenticatedClientAsync("pat1@test.com");
        using var response = await client.PostAsJsonAsync("/api/v1/ai/chat", new { message, hasOpenBookingWizard, pendingSpecialtyId });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return Assert.IsType<AiChatResponseDto>((await response.Content.ReadFromJsonAsync<ApiResponse<AiChatResponseDto>>())?.Data);
    }
}
