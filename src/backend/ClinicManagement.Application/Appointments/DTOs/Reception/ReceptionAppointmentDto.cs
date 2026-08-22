using System;
using ClinicManagement.Application.Appointments.DTOs;

namespace ClinicManagement.Application.Appointments.DTOs.Reception;

public class ReceptionAppointmentDto : AppointmentDto
{
    public string PatientName { get; set; } = string.Empty;
    public string PatientPhone { get; set; } = string.Empty;
    public string DoctorName { get; set; } = string.Empty;
    public string SpecialtyName { get; set; } = string.Empty;
}
