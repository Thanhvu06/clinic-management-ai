using System.Text.Json;
using ClinicManagement.Application.AI.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Infrastructure.Authentication;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicManagement.IntegrationTests;

public class OpeningHoursRegressionTests
{
    private const string OpeningHours = "Thứ 2 – Thứ 6: 08:00–17:00 · Thứ 7: 08:00–11:30 · Chủ nhật nghỉ";

    [Fact]
    public void NewLocation_DefaultsToTheActualWeeklyOpeningHours()
    {
        Assert.Equal(OpeningHours, new ClinicLocation().OpeningHours);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MasterSeed_AlignsFreshAndExistingLocationHoursWithAiContext(bool existingLocation)
    {
        using var factory = new FixedSeedWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<AppDbContext>();
        await RoleSeeder.SeedRolesAsync(services);
        if (existingLocation)
        {
            db.ClinicLocations.Add(new ClinicLocation
            {
                Code = "CS01", Name = "Cơ sở cũ", Address = "Địa chỉ cũ",
                OpeningHours = "07:00–17:00 (thứ 2 – thứ 7) · Chủ nhật nghỉ"
            });
            await db.SaveChangesAsync();
        }

        await DevelopmentDataSeeder.SeedMasterDataAsync(services);
        await DevelopmentDataSeeder.SeedMasterDataAsync(services);
        db.ChangeTracker.Clear();

        var location = await db.ClinicLocations.SingleAsync(l => l.Code == "CS01");
        Assert.Equal(OpeningHours, location.OpeningHours);
        var contextService = services.GetRequiredService<IClinicAiContextService>();
        using var context = JsonDocument.Parse(await contextService.GetClinicContextJsonAsync());
        Assert.Equal(OpeningHours, context.RootElement.GetProperty("ThongTinCoSo").GetProperty("GioLamViec").GetString());
        Assert.Contains(OpeningHours, context.RootElement.GetProperty("HuongDanCoSo").GetString());
    }
}
