using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using ClinicManagement.Application.Appointments.DTOs;

namespace ClinicManagement.Application.Appointments.DTOs.Doctor;

public class DoctorDashboardDto
{
    public DateOnly TodayDate { get; set; }
    public int TotalToday { get; set; }
    public int CheckedInCount { get; set; }
    public int InConsultationCount { get; set; }
    public int CompletedTodayCount { get; set; }
    public int NoShowTodayCount { get; set; }
    public string CurrentShift { get; set; } = string.Empty;
    public DoctorQueueItemDto? NextPatient { get; set; }
    public List<DoctorQueueItemDto> Queue { get; set; } = new();
    public List<DoctorQueueItemDto> UpcomingAppointments { get; set; } = new();
}

public class DoctorQueueItemDto
{
    public long AppointmentId { get; set; }
    public string AppointmentCode { get; set; } = string.Empty;
    public int QueueOrder { get; set; }
    public DateOnly AppointmentDate { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public long PatientId { get; set; }
    public string PatientName { get; set; } = string.Empty;
    public string PatientPhone { get; set; } = string.Empty;
    public string PatientGender { get; set; } = string.Empty;
    public DateOnly? PatientDob { get; set; }
    public int? PatientAge { get; set; }
    public string? Reason { get; set; }
    public string Status { get; set; } = string.Empty;
    public string SpecialtyName { get; set; } = string.Empty;
    public bool IsVitalsRecorded { get; set; }
    public string? VitalSummaryText { get; set; }
    public string? ChiefComplaint { get; set; }
}

public class DoctorScheduleDayDto
{
    public DateOnly Date { get; set; }
    public string DayOfWeekName { get; set; } = string.Empty;
    public List<DoctorShiftDto> Shifts { get; set; } = new();
}

public class DoctorShiftDto
{
    public long ScheduleId { get; set; }
    public string ShiftName { get; set; } = string.Empty;
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public bool IsActive { get; set; }
    public int TotalSlots { get; set; }
    public int BookedSlots { get; set; }
    public int AvailableSlots { get; set; }
    public List<DoctorSlotDetailDto> Slots { get; set; } = new();
}

public class DoctorSlotDetailDto
{
    public long SlotId { get; set; }
    public DateOnly SlotDate { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public bool IsBooked { get; set; }
    public long? AppointmentId { get; set; }
    public string? AppointmentCode { get; set; }
    public string? PatientName { get; set; }
    public string? Status { get; set; }
}

public class ClinicalEncounterDto
{
    public long Id { get; set; }
    public long AppointmentId { get; set; }
    public long DoctorId { get; set; }
    public string DoctorName { get; set; } = string.Empty;
    public string? ChiefComplaint { get; set; }
    public string? ClinicalFindings { get; set; }
    public string? Diagnosis { get; set; }
    public string? DiagnosisCode { get; set; }
    public string? TreatmentPlan { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string? FollowUpInstruction { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string? RowVersion { get; set; }
}

public class SaveEncounterRequest
{
    public string? ChiefComplaint { get; set; }
    public string? ClinicalFindings { get; set; }
    public string? Diagnosis { get; set; }
    public string? DiagnosisCode { get; set; }
    public string? TreatmentPlan { get; set; }
    public string? Summary { get; set; }
    public string? FollowUpInstruction { get; set; }
    public string? RowVersion { get; set; }
}

public class VitalSignsDto
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
    public DateTime RecordedAtUtc { get; set; }
    public string RecordedByUserName { get; set; } = string.Empty;
    public string? RowVersion { get; set; }
}

public class SaveVitalSignsRequest
{
    public decimal? Temperature { get; set; }
    public int? BloodPressureSystolic { get; set; }
    public int? BloodPressureDiastolic { get; set; }
    public int? HeartRate { get; set; }
    public int? RespiratoryRate { get; set; }
    public decimal? Weight { get; set; }
    public decimal? Height { get; set; }
    public int? SpO2 { get; set; }
    public string? RowVersion { get; set; }
}

public class PrescriptionDraftDto
{
    public long Id { get; set; }
    public long AppointmentId { get; set; }
    public long DoctorId { get; set; }
    public string DoctorName { get; set; } = string.Empty;
    public long PatientId { get; set; }
    public string PatientName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? RowVersion { get; set; }
    public List<PrescriptionDraftItemDto> Items { get; set; } = new();
}

public class PrescriptionDraftItemDto
{
    public long MedicineId { get; set; }
    public string MedicineCode { get; set; } = string.Empty;
    public string MedicineName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public int AvailableStock { get; set; }
    public string? Dosage { get; set; }
    public string? Frequency { get; set; }
    public int? DurationDays { get; set; }
    public string? Instructions { get; set; }
}

public class SavePrescriptionDraftRequest
{
    public string? Notes { get; set; }
    public string? RowVersion { get; set; }
    public List<SavePrescriptionItemRequest> Items { get; set; } = new();
}

public class SavePrescriptionItemRequest
{
    [Required]
    public long MedicineId { get; set; }

    [Range(1, 1000)]
    public int Quantity { get; set; }

    [MaxLength(200)]
    public string? Dosage { get; set; }

    [MaxLength(200)]
    public string? Frequency { get; set; }

    [Range(1, 365)]
    public int? DurationDays { get; set; }

    [MaxLength(500)]
    public string? Instructions { get; set; }
}

public class CompleteConsultationRequest
{
    public string? ChiefComplaint { get; set; }
    public string? ClinicalFindings { get; set; }
    public string? Diagnosis { get; set; }
    public string? DiagnosisCode { get; set; }
    public string? TreatmentPlan { get; set; }
    public string? Summary { get; set; }
    public string? FollowUpInstruction { get; set; }
    public string? EncounterRowVersion { get; set; }

    public bool IssuePrescription { get; set; }
    public string? PrescriptionNotes { get; set; }
    public string? PrescriptionRowVersion { get; set; }
    public List<SavePrescriptionItemRequest>? PrescriptionItems { get; set; }
}

public class PatientClinicalContextDto
{
    public long PatientId { get; set; }
    public string PatientName { get; set; } = string.Empty;
    public string PatientPhone { get; set; } = string.Empty;
    public string PatientGender { get; set; } = string.Empty;
    public DateOnly? PatientDob { get; set; }
    public string? Address { get; set; }
    public int TotalPastVisits { get; set; }
    public List<PastVisitSummaryDto> PastVisits { get; set; } = new();
    public DoctorAppointmentDto CurrentAppointment { get; set; } = null!;
    public VitalSignsDto? VitalSigns { get; set; }
    public VitalSignsDto? LatestKnownVitals { get; set; }
    public ClinicalEncounterDto? Encounter { get; set; }
    public PrescriptionDraftDto? Prescription { get; set; }
    public List<PatientVitalHistoryItemDto> VitalHistory { get; set; } = new();
    public AnthropometricComparisonDto? AnthropometricComparison { get; set; }
}

public class PatientVitalHistoryItemDto
{
    public long AppointmentId { get; set; }
    public string AppointmentCode { get; set; } = string.Empty;
    public DateOnly AppointmentDate { get; set; }
    public DateTime RecordedAtUtc { get; set; }
    public decimal? Height { get; set; }
    public decimal? Weight { get; set; }
    public decimal? Bmi { get; set; }
    public decimal? Temperature { get; set; }
    public int? BloodPressureSystolic { get; set; }
    public int? BloodPressureDiastolic { get; set; }
    public int? HeartRate { get; set; }
    public int? RespiratoryRate { get; set; }
    public int? SpO2 { get; set; }
    public string RecordedByUserName { get; set; } = string.Empty;
}

public class AnthropometricComparisonDto
{
    public PatientVitalHistoryItemDto? CurrentMeasurement { get; set; }
    public PatientVitalHistoryItemDto? PreviousMeasurement { get; set; }
    public decimal? WeightDeltaKg { get; set; }
    public decimal? HeightDeltaCm { get; set; }
    public decimal? BmiDelta { get; set; }
    public bool HasComparableData { get; set; }
}

public class PastVisitSummaryDto
{
    public long AppointmentId { get; set; }
    public string AppointmentCode { get; set; } = string.Empty;
    public DateOnly Date { get; set; }
    public string DoctorName { get; set; } = string.Empty;
    public string SpecialtyName { get; set; } = string.Empty;
    public string? Diagnosis { get; set; }
    public string? Summary { get; set; }
    public List<string> PrescriptionItemNames { get; set; } = new();
}
