using System;
using System.Collections.Generic;

namespace ClinicManagement.Domain.Entities;

public class AppointmentSlot
{
    public long Id { get; set; }
    public long DoctorId { get; set; }
    public DateOnly SlotDate { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public bool IsBooked { get; set; } = false;

    public Doctor Doctor { get; set; } = null!;
    public ICollection<Appointment> Appointments { get; set; } = new List<Appointment>();
}
