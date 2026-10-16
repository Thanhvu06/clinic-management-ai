using ClinicManagement.Infrastructure.Persistence;
using ClinicManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace ClinicManagement.IntegrationTests;

internal static class SqliteLegacyMigrationHelper
{
    public static IModel CatalogModelBeforeLaterColumns(AppDbContext db, string afterMigrationId)
    {
        var laterColumns = LaterMedicineColumns(db, afterMigrationId).Select(c => c.Name).Distinct().ToArray();
        using var catalog = new CatalogDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(db.Database.GetDbConnection()).Options, laterColumns);
        return catalog.GetService<IDesignTimeModel>().Model;
    }

    public static async Task ApplyLaterMedicineColumnsAsync(AppDbContext db, string afterMigrationId)
    {
        var assembly = db.GetService<IMigrationsAssembly>();
        var generator = db.GetService<IMigrationsSqlGenerator>();
        var model = db.GetService<IDesignTimeModel>().Model;
        foreach (var entry in assembly.Migrations.Where(m => string.CompareOrdinal(m.Key, afterMigrationId) > 0)
            .OrderBy(m => m.Key, StringComparer.Ordinal))
        {
            var migration = assembly.CreateMigration(entry.Value, db.Database.ProviderName!);
            var columns = migration.UpOperations.OfType<AddColumnOperation>().Where(o => o.Table == "Medicines").ToArray();
            foreach (var command in generator.Generate(columns, model))
                await db.Database.ExecuteSqlRawAsync(command.CommandText);
        }
    }

    private static IEnumerable<AddColumnOperation> LaterMedicineColumns(AppDbContext db, string afterMigrationId)
    {
        var assembly = db.GetService<IMigrationsAssembly>();
        return assembly.Migrations.Where(m => string.CompareOrdinal(m.Key, afterMigrationId) > 0)
            .OrderBy(m => m.Key, StringComparer.Ordinal)
            .SelectMany(m => assembly.CreateMigration(m.Value, db.Database.ProviderName!).UpOperations)
            .OfType<AddColumnOperation>().Where(o => o.Table == "Medicines");
    }

    private sealed class CatalogDbContext(DbContextOptions<AppDbContext> options, string[] laterColumns) : AppDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            // SQLite rebuilds foreign keys using the supplied model. Future columns
            // must not enter the catalog rebuild before their migrations add them.
            foreach (var column in laterColumns) builder.Entity<Medicine>().Ignore(column);
        }
    }
}
