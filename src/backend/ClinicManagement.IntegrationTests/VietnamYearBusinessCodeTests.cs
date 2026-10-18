using System.Globalization;
using ClinicManagement.Application.Authentication.Interfaces;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.HealthPackages.DTOs;
using ClinicManagement.Application.HealthPackages.Interfaces;
using ClinicManagement.Application.Mpi.Interfaces;
using ClinicManagement.Infrastructure.Mpi;
using ClinicManagement.Infrastructure.Persistence;
using ClinicManagement.Infrastructure.Services;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ClinicManagement.IntegrationTests;

public class VietnamYearBusinessCodeTests(CustomWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    [Theory]
    [InlineData("2026-12-31T17:30:00Z", 2027)]
    [InlineData("2026-12-31T16:59:00Z", 2026)]
    public async Task MrnUsesVietnamCalendarYearAndPersistsThatYearsSequence(string utc, int expectedYear)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var clock = new FixedClock(utc);

        var mrn = await new MrnGenerator(db, clock).GenerateNextMrnAsync();

        Assert.Equal(expectedYear, clock.VietnamToday.Year);
        Assert.Equal($"BN-{expectedYear}-000001", mrn);
        var sequence = Assert.Single(await db.MrnSequences.AsNoTracking().ToListAsync());
        Assert.Equal(expectedYear, sequence.Year);
        Assert.Equal(1L, sequence.LastSequenceNumber);
    }

    [Fact]
    public async Task PackageRegistrationUsesVietnamDateAfterUtcDayBoundary()
    {
        var clock = new FixedClock("2026-10-18T18:00:00Z");
        using var app = Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IDateTimeProvider>();
            services.AddSingleton<IDateTimeProvider>(clock);
            services.RemoveAll<ICurrentUserService>();
            services.AddScoped<ICurrentUserService>(_ => new FixedCurrentUser(Patient1Id));
        }));
        using var scope = app.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IHealthPackageRegistrationService>();

        var registration = await service.RegisterPackageAsync(new CreatePackageRegistrationRequest
        {
            HealthPackageId = PackageEntityId,
            PreferredDate = new DateOnly(2026, 10, 19),
            ContactPhone = "0901234567"
        });

        Assert.Equal(new DateOnly(2026, 10, 19), clock.VietnamToday);
        Assert.StartsWith("REG-261019-", registration.RegistrationCode);
        Assert.Matches(@"^REG-261019-[0-9A-F]{7}$", registration.RegistrationCode);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var persisted = await db.HealthPackageRegistrations.AsNoTracking().SingleAsync(item => item.Id == registration.Id);
        Assert.Equal(registration.RegistrationCode, persisted.RegistrationCode);
    }

    [Fact]
    public async Task ApiResolvesScopedMrnGeneratorUsingRegisteredSingletonClock()
    {
        var clock = new FixedClock("2026-12-31T17:30:00Z");
        using var app = Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IDateTimeProvider>();
            services.AddSingleton<IDateTimeProvider>(clock);
        }));
        using var firstScope = app.Services.CreateScope();
        using var secondScope = app.Services.CreateScope();
        var generator = firstScope.ServiceProvider.GetRequiredService<IMrnGenerator>();

        Assert.IsType<MrnGenerator>(generator);
        Assert.Same(generator, firstScope.ServiceProvider.GetRequiredService<IMrnGenerator>());
        Assert.NotSame(generator, secondScope.ServiceProvider.GetRequiredService<IMrnGenerator>());
        Assert.Same(clock, firstScope.ServiceProvider.GetRequiredService<IDateTimeProvider>());
        Assert.Same(clock, secondScope.ServiceProvider.GetRequiredService<IDateTimeProvider>());
        var mrn = await generator.GenerateNextMrnAsync();
        Assert.Equal("BN-2027-000001", mrn);
        var db = firstScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1L, (await db.MrnSequences.AsNoTracking().SingleAsync(sequence => sequence.Year == 2027)).LastSequenceNumber);
    }

    private sealed class FixedCurrentUser(Guid userId) : ICurrentUserService
    {
        public Guid? UserId => userId;
    }

    private sealed class FixedClock(string utc) : IDateTimeProvider
    {
        private readonly DateTimeProvider _converter = new();
        public DateTime UtcNow { get; } = DateTime.Parse(utc, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
        public DateTime VietnamNow => ConvertUtcToVietnam(UtcNow);
        public DateOnly VietnamToday => DateOnly.FromDateTime(VietnamNow);
        public TimeOnly VietnamTime => TimeOnly.FromDateTime(VietnamNow);
        public TimeZoneInfo VietnamTimeZone => _converter.VietnamTimeZone;
        public DateTime ConvertUtcToVietnam(DateTime value) => _converter.ConvertUtcToVietnam(value);
        public DateTime ConvertVietnamToUtc(DateTime value) => _converter.ConvertVietnamToUtc(value);
    }
}
