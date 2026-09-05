using System;

namespace ClinicManagement.Domain.Entities;

public class AppointmentVitalSigns
{
    public long Id { get; set; }
    public long AppointmentId { get; set; }

    public decimal? Temperature { get; set; }
    public int? BloodPressureSystolic { get; set; }
    public int? BloodPressureDiastolic { get; set; }
    public int? HeartRate { get; set; }
    public int? RespiratoryRate { get; set; }
    public decimal? Weight { get; set; }
    public decimal? Height { get; set; }
    public decimal? Bmi { get; set; }
    public int? SpO2 { get; set; }

    public DateTime RecordedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid RecordedByUserId { get; set; }

    public byte[]? RowVersion { get; set; } = Guid.NewGuid().ToByteArray();

    public Appointment Appointment { get; set; } = null!;

    public static decimal? CalculateBmi(decimal? weightKg, decimal? heightCm)
    {
        if (!weightKg.HasValue || !heightCm.HasValue || heightCm.Value <= 0 || weightKg.Value <= 0)
            return null;

        var heightMeters = heightCm.Value / 100m;
        var bmi = weightKg.Value / (heightMeters * heightMeters);
        return Math.Round(bmi, 1);
    }
}
