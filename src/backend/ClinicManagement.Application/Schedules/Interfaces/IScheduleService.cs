using ClinicManagement.Application.Schedules.DTOs;

namespace ClinicManagement.Application.Schedules.Interfaces;

public interface IScheduleService
{
    Task<List<WorkScheduleDto>> GetDoctorWorkSchedulesAsync(long doctorId);
    Task<WorkScheduleDto> CreateWorkScheduleAsync(long doctorId, CreateWorkScheduleRequest request);
    Task<WorkScheduleDto> UpdateWorkScheduleAsync(long scheduleId, UpdateWorkScheduleRequest request);
    Task UpdateWorkScheduleStatusAsync(long scheduleId, UpdateWorkScheduleStatusRequest request);
    Task GenerateSlotsAsync(long scheduleId);
}
