using ClinicManagement.Application.Specialties.DTOs;

namespace ClinicManagement.Application.Doctors.DTOs;

public class DoctorDetailDto
{
    public long Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string AcademicTitle { get; set; } = string.Empty;
    public int ExperienceYears { get; set; }
    public string Description { get; set; } = string.Empty;
}

public class AvailableSlotDto
{
    public long SlotId { get; set; }
    public long DoctorId { get; set; }
    public DateOnly SlotDate { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
}
