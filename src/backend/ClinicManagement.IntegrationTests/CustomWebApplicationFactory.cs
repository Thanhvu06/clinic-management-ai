using System;
using System.Data.Common;
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

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
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

            // Add SQLite In-Memory DB
            services.AddSingleton<DbConnection>(container =>
            {
                var connection = new SqliteConnection("DataSource=SharedDb;Mode=Memory;Cache=Shared");
                connection.Open();
                return connection;
            });

            services.AddDbContext<AppDbContext>((container, options) =>
            {
                var connection = container.GetRequiredService<DbConnection>();
                options.UseSqlite(connection);
            });

            var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.EnsureCreated();

            // Replace AI Provider with Mock
            var aiProviderDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IAiSpecialtySuggestionProvider));
            if (aiProviderDescriptor != null) services.Remove(aiProviderDescriptor);
            
            services.AddSingleton<IAiSpecialtySuggestionProvider>(MockAiProvider.Object);
        });
    }
}
