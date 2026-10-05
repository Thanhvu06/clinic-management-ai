using System.Text.Json;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ClinicManagement.IntegrationTests;

public class MedicineCatalogLegacySeedTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MigrationAndSeedClassifyLegacyMedicinesOnceAndPreservePharmacistChanges(bool sqlServer)
    {
        var databaseName = "MedicineLegacySeed_" + Guid.NewGuid().ToString("N");
        await using var connection = new SqliteConnection("Data Source=:memory:");
        if (sqlServer)
        {
            Assert.True(await SqlServerTestHelper.IsSqlServerAvailableAsync());
            await SqlServerTestHelper.CreateDatabaseAsync(databaseName);
        }
        else await connection.OpenAsync();

        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>();
            if (sqlServer) options.UseSqlServer(SqlServerTestHelper.GetTestDatabaseConnectionString(databaseName));
            else options.UseSqlite(connection);
            await using var db = new AppDbContext(options.Options);
            if (sqlServer)
            {
                var ids = db.Database.GetMigrations().ToArray();
                var catalogId = Assert.Single(ids, id => id.EndsWith("_AddMedicineCatalogData"));
                await db.GetService<IMigrator>().MigrateAsync(ids[Array.IndexOf(ids, catalogId) - 1]);
            }
            else
            {
                await db.Database.ExecuteSqlRawAsync("""
                    CREATE TABLE Medicines (Id INTEGER PRIMARY KEY AUTOINCREMENT, Code TEXT NOT NULL, Name TEXT NOT NULL,
                        Unit TEXT NOT NULL, StockQuantity INTEGER NOT NULL, ReorderLevel INTEGER NOT NULL,
                        UnitPrice TEXT NULL, IsActive INTEGER NOT NULL DEFAULT 1, RowVersion BLOB NULL,
                        CreatedAt TEXT NOT NULL, UpdatedAt TEXT NULL);
                    CREATE UNIQUE INDEX IX_Medicines_Code ON Medicines(Code);
                    """);
            }

            var createdAt = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
            for (var i = 1; i <= 12; i++)
            {
                var code = $"MED{i:00}";
                var name = $"Legacy medicine {i}";
                var price = i == 1 ? (decimal?)null : 1234m + i;
                await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Medicines(Code,Name,Unit,StockQuantity,ReorderLevel,UnitPrice,IsActive,CreatedAt) VALUES ({code},{name},{"Original unit"},{17 + i},{7 + i},{price},{i != 12},{createdAt});");
            }

            if (sqlServer) await db.Database.MigrateAsync();
            else
            {
                var assembly = db.GetService<IMigrationsAssembly>();
                var entry = Assert.Single(assembly.Migrations, m => m.Key.EndsWith("_AddMedicineCatalogData"));
                var migration = assembly.CreateMigration(entry.Value, db.Database.ProviderName!);
                var commands = db.GetService<IMigrationsSqlGenerator>().Generate(migration.UpOperations, db.GetService<IDesignTimeModel>().Model);
                foreach (var command in commands) await db.Database.ExecuteSqlRawAsync(command.CommandText);
            }

            var legacy = await db.Medicines.AsNoTracking().OrderBy(m => m.Code).ToListAsync();
            Assert.Equal(12, legacy.Count);
            Assert.All(legacy, m => { Assert.Null(m.CategoryId); Assert.True(m.IsPrescriptionRequired); });
            await DevelopmentDataSeeder.SeedMedicineCatalogAsync(db);
            db.ChangeTracker.Clear();
            string[] otcCodes = ["MED01", "MED03", "MED06", "MED09", "MED11", "MED12"];
            foreach (var original in legacy)
            {
                var medicine = await db.Medicines.SingleAsync(m => m.Code == original.Code);
                Assert.Equal(!otcCodes.Contains(medicine.Code), medicine.IsPrescriptionRequired);
                Assert.NotNull(medicine.CategoryId);
                Assert.Equal(original.Name, medicine.Name);
                Assert.Equal(original.Unit, medicine.Unit);
                Assert.Equal(original.UnitPrice, medicine.UnitPrice);
                Assert.Equal(original.StockQuantity, medicine.StockQuantity);
                Assert.Equal(original.ReorderLevel, medicine.ReorderLevel);
                Assert.Equal(original.IsActive, medicine.IsActive);
                Assert.Equal(original.CreatedAt, medicine.CreatedAt);
            }
            Assert.Equal(30, await db.Medicines.CountAsync());
            Assert.Equal(11, await db.MedicineCategories.CountAsync());
            var firstSeed = await SnapshotAsync(db);
            await DevelopmentDataSeeder.SeedMedicineCatalogAsync(db);
            db.ChangeTracker.Clear();
            Assert.Equal(firstSeed, await SnapshotAsync(db));

            // Both directions of a pharmacist override must survive later seeds.
            (await db.Medicines.SingleAsync(m => m.Code == "MED01")).IsPrescriptionRequired = true;
            (await db.Medicines.SingleAsync(m => m.Code == "MED02")).IsPrescriptionRequired = false;
            await db.SaveChangesAsync();
            var pharmacistChanges = await SnapshotAsync(db);
            await DevelopmentDataSeeder.SeedMedicineCatalogAsync(db);
            db.ChangeTracker.Clear();
            Assert.Equal(pharmacistChanges, await SnapshotAsync(db));
        }
        finally
        {
            if (sqlServer) await SqlServerTestHelper.DropDatabaseAsync(databaseName);
        }
    }

    private static async Task<string> SnapshotAsync(AppDbContext db)
    {
        // Compare every mapped scalar, including timestamps and SQL Server row versions.
        var medicines = await db.Medicines.AsNoTracking().OrderBy(m => m.Code).ToListAsync();
        var categories = await db.MedicineCategories.AsNoTracking().OrderBy(c => c.Name).ToListAsync();
        object Scalars(object entity) => db.Model.FindEntityType(entity.GetType())!.GetProperties()
            .OrderBy(p => p.Name).ToDictionary(p => p.Name, p => p.PropertyInfo!.GetValue(entity));
        return JsonSerializer.Serialize(new { Medicines = medicines.Select(Scalars), Categories = categories.Select(Scalars) });
    }
}
