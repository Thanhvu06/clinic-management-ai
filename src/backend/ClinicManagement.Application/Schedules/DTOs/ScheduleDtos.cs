using System.ComponentModel.DataAnnotations;

namespace ClinicManagement.Application.Schedules.DTOs;

public class WorkScheduleDto
{
    public long Id { get; set; }
    public long DoctorId { get; set; }
    public DateOnly WorkDate { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public bool IsActive { get; set; }
}

public class CreateWorkScheduleRequest
{
    [Required]
    public DateOnly WorkDate { get; set; }
    [Required]
    public TimeOnly StartTime { get; set; }
    [Required]
    public TimeOnly EndTime { get; set; }
}

public class UpdateWorkScheduleRequest
{
    [Required]
    public DateOnly WorkDate { get; set; }
    [Required]
    public TimeOnly StartTime { get; set; }
    [Required]
    public TimeOnly EndTime { get; set; }
}

public class UpdateWorkScheduleStatusRequest
{
    public bool IsActive { get; set; }
}
