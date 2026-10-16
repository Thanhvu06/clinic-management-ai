using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Infrastructure.Medicines;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicManagement.IntegrationTests;

public class MedicineDetailTests(CustomWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    private static readonly string[] Fields = ["indications", "dosageAndAdministration", "contraindications", "sideEffects", "precautions", "overdoseManagement"];
    private static async Task<JsonElement> Data(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("data").Clone();
    }
    private async Task<long> Create(string ingredient = "detail ingredient", bool active = true)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var medicine = new Medicine { Code = "DETAIL-" + Guid.NewGuid().ToString("N"), Name = "Detail test " + Guid.NewGuid(), Unit = "Viên",
            ActiveIngredient = ingredient, IsActive = active, Indications = "Chỉ định mẫu", DosageAndAdministration = "Theo đơn",
            Contraindications = "Chống chỉ định mẫu", SideEffects = "Tác dụng phụ mẫu", Precautions = "Thận trọng mẫu", OverdoseManagement = "Liên hệ cơ sở y tế" };
        db.Medicines.Add(medicine);
        await db.SaveChangesAsync();
        return medicine.Id;
    }

    [Fact]
    public async Task PublicDetailReturnsAllMonographFieldsWithoutInternalDataOrListInteractions()
    {
        var id = await Create();
        using var anonymous = Factory.CreateClient();
        var detail = await Data(await anonymous.GetAsync($"/api/v1/public/medicines/{id}"));
        foreach (var field in Fields) Assert.False(string.IsNullOrWhiteSpace(detail.GetProperty(field).GetString()));
        Assert.Equal("Chỉ định mẫu", detail.GetProperty("indications").GetString());
        Assert.Empty(detail.GetProperty("interactions").EnumerateArray());
        Assert.False(detail.TryGetProperty("stockQuantity", out _));
        Assert.False(detail.TryGetProperty("reorderLevel", out _));
        var list = await Data(await anonymous.GetAsync("/api/v1/public/medicines?pageSize=50"));
        Assert.All(list.GetProperty("items").EnumerateArray(), item => Assert.False(item.TryGetProperty("interactions", out _)));
    }

    [Theory]
    [InlineData("Ciprofloxacin", "domperidone", "Major")]
    [InlineData("Ciprofloxacin hydrochloride", "calci", "Moderate")]
    [InlineData("Calci carbonat + Cholecalciferol", "ciprofloxacin", "Moderate")]
    public async Task PublicDetailFindsSharedInteractionRulesInBothDirectionsAndSaltForms(string ingredient, string other, string severity)
    {
        var id = await Create(ingredient);
        var detail = await Data(await Client.GetAsync($"/api/v1/public/medicines/{id}"));
        var interaction = Assert.Single(detail.GetProperty("interactions").EnumerateArray(), i => i.GetProperty("otherIngredient").GetString() == other);
        Assert.Equal(severity, interaction.GetProperty("severity").GetString());
        Assert.False(string.IsNullOrWhiteSpace(interaction.GetProperty("description").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(interaction.GetProperty("recommendation").GetString()));
    }

    [Theory]
    [InlineData("no known ingredient")]
    [InlineData("cipro")]
    [InlineData("")]
    public async Task UnknownOrPartialIngredientsReturnEmptyInteractions(string ingredient)
    {
        var detail = await Data(await Client.GetAsync($"/api/v1/public/medicines/{await Create(ingredient)}"));
        Assert.Empty(detail.GetProperty("interactions").EnumerateArray());
    }

    [Fact]
    public async Task InactiveMedicineIsNotPubliclyAccessible()
    {
        var id = await Create(active: false);
        using var anonymous = Factory.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/v1/public/medicines/{id}")).StatusCode);
    }

    [Theory]
    [InlineData("admin@test.com")]
    [InlineData("pharm@test.com")]
    public async Task CatalogEditorsCanPersistClearAndOmitAllSixFields(string email)
    {
        await AuthenticateAsync(email);
        var id = await Create();
        var payload = new Dictionary<string, object?> { ["name"] = "Updated detail", ["unit"] = "Viên", ["isActive"] = true };
        foreach (var field in Fields) payload[field] = "Đã sửa " + field;
        var updated = await Data(await Client.PutAsJsonAsync($"/api/v1/admin/medicines/{id}", payload));
        foreach (var field in Fields) Assert.Equal(payload[field], updated.GetProperty(field).GetString());
        var legacy = await Data(await Client.PutAsJsonAsync($"/api/v1/admin/medicines/{id}", new { name = "Legacy update", unit = "Viên" }));
        var stored = await Data(await Client.GetAsync($"/api/v1/public/medicines/{id}"));
        foreach (var field in Fields)
        {
            Assert.Equal(payload[field], legacy.GetProperty(field).GetString());
            Assert.Equal(payload[field], stored.GetProperty(field).GetString());
            payload[field] = null;
        }
        var cleared = await Data(await Client.PutAsJsonAsync($"/api/v1/admin/medicines/{id}", payload));
        foreach (var field in Fields) Assert.Equal(JsonValueKind.Null, cleared.GetProperty(field).ValueKind);
        payload["code"] = "DETAIL-" + Guid.NewGuid().ToString("N");
        foreach (var field in Fields) payload[field] = "Tạo mới " + field;
        var created = await Data(await Client.PostAsJsonAsync("/api/v1/admin/medicines", payload));
        foreach (var field in Fields) Assert.Equal(payload[field], created.GetProperty(field).GetString());
    }

    [Theory]
    [InlineData("indications", 2000)]
    [InlineData("dosageAndAdministration", 2000)]
    [InlineData("contraindications", 2000)]
    [InlineData("sideEffects", 2000)]
    [InlineData("precautions", 2000)]
    [InlineData("overdoseManagement", 1000)]
    public async Task MonographLengthLimitsAreValidatedOnCreateAndUpdate(string field, int limit)
    {
        await AuthenticateAsync("admin@test.com");
        var id = await Create();
        var payload = new Dictionary<string, object?> { ["code"] = "DETAIL-" + Guid.NewGuid().ToString("N"), ["name"] = "Boundary", ["unit"] = "Viên", [field] = new string('é', limit) };
        await Data(await Client.PostAsJsonAsync("/api/v1/admin/medicines", payload));
        await Data(await Client.PutAsJsonAsync($"/api/v1/admin/medicines/{id}", payload));
        payload[field] = new string('é', limit + 1);
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.PostAsJsonAsync("/api/v1/admin/medicines", payload)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.PutAsJsonAsync($"/api/v1/admin/medicines/{id}", payload)).StatusCode);
    }

    [Fact]
    public async Task InteractionQueriesAreBatchedAndInactiveRulesAreExcluded()
    {
        var unique = "detail-" + Guid.NewGuid().ToString("N");
        var id = await Create("Ciprofloxacin + Calci carbonat + " + unique);
        using var scope = Factory.Services.CreateScope();
        var source = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        source.DrugInteractionRules.Add(new DrugInteractionRule { IngredientKeyA = "a-" + unique, IngredientKeyB = unique, IsActive = false, Description = "Hidden", Recommendation = "Hidden" });
        await source.SaveChangesAsync();
        var counter = new QueryCounter();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(source.Database.GetConnectionString()).AddInterceptors(counter).Options);
        var result = await new MedicineCatalogService(db).GetPublicMedicineAsync(id);
        Assert.Equal(2, counter.Count);
        Assert.DoesNotContain(result.Interactions!, i => i.Description == "Hidden");
        Assert.Contains(result.Interactions!, i => i.OtherIngredient == "domperidone");
    }

    [Fact]
    public async Task CatalogSeedPopulatesThirtyMonographsDefersRxDosesAndPreservesAdminChanges()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        await DevelopmentDataSeeder.SeedMedicineCatalogAsync(db);
        var medicines = await db.Medicines.OrderBy(m => m.Code).ToListAsync();
        Assert.Equal(Enumerable.Range(1, 30).Select(i => $"MED{i:00}"), medicines.Select(m => m.Code));
        var properties = new[] { nameof(Medicine.Indications), nameof(Medicine.DosageAndAdministration), nameof(Medicine.Contraindications), nameof(Medicine.SideEffects), nameof(Medicine.Precautions), nameof(Medicine.OverdoseManagement) };
        foreach (var medicine in medicines)
        {
            foreach (var property in properties) Assert.False(string.IsNullOrWhiteSpace((string?)typeof(Medicine).GetProperty(property)!.GetValue(medicine)));
            if (medicine.IsPrescriptionRequired)
            {
                Assert.StartsWith("Dùng theo đơn và chỉ định của bác sĩ. Không tự ý tăng/giảm liều hoặc ngừng thuốc.", medicine.DosageAndAdministration);
                Assert.False(Regex.IsMatch(medicine.DosageAndAdministration!, @"\d+\s*mg", RegexOptions.IgnoreCase));
            }
            else Assert.Contains("Trẻ em: hỏi ý kiến bác sĩ/dược sĩ", medicine.DosageAndAdministration);
        }
        var edited = medicines[0];
        foreach (var property in properties) typeof(Medicine).GetProperty(property)!.SetValue(edited, "Nội dung dược sĩ " + property);
        await db.SaveChangesAsync();
        await DevelopmentDataSeeder.SeedMedicineCatalogAsync(db);
        db.ChangeTracker.Clear();
        var preserved = await db.Medicines.SingleAsync(m => m.Id == edited.Id);
        foreach (var property in properties) Assert.Equal("Nội dung dược sĩ " + property, typeof(Medicine).GetProperty(property)!.GetValue(preserved));
    }

    [Fact]
    public async Task GeneratedMonographMigrationAddsNullableColumnsAndPreservesExistingMedicineData()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE Medicines (Id INTEGER PRIMARY KEY AUTOINCREMENT, Code TEXT NOT NULL, Name TEXT NOT NULL,
                Unit TEXT NOT NULL, StockQuantity INTEGER NOT NULL, ReorderLevel INTEGER NOT NULL,
                UnitPrice TEXT NULL, IsActive INTEGER NOT NULL DEFAULT 1, RowVersion BLOB NULL,
                CreatedAt TEXT NOT NULL, UpdatedAt TEXT NULL, ActiveIngredient TEXT NULL, CategoryId INTEGER NULL,
                Description TEXT NULL, DosageForm TEXT NULL, ImagePath TEXT NULL, IsPrescriptionRequired INTEGER NOT NULL DEFAULT 1,
                Manufacturer TEXT NULL, StorageInstructions TEXT NULL, Strength TEXT NULL);
            CREATE UNIQUE INDEX IX_Medicines_Code ON Medicines(Code);
            INSERT INTO Medicines(Code,Name,Unit,StockQuantity,ReorderLevel,UnitPrice,IsActive,CreatedAt)
                VALUES ('OLD-DETAIL','Existing medicine','Viên',17,5,'2345',1,'2026-10-01');
            """);
        var assembly = db.GetService<IMigrationsAssembly>();
        var generator = db.GetService<IMigrationsSqlGenerator>();
        // Apply the generated migration to a fixture with the pre-monograph columns.
        var monograph = assembly.CreateMigration(Assert.Single(assembly.Migrations, m => m.Key.EndsWith("_AddMedicineMonograph")).Value, db.Database.ProviderName!);
        Assert.Equal(6, monograph.UpOperations.Count);
        foreach (var command in generator.Generate(monograph.UpOperations, db.GetService<IDesignTimeModel>().Model)) await db.Database.ExecuteSqlRawAsync(command.CommandText);
        var medicine = await db.Medicines.SingleAsync();
        Assert.Equal("Existing medicine", medicine.Name);
        Assert.Equal(17, medicine.StockQuantity);
        Assert.Equal(2345m, medicine.UnitPrice);
        Assert.Null(medicine.Indications);
        Assert.Null(medicine.DosageAndAdministration);
        Assert.Null(medicine.Contraindications);
        Assert.Null(medicine.SideEffects);
        Assert.Null(medicine.Precautions);
        Assert.Null(medicine.OverdoseManagement);
        medicine.Indications = "New guidance";
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        Assert.Equal("New guidance", (await db.Medicines.SingleAsync()).Indications);
    }

    private sealed class QueryCounter : DbCommandInterceptor
    {
        public int Count { get; private set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { Count++; return base.ReaderExecutingAsync(command, eventData, result, cancellationToken); }
    }
}
