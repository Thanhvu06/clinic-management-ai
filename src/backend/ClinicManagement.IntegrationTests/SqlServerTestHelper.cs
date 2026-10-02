using System;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using ClinicManagement.Infrastructure.Persistence;

namespace ClinicManagement.IntegrationTests;

public static class SqlServerTestHelper
{
    public static string? GetMasterConnectionString()
    {
        var envConn = Environment.GetEnvironmentVariable("TEST_SQLSERVER_CONNECTION_STRING");
        if (!string.IsNullOrWhiteSpace(envConn))
        {
            var builder = new SqlConnectionStringBuilder(envConn)
            {
                InitialCatalog = "master",
                ConnectTimeout = 15
            };
            return builder.ConnectionString;
        }

        // LocalDB fallback for Windows local environments
        return "Server=(localdb)\\mssqllocaldb;Database=master;Trusted_Connection=True;TrustServerCertificate=True;Connect Timeout=5;";
    }

    public static async Task<bool> IsSqlServerAvailableAsync()
    {
        try
        {
            var connStr = GetMasterConnectionString();
            if (string.IsNullOrWhiteSpace(connStr)) return false;

            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1";
            var result = await cmd.ExecuteScalarAsync();
            return result != null;
        }
        catch
        {
            return false;
        }
    }

    public static string GetTestDatabaseConnectionString(string dbName)
    {
        var envConn = Environment.GetEnvironmentVariable("TEST_SQLSERVER_CONNECTION_STRING");
        if (!string.IsNullOrWhiteSpace(envConn))
        {
            var builder = new SqlConnectionStringBuilder(envConn)
            {
                InitialCatalog = dbName,
                ConnectTimeout = 30
            };
            return builder.ConnectionString;
        }

        return $"Server=(localdb)\\mssqllocaldb;Database={dbName};Trusted_Connection=True;TrustServerCertificate=True;Connect Timeout=15;MultipleActiveResultSets=true;";
    }

    public static async Task CreateDatabaseAsync(string dbName)
    {
        var masterConn = GetMasterConnectionString();
        await using var conn = new SqlConnection(masterConn);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            IF DB_ID('{dbName}') IS NOT NULL
            BEGIN
                ALTER DATABASE [{dbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{dbName}];
            END;
            CREATE DATABASE [{dbName}];";
        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task DropDatabaseAsync(string dbName)
    {
        try
        {
            SqlConnection.ClearAllPools();
            var masterConn = GetMasterConnectionString();
            await using var conn = new SqlConnection(masterConn);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                IF DB_ID('{dbName}') IS NOT NULL
                BEGIN
                    ALTER DATABASE [{dbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{dbName}];
                END;";
            await cmd.ExecuteNonQueryAsync();
        }
        catch
        {
            // Best effort cleanup
        }
    }

    public static DbContextOptions<AppDbContext> CreateSqlServerOptions(string connectionString)
    {
        return new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .EnableSensitiveDataLogging()
            .Options;
    }
}
