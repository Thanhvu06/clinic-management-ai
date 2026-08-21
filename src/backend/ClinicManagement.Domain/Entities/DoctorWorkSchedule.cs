using System;

namespace ClinicManagement.Domain.Entities;

public class DoctorWorkSchedule
{
    public long Id { get; set; }
    public long DoctorId { get; set; }
    public DateOnly WorkDate { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public bool IsActive { get; set; } = true;

    public Doctor Doctor { get; set; } = null!;
}
