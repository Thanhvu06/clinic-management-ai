using System.Globalization;

namespace ClinicManagement.Infrastructure.AI;

public static class AiDisplayText
{
    public static string? Time(string? value) => TimeOnly.TryParse(value, CultureInfo.InvariantCulture, out var time)
        ? time.ToString("HH:mm", CultureInfo.InvariantCulture) : value;

    public static string? Date(string? value) => DateOnly.TryParse(value, CultureInfo.InvariantCulture, out var date)
        ? date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) : value;

    public static string PrescriptionCode(long id, DateTime createdAt) =>
        $"RX-{(createdAt.Year > 1900 ? createdAt.ToString("yyMMdd", CultureInfo.InvariantCulture) : "000000")}-{id:0000}";
}
