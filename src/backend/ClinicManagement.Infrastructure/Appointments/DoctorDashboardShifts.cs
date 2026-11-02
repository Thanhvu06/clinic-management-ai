using ClinicManagement.Application.Appointments.DTOs.Doctor;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Infrastructure.Appointments;

internal static class DoctorDashboardShifts
{
    private static string ShiftName(DoctorWorkSchedule schedule, bool twentyFourSeven) => twentyFourSeven
        ? schedule.StartTime < new TimeOnly(8, 0) ? "Ca đêm" : schedule.StartTime < new TimeOnly(16, 0) ? "Ca ngày" : "Ca tối"
        : schedule.StartTime < new TimeOnly(12, 0) ? "Ca sáng" : "Ca chiều";

    internal static List<DoctorDashboardShiftDto> ToDtos(IEnumerable<DoctorWorkSchedule> schedules, bool twentyFourSeven = false) => schedules
        .OrderBy(schedule => schedule.StartTime)
        .Select(schedule => new DoctorDashboardShiftDto
        {
            ShiftName = ShiftName(schedule, twentyFourSeven),
            StartTime = schedule.StartTime,
            EndTime = schedule.EndTime
        })
        .ToList();

    internal static string DescribeCurrent(IReadOnlyCollection<DoctorWorkSchedule> schedules, TimeOnly currentTime, bool twentyFourSeven = false)
    {
        if (schedules.Count == 0) return "Không có ca trực";
        var matching = schedules.FirstOrDefault(schedule => schedule.StartTime <= currentTime && schedule.EndTime >= currentTime);
        return matching != null
            ? $"{ShiftName(matching, twentyFourSeven)} ({matching.StartTime:HH\\:mm} - {matching.EndTime:HH\\:mm})"
            : string.Join(", ", schedules.Select(schedule => $"{schedule.StartTime:HH\\:mm} - {schedule.EndTime:HH\\:mm}"));
    }
}
