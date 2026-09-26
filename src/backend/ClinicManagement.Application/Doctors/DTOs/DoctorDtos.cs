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

public class DoctorAvailabilityDto
{
    public long DoctorId { get; set; }
    public string DoctorName { get; set; } = string.Empty;
    public string AcademicTitle { get; set; } = string.Empty;
    public string Timezone { get; set; } = "Asia/Ho_Chi_Minh";
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
    public List<DoctorDayAvailabilityDto> Days { get; set; } = new();
}

public class DoctorDayAvailabilityDto
{
    public DateOnly Date { get; set; }
    public string DayOfWeek { get; set; } = string.Empty;
    public bool HasWorkSchedule { get; set; }
    public List<ScheduleBlockDto> ScheduleBlocks { get; set; } = new();
    public List<AvailableSlotDto> AvailableSlots { get; set; } = new();
}

public class ScheduleBlockDto
{
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
}
