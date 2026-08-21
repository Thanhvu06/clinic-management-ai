using System;
using System.Collections.Generic;

namespace ClinicManagement.Domain.Entities;

public class Doctor
{
    public long Id { get; set; }
    public Guid UserId { get; set; }
    public string? AcademicTitle { get; set; }
    public int ExperienceYears { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; }

    public ICollection<DoctorSpecialty> DoctorSpecialties { get; set; } = new List<DoctorSpecialty>();
    public ICollection<DoctorWorkSchedule> WorkSchedules { get; set; } = new List<DoctorWorkSchedule>();
    public ICollection<DoctorLeaveRequest> LeaveRequests { get; set; } = new List<DoctorLeaveRequest>();
    public ICollection<AppointmentSlot> AppointmentSlots { get; set; } = new List<AppointmentSlot>();
    public ICollection<Appointment> Appointments { get; set; } = new List<Appointment>();
}
