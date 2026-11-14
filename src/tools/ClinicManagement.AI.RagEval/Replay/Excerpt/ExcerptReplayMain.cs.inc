using ClinicManagement.Application.Common.Interfaces;
using Microsoft.Extensions.Hosting;

namespace ClinicManagement.AI.RagEval;

internal static class HistoricalExcerptReplay
{
    public static async Task<int> Main(string[] args)
    {
        if (args.FirstOrDefault() != "excerpt-replay") return 1;
        try { return await ExcerptReplayCommand.RunAsync(args[1..], Console.Out); }
        catch (Exception)
        {
            await Console.Error.WriteLineAsync("Excerpt replay failed. Check workbook structure, pinned local bge-m3 and the new output path.");
            return 1;
        }
    }
}

// BlindAnswerRuntime constructs its clock locally. Use the recorded run time for date-dependent policies.
internal sealed class DateTimeProvider : IDateTimeProvider
{
    private readonly ClinicManagement.Infrastructure.Services.DateTimeProvider _clock = new();
    public DateTime UtcNow { get; } = new DateTimeOffset(2026, 11, 10, 18, 55, 34, TimeSpan.FromHours(7)).UtcDateTime;
    public TimeZoneInfo VietnamTimeZone => _clock.VietnamTimeZone;
    public DateTime VietnamNow => ConvertUtcToVietnam(UtcNow);
    public DateOnly VietnamToday => DateOnly.FromDateTime(VietnamNow);
    public TimeOnly VietnamTime => TimeOnly.FromDateTime(VietnamNow);
    public DateTime ConvertUtcToVietnam(DateTime value) => _clock.ConvertUtcToVietnam(value);
    public DateTime ConvertVietnamToUtc(DateTime value) => _clock.ConvertVietnamToUtc(value);
}

internal sealed class EvaluationEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = "Development";
    public string ApplicationName { get; set; } = "RagEval";
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
        new Microsoft.Extensions.FileProviders.NullFileProvider();
}
