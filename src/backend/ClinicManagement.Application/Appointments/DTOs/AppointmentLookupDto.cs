using System;

namespace ClinicManagement.Application.Appointments.DTOs;

public class AppointmentLookupDto
{
    public string AppointmentCode { get; set; } = string.Empty;
    public DateOnly AppointmentDate { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public string SpecialtyName { get; set; } = string.Empty;
    public string DoctorName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string MaskedPatientName { get; set; } = string.Empty;
    public string MaskedPhoneNumber { get; set; } = string.Empty;
}
