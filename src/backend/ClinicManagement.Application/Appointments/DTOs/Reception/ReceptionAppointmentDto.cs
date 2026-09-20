using System;
using ClinicManagement.Application.Appointments.DTOs;

namespace ClinicManagement.Application.Appointments.DTOs.Reception;

public class ReceptionAppointmentDto : AppointmentDto
{
    public string PatientName { get; set; } = string.Empty;
    public string PatientPhone { get; set; } = string.Empty;
    public string MedicalRecordNumber { get; set; } = string.Empty;
    public string NationalId { get; set; } = string.Empty;
    public long? PatientVisitId { get; set; }
    public long? FacilityId { get; set; }
    public string? FacilityName { get; set; }
}
