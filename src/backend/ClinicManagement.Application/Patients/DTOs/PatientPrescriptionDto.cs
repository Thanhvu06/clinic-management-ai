using System;
using System.Collections.Generic;

namespace ClinicManagement.Application.Patients.DTOs;

public class PatientPrescriptionDto
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public long AppointmentId { get; set; }
    public string AppointmentCode { get; set; } = string.Empty;
    public DateOnly AppointmentDate { get; set; }
    public string DoctorName { get; set; } = string.Empty;
    public string SpecialtyName { get; set; } = string.Empty;
    public string Diagnosis { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? DispensedAt { get; set; }
    public List<PatientPrescriptionItemDto> Items { get; set; } = new();
}

public class PatientPrescriptionItemDto
{
    public long MedicineId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public string Dosage { get; set; } = string.Empty;
    public string Frequency { get; set; } = string.Empty;
    public int? DurationDays { get; set; }
    public string? Instructions { get; set; }
}
