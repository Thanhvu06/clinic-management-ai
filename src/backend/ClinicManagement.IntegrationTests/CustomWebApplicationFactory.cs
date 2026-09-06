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

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    public Mock<IAiSpecialtySuggestionProvider> MockAiProvider { get; } = new();
    private readonly string _dbFilePath = Path.Combine(Path.GetTempPath(), $"clinic_test_{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            var descriptors = services.Where(d => 
                d.ServiceType.Name.Contains("DbContextOptions") || 
                d.ServiceType == typeof(DbConnection) ||
                d.ServiceType == typeof(AppDbContext)).ToList();
            
            foreach (var d in descriptors)
            {
                services.Remove(d);
            }

            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = _dbFilePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                DefaultTimeout = 15
            }.ToString();

            // Configure SQLite with WAL mode on creation
            using (var initConn = new SqliteConnection(connectionString))
            {
                initConn.Open();
                using var cmd = initConn.CreateCommand();
                cmd.CommandText = "PRAGMA journal_mode = WAL; PRAGMA busy_timeout = 15000;";
                cmd.ExecuteNonQuery();
            }

            // Create schema directly on the SQLite database without building DI container
            var initOptions = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connectionString)
                .Options;
            using (var initDb = new AppDbContext(initOptions))
            {
                initDb.Database.EnsureCreated();
            }

            services.AddDbContext<AppDbContext>(options =>
            {
                options.UseSqlite(connectionString);
            });

            // Replace AI Provider with Mock
            var aiProviderDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IAiSpecialtySuggestionProvider));
            if (aiProviderDescriptor != null) services.Remove(aiProviderDescriptor);
            
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
                // Best-effort cleanup of temp SQLite files
            }
        }
    }
}
