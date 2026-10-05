using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicManagement.IntegrationTests;

public class MedicineCatalogFactory : CustomWebApplicationFactory
{
    public string ImagesPath { get; } = Path.Combine(Path.GetTempPath(), $"medicine-images-{Guid.NewGuid():N}");
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
            new Dictionary<string, string?> { ["Storage:MedicineImagesPath"] = ImagesPath }));
    }
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(ImagesPath)) Directory.Delete(ImagesPath, true);
    }
}

public class MedicineCatalogTests : IntegrationTestBase, IClassFixture<MedicineCatalogFactory>
{
    private readonly MedicineCatalogFactory _catalogFactory;
    public MedicineCatalogTests(MedicineCatalogFactory factory) : base(factory) => _catalogFactory = factory;
    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    private async Task<long> Category(string? name = null)
    {
        var response = await Client.PostAsJsonAsync("/api/v1/admin/medicine-categories", new { name = name ?? Guid.NewGuid().ToString(), sortOrder = 1 });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await Json(response)).GetProperty("data").GetProperty("id").GetInt64();
    }
    private async Task<long> Medicine(long? categoryId = null, bool rx = true)
    {
        var response = await Client.PostAsJsonAsync("/api/v1/admin/medicines", new {
            code = "CAT-" + Guid.NewGuid().ToString("N"), name = "Catalog test", unit = "Viên",
            stockQuantity = 200, reorderLevel = 40, unitPrice = 1234, categoryId,
            activeIngredient = "UniqueIngredient", strength = "500 mg", dosageForm = "Viên nén",
            manufacturer = "Example", isPrescriptionRequired = rx, description = "Mô tả", storageInstructions = "Khô ráo"
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = (await Json(response)).GetProperty("data");
        Assert.Equal(rx, data.GetProperty("isPrescriptionRequired").GetBoolean());
        Assert.Equal("500 mg", data.GetProperty("strength").GetString());
        return data.GetProperty("id").GetInt64();
    }

    [Fact]
    public async Task CategoriesCrudUniquenessAndActiveMedicineGuard()
    {
        await AuthenticateAsync("admin@test.com");
        var name = Guid.NewGuid().ToString();
        var category = await Category(name);
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.PostAsJsonAsync("/api/v1/admin/medicine-categories", new { name })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Client.PutAsJsonAsync($"/api/v1/admin/medicine-categories/{category}", new { name = name + " edited", description = "Test", sortOrder = 3 })).StatusCode);
        var medicine = await Medicine(category);
        var blocked = await Client.PatchAsync($"/api/v1/admin/medicine-categories/{category}/toggle-status", null);
        Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);
        Assert.Equal("CATEGORY_HAS_ACTIVE_MEDICINES", (await Json(blocked)).GetProperty("errorCode").GetString());
        await Client.PatchAsync($"/api/v1/admin/medicines/{medicine}/toggle-status", null);
        Assert.Equal(HttpStatusCode.OK, (await Client.PatchAsync($"/api/v1/admin/medicine-categories/{category}/toggle-status", null)).StatusCode);
        var categories = (await Json(await Client.GetAsync("/api/v1/admin/medicine-categories"))).GetProperty("data");
        Assert.Contains(categories.EnumerateArray(), c => c.GetProperty("id").GetInt64() == category && !c.GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task InvalidOrInactiveCategoryIsRejectedOnCreateAndUpdate()
    {
        await AuthenticateAsync("admin@test.com");
        var category = await Category();
        await Client.PatchAsync($"/api/v1/admin/medicine-categories/{category}/toggle-status", null);
        var medicine = await Medicine();
        foreach (var id in new[] { category, long.MaxValue })
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await Client.PostAsJsonAsync("/api/v1/admin/medicines", new { code = Guid.NewGuid().ToString(), name = "Bad", unit = "Viên", categoryId = id })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await Client.PutAsJsonAsync($"/api/v1/admin/medicines/{medicine}", new { name = "Bad", unit = "Viên", categoryId = id })).StatusCode);
        }
    }

    [Fact]
    public async Task ExtendedFieldsSearchFiltersAndLegacyUpdateAreCompatible()
    {
        await AuthenticateAsync("pharm@test.com");
        var category = await Category();
        var medicine = await Medicine(category, false);
        var response = await Client.GetAsync($"/api/v1/admin/medicines?search=uniqueingredient&categoryId={category}&isPrescriptionRequired=false");
        var data = (await Json(response)).GetProperty("data");
        Assert.Single(data.GetProperty("items").EnumerateArray());
        var legacy = await Client.PutAsJsonAsync($"/api/v1/admin/medicines/{medicine}", new { name = "Renamed", unit = "Viên", reorderLevel = 40, unitPrice = 4321, isActive = true });
        var updated = (await Json(legacy)).GetProperty("data");
        Assert.Equal("UniqueIngredient", updated.GetProperty("activeIngredient").GetString());
        Assert.False(updated.GetProperty("isPrescriptionRequired").GetBoolean());
        Assert.Equal(category, updated.GetProperty("categoryId").GetInt64());
        var active = (await Json(await Client.GetAsync("/api/v1/medicines/active"))).GetProperty("data").EnumerateArray().Single(m => m.GetProperty("id").GetInt64() == medicine);
        Assert.False(active.GetProperty("isPrescriptionRequired").GetBoolean());
        Assert.Equal("500 mg", active.GetProperty("strength").GetString());
    }

    [Theory]
    [InlineData("doc@test.com")]
    [InlineData("pat1@test.com")]
    public async Task OtherRolesCannotManageCatalogOrImages(string email)
    {
        await AuthenticateAsync(email);
        Assert.Equal(HttpStatusCode.Forbidden, (await Client.GetAsync("/api/v1/admin/medicine-categories")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Client.PostAsJsonAsync("/api/v1/admin/medicine-categories", new { name = "Denied" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Client.PutAsJsonAsync("/api/v1/admin/medicine-categories/1", new { name = "Denied" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Client.PatchAsync("/api/v1/admin/medicine-categories/1/toggle-status", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Client.PostAsJsonAsync("/api/v1/admin/medicines", new { code = "DENIED", name = "Denied", unit = "Viên" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Client.PostAsync($"/api/v1/admin/medicines/{MedicineEntityId}/image", new MultipartFormDataContent())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Client.DeleteAsync($"/api/v1/admin/medicines/{MedicineEntityId}/image")).StatusCode);
    }

    [Theory]
    [InlineData("activeIngredient", 200)]
    [InlineData("strength", 100)]
    [InlineData("dosageForm", 100)]
    [InlineData("manufacturer", 200)]
    [InlineData("description", 1000)]
    [InlineData("storageInstructions", 500)]
    [InlineData("imagePath", 300)]
    public async Task NewFieldLengthsAreValidated(string field, int maxLength)
    {
        await AuthenticateAsync("admin@test.com");
        var payload = new Dictionary<string, object> { ["code"] = Guid.NewGuid().ToString(), ["name"] = "Lengths", ["unit"] = "Viên", [field] = new string('x', maxLength + 1) };
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.PostAsJsonAsync("/api/v1/admin/medicines", payload)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.PutAsJsonAsync($"/api/v1/admin/medicines/{MedicineEntityId}", payload)).StatusCode);
    }

    [Fact]
    public async Task LegacyCreateDefaultsToRxAndExplicitNullClearsAddedFields()
    {
        await AuthenticateAsync("admin@test.com");
        var created = await Client.PostAsJsonAsync("/api/v1/admin/medicines", new { code = Guid.NewGuid().ToString(), name = "Legacy", unit = "Viên" });
        Assert.True((await Json(created)).GetProperty("data").GetProperty("isPrescriptionRequired").GetBoolean());
        var id = await Medicine(await Category());
        var cleared = await Client.PutAsJsonAsync($"/api/v1/admin/medicines/{id}", new { name = "Cleared", unit = "Viên", categoryId = (long?)null, strength = (string?)null, activeIngredient = (string?)null });
        var data = (await Json(cleared)).GetProperty("data");
        Assert.Equal(JsonValueKind.Null, data.GetProperty("categoryId").ValueKind);
        Assert.Equal(JsonValueKind.Null, data.GetProperty("strength").ValueKind);
        Assert.Equal(JsonValueKind.Null, data.GetProperty("activeIngredient").ValueKind);
    }

    [Fact]
    public async Task PublicCatalogHidesInternalFieldsInactiveMedicinesAndInactiveCategories()
    {
        await AuthenticateAsync("admin@test.com");
        var category = await Category();
        var medicine = await Medicine(category, false);
        using var anonymous = Factory.CreateClient();
        var response = await anonymous.GetAsync($"/api/v1/public/medicines?search=uniqueingredient&categoryId={category}&type=otc&pageSize=100");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = (await Json(response)).GetProperty("data");
        Assert.Equal(50, data.GetProperty("pageSize").GetInt32());
        var item = Assert.Single(data.GetProperty("items").EnumerateArray());
        Assert.Equal("in_stock", item.GetProperty("availability").GetString());
        Assert.False(item.TryGetProperty("stockQuantity", out _));
        Assert.False(item.TryGetProperty("reorderLevel", out _));
        Assert.False(item.TryGetProperty("rowVersion", out _));
        Assert.Equal(0, (await Json(await anonymous.GetAsync($"/api/v1/public/medicines?categoryId={category}&type=rx"))).GetProperty("data").GetProperty("totalItems").GetInt32());
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var entity = await db.Medicines.FindAsync(medicine);
            entity!.StockQuantity = 40;
            await db.SaveChangesAsync();
        }
        Assert.Equal("low", (await Json(await anonymous.GetAsync($"/api/v1/public/medicines/{medicine}"))).GetProperty("data").GetProperty("availability").GetString());
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var entity = await db.Medicines.FindAsync(medicine);
            entity!.StockQuantity = 0;
            await db.SaveChangesAsync();
        }
        Assert.Equal("out_of_stock", (await Json(await anonymous.GetAsync($"/api/v1/public/medicines/{medicine}"))).GetProperty("data").GetProperty("availability").GetString());
        await Client.PatchAsync($"/api/v1/admin/medicines/{medicine}/toggle-status", null);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/v1/public/medicines/{medicine}")).StatusCode);
        await Client.PatchAsync($"/api/v1/admin/medicine-categories/{category}/toggle-status", null);
        // Simulate imported legacy data with an active medicine under an inactive category.
        await Client.PatchAsync($"/api/v1/admin/medicines/{medicine}/toggle-status", null);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/v1/public/medicines/{medicine}")).StatusCode);
        Assert.DoesNotContain((await Json(await anonymous.GetAsync("/api/v1/public/medicine-categories"))).GetProperty("data").EnumerateArray(), c => c.GetProperty("id").GetInt64() == category);
    }

    [Fact]
    public async Task ImagesCheckSignaturesLimitReplaceDeleteAndStaticScope()
    {
        await AuthenticateAsync("pharm@test.com");
        var medicine = await Medicine();
        string? previous = null;
        foreach (var bytes in new[] {
            Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aS1cAAAAASUVORK5CYII="),
            new byte[] { 0xff, 0xd8, 0xff, 0xe0, 0, 0, 0xff, 0xd9 },
            "RIFF\u0018\0\0\0WEBPVP8 "u8.ToArray()
        })
        {
            using var form = new MultipartFormDataContent();
            form.Add(new ByteArrayContent(bytes), "file", "../../renamed.exe");
            var upload = await Client.PostAsync($"/api/v1/admin/medicines/{medicine}/image", form);
            Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
            var path = (await Json(upload)).GetProperty("data").GetProperty("imagePath").GetString()!;
            Assert.StartsWith(medicine + "-", path);
            Assert.True(File.Exists(Path.Combine(_catalogFactory.ImagesPath, path)));
            if (previous != null) Assert.False(File.Exists(Path.Combine(_catalogFactory.ImagesPath, previous)));
            var url = (await Json(upload)).GetProperty("data").GetProperty("imageUrl").GetString()!;
            Assert.Equal(bytes, await Client.GetByteArrayAsync(url));
            previous = path;
        }
        foreach (var (bytes, code) in new[] { ("MZ-fake-executable"u8.ToArray(), "INVALID_IMAGE_TYPE"), (new byte[2 * 1024 * 1024 + 1], "IMAGE_TOO_LARGE") })
        {
            using var form = new MultipartFormDataContent();
            var file = new ByteArrayContent(bytes);
            file.Headers.ContentType = new("image/png");
            form.Add(file, "file", "fake.png");
            var response = await Client.PostAsync($"/api/v1/admin/medicines/{medicine}/image", form);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(code, (await Json(response)).GetProperty("errorCode").GetString());
        }
        Assert.Equal(HttpStatusCode.OK, (await Client.DeleteAsync($"/api/v1/admin/medicines/{medicine}/image")).StatusCode);
        Assert.False(File.Exists(Path.Combine(_catalogFactory.ImagesPath, previous!)));
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync("/media/medicines/../appsettings.json")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync("/uploads/medicines/" + previous)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync("/media/medicines/" + previous)).StatusCode);
    }
}
