using System.Reflection;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ClinicManagement.IntegrationTests;

public class MedicineCatalogPersistenceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OfficialMigrationAppliesToSqlServerEmptyAndLegacyMedicineData(bool legacyData)
    {
        Assert.True(await SqlServerTestHelper.IsSqlServerAvailableAsync(), "SQL Server is required to verify the official migration.");
        var databaseName = "MedicineCatalog_" + Guid.NewGuid().ToString("N");
        await SqlServerTestHelper.CreateDatabaseAsync(databaseName);
        try
        {
            await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlServer(SqlServerTestHelper.GetTestDatabaseConnectionString(databaseName)).Options);
            var ids = db.Database.GetMigrations().ToArray();
            var catalogId = Assert.Single(ids.Where(id => id.EndsWith("_AddMedicineCatalogData")));
            if (legacyData)
            {
                await db.GetService<IMigrator>().MigrateAsync(ids[Array.IndexOf(ids, catalogId) - 1]);
                await db.Database.ExecuteSqlRawAsync("INSERT INTO Medicines(Code,Name,Unit,StockQuantity,ReorderLevel,UnitPrice,IsActive,CreatedAt) VALUES ('OLD','Original','Viên',17,5,2345,1,GETUTCDATE());");
            }
            await db.Database.MigrateAsync();
            if (legacyData)
            {
                var medicine = await db.Medicines.SingleAsync();
                Assert.True((bool)typeof(Medicine).GetProperty("IsPrescriptionRequired")!.GetValue(medicine)!);
                Assert.Equal(17, medicine.StockQuantity);
                Assert.Equal(2345m, medicine.UnitPrice);
            }
            var script = db.GetService<IMigrator>().GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent);
            // Use the same official script a second time to verify idempotence.
            foreach (var batch in System.Text.RegularExpressions.Regex.Split(script, @"^GO\s*$", System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                if (!string.IsNullOrWhiteSpace(batch)) await db.Database.ExecuteSqlRawAsync(batch);
            Assert.Contains(catalogId, await db.Database.GetAppliedMigrationsAsync());
        }
        finally { await SqlServerTestHelper.DropDatabaseAsync(databaseName); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OfficialMigrationAppliesToSqliteEmptyAndLegacyMedicineData(bool legacyData)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        var assembly = db.GetService<IMigrationsAssembly>();
        var entry = assembly.Migrations.SingleOrDefault(m => m.Key.EndsWith("_AddMedicineCatalogData"));
        Assert.NotNull(entry.Value);
        var migration = assembly.CreateMigration(entry.Value!, db.Database.ProviderName!);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE Medicines (Id INTEGER PRIMARY KEY AUTOINCREMENT, Code TEXT NOT NULL, Name TEXT NOT NULL,
                Unit TEXT NOT NULL, StockQuantity INTEGER NOT NULL, ReorderLevel INTEGER NOT NULL,
                UnitPrice TEXT NULL, IsActive INTEGER NOT NULL DEFAULT 1, RowVersion BLOB NULL,
                CreatedAt TEXT NOT NULL, UpdatedAt TEXT NULL);
            CREATE UNIQUE INDEX IX_Medicines_Code ON Medicines(Code);
            """);
        if (legacyData)
            await db.Database.ExecuteSqlRawAsync("INSERT INTO Medicines (Code,Name,Unit,StockQuantity,ReorderLevel,UnitPrice,CreatedAt) VALUES ('OLD','Old medicine','Viên',17,5,'2345','2026-10-01');");
        var commands = db.GetService<IMigrationsSqlGenerator>().Generate(migration.UpOperations, db.GetService<IDesignTimeModel>().Model);
        foreach (var command in commands) await db.Database.ExecuteSqlRawAsync(command.CommandText);
        if (legacyData)
        {
            var old = await db.Medicines.SingleAsync();
            Assert.True((bool)typeof(Medicine).GetProperty("IsPrescriptionRequired")!.GetValue(old)!);
            Assert.Equal(17, old.StockQuantity);
            Assert.Equal(2345m, old.UnitPrice);
        }
        await db.Database.ExecuteSqlRawAsync("INSERT INTO MedicineCategories(Name,SortOrder,CreatedAt) VALUES ('Migration group',1,'2026-10-01');");
        var categoryId = await db.Database.SqlQueryRaw<long>("SELECT Id AS Value FROM MedicineCategories").SingleAsync();
        Assert.True(categoryId > 0);
        var newMedicine = new Medicine { Code = "NEW", Name = "New medicine", Unit = "Viên" };
        typeof(Medicine).GetProperty("CategoryId")!.SetValue(newMedicine, categoryId);
        typeof(Medicine).GetProperty("IsPrescriptionRequired")!.SetValue(newMedicine, false);
        db.Medicines.Add(newMedicine);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        Assert.False((bool)typeof(Medicine).GetProperty("IsPrescriptionRequired")!.GetValue(await db.Medicines.SingleAsync(m => m.Code == "NEW"))!);
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync("DELETE FROM MedicineCategories;"));
    }

    [Fact]
    public async Task SeederTwiceAddsExactlyElevenCategoriesAndThirtyMedicinesWithoutOverwritingExistingValues()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var seed = typeof(DevelopmentDataSeeder).GetMethod("SeedMedicineCatalogAsync", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(seed);
        var old = new Medicine { Code = "MED01", Name = "Existing name", Unit = "Original unit", StockQuantity = 17, ReorderLevel = 7, UnitPrice = null, IsActive = false };
        db.Medicines.Add(old);
        await db.SaveChangesAsync();
        await (Task)seed!.Invoke(null, new object[] { db })!;
        typeof(Medicine).GetProperty("Strength")!.SetValue(old, "custom strength");
        await db.SaveChangesAsync();
        await (Task)seed.Invoke(null, new object[] { db })!;
        Assert.Equal(30, await db.Medicines.CountAsync());
        Assert.Equal(11, await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM MedicineCategories").SingleAsync());
        Assert.Equal("Existing name", old.Name);
        Assert.Equal("Original unit", old.Unit);
        Assert.Equal(17, old.StockQuantity);
        Assert.Equal(7, old.ReorderLevel);
        Assert.Null(old.UnitPrice);
        Assert.False(old.IsActive);
        Assert.Equal("custom strength", typeof(Medicine).GetProperty("Strength")!.GetValue(old));
        Assert.True((bool)typeof(Medicine).GetProperty("IsPrescriptionRequired")!.GetValue(old)!);
        var low = await db.Medicines.SingleAsync(m => m.Code == "MED10");
        Assert.Equal(25, low.StockQuantity);
        Assert.Equal(50, low.ReorderLevel);
        foreach (var medicine in await db.Medicines.Where(m => m.Code != "MED01" && m.Code != "MED10").ToListAsync())
        { Assert.Equal(200, medicine.StockQuantity); Assert.Equal(40, medicine.ReorderLevel); }
        foreach (var medicine in await db.Medicines.ToListAsync())
        {
            Assert.Equal("Bảo quản nơi khô, dưới 30°C, tránh ánh sáng.", typeof(Medicine).GetProperty("StorageInstructions")!.GetValue(medicine));
            Assert.NotNull(typeof(Medicine).GetProperty("Description")!.GetValue(medicine));
            Assert.Null(typeof(Medicine).GetProperty("ImagePath")!.GetValue(medicine));
        }
        Assert.False((bool)typeof(Medicine).GetProperty("IsPrescriptionRequired")!.GetValue(await db.Medicines.SingleAsync(m => m.Code == "MED30"))!);
    }
}
