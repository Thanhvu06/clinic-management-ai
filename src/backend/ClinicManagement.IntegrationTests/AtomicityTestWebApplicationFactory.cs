using System;
using System.Data.Common;
using System.IO;
using System.Linq;
using ClinicManagement.Application.AI.Interfaces;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace ClinicManagement.IntegrationTests;

/// <summary>
/// A dedicated <see cref="WebApplicationFactory{TEntryPoint}"/> for transaction-atomicity tests.
/// It registers a <see cref="SaveFailureInterceptor"/> into the EF Core pipeline so that tests
/// can arm a controlled mid-transaction failure and then verify that zero partial state leaked
/// to the database.
/// </summary>
public sealed class AtomicityTestWebApplicationFactory : WebApplicationFactory<Program>
{
    public Mock<IAiSpecialtySuggestionProvider> MockAiProvider { get; } = new();

    /// <summary>The interceptor that can be armed per-test via <see cref="SaveFailureInterceptor.FailOnSaveNumber"/>.</summary>
    public SaveFailureInterceptor Interceptor { get; } = new SaveFailureInterceptor();

    private readonly string _dbFilePath = Path.Combine(
        Path.GetTempPath(), $"clinic_atomic_{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            // Remove existing DbContext registrations
            var descriptors = services.Where(d =>
                d.ServiceType.Name.Contains("DbContextOptions") ||
                d.ServiceType == typeof(DbConnection) ||
                d.ServiceType == typeof(AppDbContext)).ToList();

            foreach (var d in descriptors)
                services.Remove(d);

            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = _dbFilePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                DefaultTimeout = 15
            }.ToString();

            // Initialize SQLite with WAL mode
            using (var initConn = new SqliteConnection(connectionString))
            {
                initConn.Open();
                using var cmd = initConn.CreateCommand();
                cmd.CommandText = "PRAGMA journal_mode = WAL; PRAGMA busy_timeout = 15000;";
                cmd.ExecuteNonQuery();
            }

            // Create schema
            var initOptions = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connectionString)
                .Options;
            using (var initDb = new AppDbContext(initOptions))
            {
                initDb.Database.EnsureCreated();
            }

            // Register DbContext WITH the failure interceptor
            services.AddDbContext<AppDbContext>(options =>
            {
                options.UseSqlite(connectionString)
                       .AddInterceptors(Interceptor);
            });

            // Replace AI provider with mock
            var aiDesc = services.SingleOrDefault(d => d.ServiceType == typeof(IAiSpecialtySuggestionProvider));
            if (aiDesc != null) services.Remove(aiDesc);
            services.AddSingleton<IAiSpecialtySuggestionProvider>(MockAiProvider.Object);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            try
            {
                SqliteConnection.ClearAllPools();
                if (File.Exists(_dbFilePath)) File.Delete(_dbFilePath);
                var shm = $"{_dbFilePath}-shm";
                if (File.Exists(shm)) File.Delete(shm);
                var wal = $"{_dbFilePath}-wal";
                if (File.Exists(wal)) File.Delete(wal);
            }
            catch
            {
                // Best-effort cleanup
            }
        }
    }
}
