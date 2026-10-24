using ClinicManagement.Application.Appointments.DTOs.Doctor;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Infrastructure.Appointments;

internal static class DoctorDashboardShifts
{
    private static string ShiftName(DoctorWorkSchedule schedule) =>
        schedule.StartTime < new TimeOnly(12, 0) ? "Ca sáng" : "Ca chiều";

    internal static List<DoctorDashboardShiftDto> ToDtos(IEnumerable<DoctorWorkSchedule> schedules) => schedules
        .OrderBy(schedule => schedule.StartTime)
        .Select(schedule => new DoctorDashboardShiftDto
        {
            ShiftName = ShiftName(schedule),
            StartTime = schedule.StartTime,
            EndTime = schedule.EndTime
        })
        .ToList();

    internal static string DescribeCurrent(IReadOnlyCollection<DoctorWorkSchedule> schedules, TimeOnly currentTime)
    {
        if (schedules.Count == 0) return "Không có ca trực";
        var matching = schedules.FirstOrDefault(schedule => schedule.StartTime <= currentTime && schedule.EndTime >= currentTime);
        return matching != null
            ? $"{ShiftName(matching)} ({matching.StartTime:HH\\:mm} - {matching.EndTime:HH\\:mm})"
            : string.Join(", ", schedules.Select(schedule => $"{schedule.StartTime:HH\\:mm} - {schedule.EndTime:HH\\:mm}"));
    }
}
