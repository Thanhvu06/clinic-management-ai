using System.Globalization;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Infrastructure.Common;

public static class BusinessDisplay
{
    public static string Money(decimal amount) => $"{amount.ToString("N0", CultureInfo.GetCultureInfo("vi-VN"))} ₫";
    public static string PrescriptionCode(Prescription prescription, IDateTimeProvider clock) =>
        $"RX-{clock.ConvertUtcToVietnam(prescription.CreatedAt):yyMMdd}-{prescription.Id:D4}";
    public static string RandomCode(string prefix, DateOnly date, int suffixLength) =>
        $"{prefix}-{date:yyMMdd}-{Guid.NewGuid().ToString("N")[..suffixLength].ToUpperInvariant()}";
}
