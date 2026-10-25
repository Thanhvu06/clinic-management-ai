using System.ComponentModel.DataAnnotations;

namespace ClinicManagement.Application.Common.Expiry;

public sealed class ExpiryRulesOptions
{
    [Range(1, 1440)] public int IntervalMinutes { get; set; } = 10;
    [Range(1, 1000)] public int BatchSize { get; set; } = 100;
    [Range(1, 1440)] public int AppointmentGraceMinutes { get; set; } = 30;
    [Range(1, 8760)] public int DiagnosticHours { get; set; } = 24;
    [Range(1, 365)] public int PrescriptionDays { get; set; } = 5;
    [Range(1, 365)] public int InvoiceOverdueDays { get; set; } = 3;
    [Range(1, 365)] public int RevisitDays { get; set; } = 7;
    [Range(1, 100)] public int NoShowThreshold { get; set; } = 3;
    [Range(1, 3650)] public int NoShowWindowDays { get; set; } = 90;
    [Range(1, 365)] public int OnlineBookingBlockDays { get; set; } = 30;
}
