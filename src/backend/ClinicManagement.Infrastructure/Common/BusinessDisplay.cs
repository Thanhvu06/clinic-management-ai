using System.Globalization;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Infrastructure.Common;

public static class BusinessDisplay
{
    public static string Money(decimal amount) => $"{amount.ToString("N0", CultureInfo.GetCultureInfo("vi-VN"))} ₫";
    public static string PrescriptionCode(Prescription prescription, IDateTimeProvider clock) =>
        PrescriptionCode(prescription.Id, prescription.CreatedAt, clock);
    public static string PrescriptionCode(long id, DateTime createdAt, IDateTimeProvider clock) =>
        $"RX-{clock.ConvertUtcToVietnam(createdAt):yyMMdd}-{id:D4}";
    public static string RandomCode(string prefix, DateOnly date, int suffixLength) =>
        $"{prefix}-{date:yyMMdd}-{Guid.NewGuid().ToString("N")[..suffixLength].ToUpperInvariant()}";
}
