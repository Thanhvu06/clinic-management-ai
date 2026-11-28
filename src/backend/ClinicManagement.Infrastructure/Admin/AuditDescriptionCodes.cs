using System.Text.RegularExpressions;
using ClinicManagement.Application.Admin.DTOs;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Infrastructure.AI;
using ClinicManagement.Infrastructure.Common;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Admin;

internal static partial class AuditDescriptionCodes
{
    public static async Task ApplyAsync(AppDbContext db, IDateTimeProvider clock, IReadOnlyList<SystemAuditLogDto> logs)
    {
        var codes = logs.SelectMany(log => LegacyCode().Matches(log.Description))
            .Select(match => match.Value.ToUpperInvariant()).ToLookup(code => code[..code.IndexOf('-')]);
        if (codes.Count == 0) return;

        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await AddAliasesAsync(codes["APT"], db.Appointments.Select(row => new Alias { Legacy = row.AppointmentCode, Short = row.ShortCode }), aliases);
        await AddAliasesAsync(codes["VIS"], db.PatientVisits.Select(row => new Alias { Legacy = row.VisitCode, Short = row.ShortCode }), aliases);
        await AddAliasesAsync(codes["INV"], db.Invoices.Select(row => new Alias { Legacy = row.InvoiceCode, Short = row.ShortCode }), aliases);
        await AddAliasesAsync(codes["PAY"], db.Payments.Select(row => new Alias { Legacy = row.PaymentCode, Short = row.ShortCode }), aliases);
        await AddAliasesAsync(codes["DX"], db.DiagnosticOrders.Select(row => new Alias { Legacy = row.OrderCode, Short = row.ShortCode }), aliases);
        await AddAliasesAsync(codes["REG"], db.HealthPackageRegistrations.Select(row => new Alias { Legacy = row.RegistrationCode, Short = row.ShortCode }), aliases);
        await AddAliasesAsync(codes["BN"], db.Patients.Select(row => new Alias { Legacy = row.MedicalRecordNumber, Short = row.ShortCode }), aliases);

        var prescriptionIds = codes["RX"].Select(DisplayCodeSearch.LegacyPrescriptionId).OfType<long>().Distinct().ToArray();
        if (prescriptionIds.Length > 0)
        {
            var rows = await db.Prescriptions.Where(row => row.ShortCode != null && prescriptionIds.Contains(row.Id))
                .Select(row => new { row.Id, row.CreatedAt, row.ShortCode }).ToListAsync();
            foreach (var row in rows)
            {
                aliases[BusinessDisplay.LegacyPrescriptionCode(row.Id, row.CreatedAt, clock)] = row.ShortCode!;
                aliases[AiDisplayText.PrescriptionCode(row.Id, row.CreatedAt)] = row.ShortCode!;
            }
        }

        foreach (var log in logs)
            log.Description = LegacyCode().Replace(log.Description, match => aliases.GetValueOrDefault(match.Value, match.Value));
    }

    private static async Task AddAliasesAsync(IEnumerable<string> tokens, IQueryable<Alias> query, Dictionary<string, string> aliases)
    {
        var codes = tokens.Distinct().ToArray();
        if (codes.Length == 0) return;
        foreach (var row in await query.Where(row => row.Short != null && codes.Contains(row.Legacy.ToUpper())).ToListAsync())
            aliases[row.Legacy] = row.Short!;
    }

    private sealed class Alias
    {
        public required string Legacy { get; init; }
        public string? Short { get; init; }
    }

    [GeneratedRegex(@"\b(?:(?:APT|VIS|INV|PAY|RX|DX|REG)(?:-[A-Z0-9]+)+|BN-[0-9]{4}-[0-9]+)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LegacyCode();
}
