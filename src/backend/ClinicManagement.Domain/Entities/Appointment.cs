using System;
using System.Collections.Generic;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.Entities;

public class Appointment
{
    public long Id { get; set; }
    public string AppointmentCode { get; set; } = string.Empty;
    public long PatientId { get; set; }
    public long DoctorId { get; set; }
    public long SpecialtyId { get; set; }
    public long AppointmentSlotId { get; set; }
    public DateOnly AppointmentDate { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public string? Reason { get; set; }
    public AppointmentStatus Status { get; set; } = AppointmentStatus.Pending;

    public Patient Patient { get; set; } = null!;
    public Doctor Doctor { get; set; } = null!;
    public Specialty Specialty { get; set; } = null!;
    public AppointmentSlot AppointmentSlot { get; set; } = null!;
    
    public ICollection<AppointmentHistory> Histories { get; set; } = new List<AppointmentHistory>();
    public ICollection<AppointmentChangeRequest> ChangeRequests { get; set; } = new List<AppointmentChangeRequest>();
}
