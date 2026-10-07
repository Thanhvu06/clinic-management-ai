using System.Net;
using System.Text.Json;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicManagement.IntegrationTests;

public class ActiveMedicineInventoryFieldsTests(CustomWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task ActiveMedicinesReturnImageUrlAndReorderLevelAndNullForMissingImages()
    {
        var withImage = new Medicine
        {
            Code = $"IMG-{Guid.NewGuid():N}"[..18], Name = "Thuốc có ảnh", Unit = "Viên",
            ImagePath = "medicine-photo.png", StockQuantity = 25, ReorderLevel = 37, UnitPrice = 1500m, IsActive = true,
        };
        var withoutImage = new Medicine
        {
            Code = $"NOIMG-{Guid.NewGuid():N}"[..18], Name = "Thuốc chưa có ảnh", Unit = "Viên",
            ImagePath = null, StockQuantity = 100, ReorderLevel = 20, IsActive = true,
        };
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Medicines.AddRange(withImage, withoutImage);
            await db.SaveChangesAsync();
        }

        await AuthenticateAsync("pharm@test.com");
        var response = await Client.GetAsync("/api/v1/medicines/active");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var medicines = json.RootElement.GetProperty("data").EnumerateArray().ToList();
        var imageMedicine = Assert.Single(medicines, item => item.GetProperty("id").GetInt64() == withImage.Id);
        Assert.Equal("/media/medicines/medicine-photo.png", imageMedicine.GetProperty("imageUrl").GetString());
        Assert.Equal(37, imageMedicine.GetProperty("reorderLevel").GetInt32());
        Assert.Equal(25, imageMedicine.GetProperty("stockQuantity").GetInt32());
        Assert.Equal(1500m, imageMedicine.GetProperty("unitPrice").GetDecimal());

        var noImageMedicine = Assert.Single(medicines, item => item.GetProperty("id").GetInt64() == withoutImage.Id);
        Assert.Equal(JsonValueKind.Null, noImageMedicine.GetProperty("imageUrl").ValueKind);
        Assert.Equal(20, noImageMedicine.GetProperty("reorderLevel").GetInt32());

        var publicResponse = await Client.GetAsync($"/api/v1/public/medicines/{withImage.Id}");
        Assert.Equal(HttpStatusCode.OK, publicResponse.StatusCode);
        using var publicJson = JsonDocument.Parse(await publicResponse.Content.ReadAsStringAsync());
        Assert.Equal(publicJson.RootElement.GetProperty("data").GetProperty("imageUrl").GetString(),
            imageMedicine.GetProperty("imageUrl").GetString());
    }
}
