using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using ClinicManagement.Application.Authentication.Interfaces;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Pharmacy.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Identity;
using ClinicManagement.Infrastructure.Persistence;
using ClinicManagement.Infrastructure.Pharmacy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace ClinicManagement.IntegrationTests;

public class AuditAuthAndPharmacyTests : IntegrationTestBase
{
    public AuditAuthAndPharmacyTests(CustomWebApplicationFactory factory) : base(factory) { }

    [Theory]
    [InlineData("StockIn", 0)] [InlineData("StockIn", -1)]
    [InlineData("Adjustment", 0)] [InlineData("Dispense", 1)]
    [InlineData("Reservation", -1)] [InlineData("Initial", 1)]
    [InlineData("ReservationCancelled", 1)] [InlineData("999", 1)]
    public async Task B2_Invalid_manual_stock_change_does_not_mutate_stock(string type, int quantity)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var med = new Medicine { Code = $"A-{Guid.NewGuid():N}"[..18], Name = "Audit stock", Unit = "Viên", StockQuantity = 100, IsActive = true };
        db.Medicines.Add(med);
        await db.SaveChangesAsync();
        var service = scope.ServiceProvider.GetRequiredService<IPharmacyService>();
        await Assert.ThrowsAsync<ClinicManagement.Application.Common.Exceptions.BusinessException>(() => service.AdjustStockAsync(new ClinicManagement.Application.Pharmacy.DTOs.AdjustStockDto { MedicineId = med.Id, Type = Enum.Parse<MedicineStockTransactionType>(type), Quantity = quantity }));
        await db.Entry(med).ReloadAsync();
        Assert.Equal(100, med.StockQuantity);
        Assert.False(await db.MedicineStockTransactions.AnyAsync(t => t.MedicineId == med.Id));
    }

    [Theory]
    [InlineData("StockIn", 1, 101)] [InlineData("Adjustment", -1, 99)] [InlineData("Adjustment", 2, 102)]
    public async Task B2_Allowed_manual_stock_signs_are_preserved(string type, int quantity, int expected)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var med = new Medicine { Code = $"A-{Guid.NewGuid():N}"[..18], Name = "Audit stock", Unit = "Viên", StockQuantity = 100, IsActive = true };
        db.Medicines.Add(med);
        await db.SaveChangesAsync();
        using var pharm = await CreateAuthenticatedClientAsync("pharm@test.com");
        Assert.Equal(HttpStatusCode.OK, (await pharm.PostAsJsonAsync("/api/v1/pharmacy/inventory/adjust", new { medicineId = med.Id, type = (int)Enum.Parse<MedicineStockTransactionType>(type), quantity })).StatusCode);
        await db.Entry(med).ReloadAsync();
        Assert.Equal(expected, med.StockQuantity);
    }

    [Theory]
    [InlineData("invalid-operation", false)] [InlineData("connection-message", false)]
    [InlineData("sqlite-constraint", false)] [InlineData("sqlite-busy", true)]
    [InlineData("sqlite-locked", true)] [InlineData("ef-concurrency", true)]
    public void B3_Only_typed_concurrency_and_lock_errors_are_classified(string kind, bool expected)
    {
        Exception error = kind switch
        {
            "invalid-operation" => new InvalidOperationException("Invalid transaction state"),
            "connection-message" => new Exception("connection locked transaction busy deadlock concurrency"),
            "sqlite-constraint" => new DbUpdateException("Save failed", new SqliteException("constraint", 19)),
            "sqlite-busy" => new DbUpdateException("Save failed", new SqliteException("busy", 5)),
            "sqlite-locked" => new SqliteException("locked", 6),
            _ => new DbUpdateConcurrencyException()
        };
        var method = typeof(PharmacyService).GetMethod("IsConcurrencyOrLockException", BindingFlags.Static | BindingFlags.NonPublic)!;
        Assert.Equal(expected, (bool)method.Invoke(null, new object[] { error })!);
    }

    [Theory]
    [InlineData("prescriptions", 0, 1000, 1, 100)]
    [InlineData("prescriptions", -1, 0, 1, 10)]
    [InlineData("inventory-transactions", 0, 1000, 1, 100)]
    [InlineData("inventory-transactions", -1, -1, 1, 10)]
    public async Task B6_Pharmacy_pagination_is_bounded(string endpoint, int page, int size, int expectedPage, int expectedSize)
    {
        using var pharm = await CreateAuthenticatedClientAsync("pharm@test.com");
        var response = await pharm.GetAsync($"/api/v1/pharmacy/{endpoint}?page={page}&pageSize={size}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("data");
        Assert.Equal(expectedPage, data.GetProperty("page").GetInt32());
        Assert.Equal(expectedSize, data.GetProperty("pageSize").GetInt32());
    }

    [Fact]
    public async Task B5_Dispensed_today_uses_vietnam_midnight_and_excludes_tomorrow()
    {
        var utc = new DateTime(2030, 1, 1, 17, 10, 0, DateTimeKind.Utc); // 00:10 Vietnam Jan 2
        var clock = new Mock<IDateTimeProvider>();
        clock.SetupGet(x => x.UtcNow).Returns(utc);
        clock.SetupGet(x => x.VietnamToday).Returns(new DateOnly(2030, 1, 2));
        clock.Setup(x => x.ConvertVietnamToUtc(It.IsAny<DateTime>())).Returns((DateTime value) => DateTime.SpecifyKind(value.AddHours(-7), DateTimeKind.Utc));
        using var factory = Factory.WithWebHostBuilder(builder => builder.ConfigureServices(services => services.AddSingleton(clock.Object)));
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // This factory shares the class fixture's SQLite database.
        foreach (var time in new[] { utc.AddMinutes(-11), utc.AddMinutes(-5), utc.AddDays(1) })
            db.Prescriptions.Add(new Prescription { PatientId = Patient1EntityId, DoctorId = DoctorEntityId, Status = PrescriptionStatus.Dispensed, CreatedAt = time, DispensedAt = time });
        await db.SaveChangesAsync();
        Assert.Equal(1, (await scope.ServiceProvider.GetRequiredService<IPharmacyService>().GetDashboardStatsAsync()).DispensedTodayCount);
    }

    [Fact]
    public async Task A6_Five_failed_logins_lock_for_fifteen_minutes_with_generic_message()
    {
        var email = $"audit-{Guid.NewGuid():N}@test.com";
        Guid id;
        using (var scope = Factory.Services.CreateScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { UserName = email, Email = email, FullName = "Audit", PhoneNumber = $"09{Random.Shared.Next(10000000, 99999999)}", IsActive = true };
            Assert.True((await manager.CreateAsync(user, "Pass@123")).Succeeded);
            id = user.Id;
        }
        for (var i = 0; i < 5; i++)
        {
            var failed = await Client.PostAsJsonAsync("/api/v1/auth/login", new { emailOrPhone = email, password = "Wrong@123" });
            Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode);
            Assert.Equal("Email/Số điện thoại hoặc mật khẩu không chính xác.", JsonDocument.Parse(await failed.Content.ReadAsStringAsync()).RootElement.GetProperty("message").GetString());
        }
        var correct = await Client.PostAsJsonAsync("/api/v1/auth/login", new { emailOrPhone = email, password = "Pass@123" });
        Assert.Equal(HttpStatusCode.Unauthorized, correct.StatusCode);
        Assert.Equal("Email/Số điện thoại hoặc mật khẩu không chính xác.", JsonDocument.Parse(await correct.Content.ReadAsStringAsync()).RootElement.GetProperty("message").GetString());
        using var verify = Factory.Services.CreateScope();
        var locked = await verify.ServiceProvider.GetRequiredService<AppDbContext>().Users.FindAsync(id);
        Assert.InRange((locked!.LockoutEnd!.Value - DateTimeOffset.UtcNow).TotalMinutes, 14, 15);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task A6_Token_is_rejected_after_user_deactivation_or_deletion(bool delete)
    {
        var email = $"audit-{Guid.NewGuid():N}@test.com";
        Guid id;
        using (var scope = Factory.Services.CreateScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { UserName = email, Email = email, FullName = "Audit", PhoneNumber = $"09{Random.Shared.Next(10000000, 99999999)}", IsActive = true };
            Assert.True((await manager.CreateAsync(user, "Pass@123")).Succeeded);
            await manager.AddToRoleAsync(user, "Receptionist");
            id = user.Id;
        }
        using var client = await CreateAuthenticatedClientAsync(email);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/reception/billing/kpi")).StatusCode);
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = (await db.Users.FindAsync(id))!;
            if (delete) db.Users.Remove(user); else user.IsActive = false;
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/reception/billing/kpi")).StatusCode);
    }

    [Theory]
    [InlineData("login")] [InlineData("register")] [InlineData("forgot-password")] [InlineData("reset-password")]
    public async Task A6_All_four_auth_endpoints_are_rate_limited(string endpoint)
    {
        using var factory = Factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> { ["AuthRateLimiting:TestingPermitLimit"] = "2" })));
        using var client = factory.CreateClient();
        for (var i = 0; i < 2; i++) Assert.NotEqual(HttpStatusCode.TooManyRequests, (await client.PostAsJsonAsync($"/api/v1/auth/{endpoint}", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsJsonAsync($"/api/v1/auth/{endpoint}", new { })).StatusCode);
    }

    [Theory]
    [InlineData("Production", null)] [InlineData("Demo", "short")]
    [InlineData("Production", "ClinicCareDevelopmentSecretKey2026MustBeAtLeast32BytesLong!")]
    public void A6_Unsafe_jwt_key_fails_startup(string environment, string? key)
    {
        using var factory = new JwtStartupFactory(environment, key, "60");
        var error = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.Contains("Jwt:Key", error.Message);
    }

    [Fact]
    public void A6_Missing_expiry_fails_startup_clearly()
    {
        using var factory = new JwtStartupFactory("Testing", "AuditSecureSecretKeyAtLeastThirtyTwoBytesLong!", null);
        var error = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.Contains("Jwt:ExpiryMinutes", error.Message);
    }

    private sealed class JwtStartupFactory(string environment, string? key, string? expiry) : CustomWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseEnvironment(environment);
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:Key"] = key, ["Jwt:ExpiryMinutes"] = expiry }));
        }
    }
}
