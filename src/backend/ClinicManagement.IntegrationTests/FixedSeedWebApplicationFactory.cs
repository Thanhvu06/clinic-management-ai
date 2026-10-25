using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Infrastructure.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ClinicManagement.IntegrationTests;

internal sealed class FixedSeedWebApplicationFactory(DateTime? utcNow = null) : CustomWebApplicationFactory
{
    private static readonly DateTime WeekdayUtc = new(2026, 10, 27, 3, 0, 0, DateTimeKind.Utc);
    private readonly IDateTimeProvider clock = new SeedClock(utcNow ?? WeekdayUtc);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services => UseClock(services, clock));
    }

    internal static WebApplicationFactory<Program> WithClock(CustomWebApplicationFactory factory) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => UseClock(services, new SeedClock(WeekdayUtc))));

    private static void UseClock(IServiceCollection services, IDateTimeProvider clock)
    {
        services.RemoveAll<IDateTimeProvider>();
        services.AddSingleton(clock);
    }

    private sealed class SeedClock(DateTime utc) : IDateTimeProvider
    {
        private readonly DateTimeProvider converter = new();
        public DateTime UtcNow => utc;
        public DateTime VietnamNow => ConvertUtcToVietnam(utc);
        public DateOnly VietnamToday => DateOnly.FromDateTime(VietnamNow);
        public TimeOnly VietnamTime => TimeOnly.FromDateTime(VietnamNow);
        public TimeZoneInfo VietnamTimeZone => converter.VietnamTimeZone;
        public DateTime ConvertUtcToVietnam(DateTime value) => converter.ConvertUtcToVietnam(value);
        public DateTime ConvertVietnamToUtc(DateTime value) => converter.ConvertVietnamToUtc(value);
    }
}
