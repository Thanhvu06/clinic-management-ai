using System.Text.RegularExpressions;
using ClinicManagement.Application.Notifications.DTOs;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Notifications;

internal static partial class NotificationDisplayCodes
{
    public static async Task ApplyAsync(AppDbContext db, IReadOnlyList<NotificationDto> items, CancellationToken ct)
    {
        var tokens = items.SelectMany(item => LegacyCode().Matches(item.Title + " " + item.Message))
            .Select(match => match.Value.ToUpperInvariant()).ToHashSet();
        if (tokens.Count == 0) return;

        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (tokens.Any(code => code.StartsWith("APT-", StringComparison.Ordinal)))
            foreach (var row in await db.Appointments.Where(x => x.ShortCode != null && tokens.Contains(x.AppointmentCode))
                .Select(x => new { x.AppointmentCode, x.ShortCode }).ToListAsync(ct))
                aliases[row.AppointmentCode] = row.ShortCode!;
        if (tokens.Any(code => code.StartsWith("VIS-", StringComparison.Ordinal)))
            foreach (var row in await db.PatientVisits.Where(x => x.ShortCode != null && tokens.Contains(x.VisitCode))
                .Select(x => new { x.VisitCode, x.ShortCode }).ToListAsync(ct))
                aliases[row.VisitCode] = row.ShortCode!;
        if (tokens.Any(code => code.StartsWith("INV-", StringComparison.Ordinal)))
            foreach (var row in await db.Invoices.Where(x => x.ShortCode != null && tokens.Contains(x.InvoiceCode))
                .Select(x => new { x.InvoiceCode, x.ShortCode }).ToListAsync(ct))
                aliases[row.InvoiceCode] = row.ShortCode!;
        if (tokens.Any(code => code.StartsWith("DX-", StringComparison.Ordinal)))
            foreach (var row in await db.DiagnosticOrders.Where(x => x.ShortCode != null && tokens.Contains(x.OrderCode))
                .Select(x => new { x.OrderCode, x.ShortCode }).ToListAsync(ct))
                aliases[row.OrderCode] = row.ShortCode!;
        if (tokens.Any(code => code.StartsWith("REG-", StringComparison.Ordinal)))
            foreach (var row in await db.HealthPackageRegistrations.Where(x => x.ShortCode != null && tokens.Contains(x.RegistrationCode))
                .Select(x => new { x.RegistrationCode, x.ShortCode }).ToListAsync(ct))
                aliases[row.RegistrationCode] = row.ShortCode!;

        foreach (var item in items)
        {
            item.Title = LegacyCode().Replace(item.Title, match => aliases.GetValueOrDefault(match.Value, match.Value));
            item.Message = LegacyCode().Replace(item.Message, match => aliases.GetValueOrDefault(match.Value, match.Value));
        }
    }

    [GeneratedRegex(@"\b(?:APT|VIS|INV|DX|REG)(?:-[A-Z0-9]+)+\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LegacyCode();
}
