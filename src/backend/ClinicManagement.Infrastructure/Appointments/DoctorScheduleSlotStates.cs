using ClinicManagement.Application.Appointments.DTOs.Doctor;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Infrastructure.Appointments;

public sealed record DoctorScheduleAppointmentRow(long Id, long AppointmentSlotId, string AppointmentCode, AppointmentStatus Status, string PatientName);

public static class DoctorScheduleSlotStates
{
    public const string Booked = "Booked";
    public const string NoShow = "NoShow";
    public const string Past = "Past";
    public const string Free = "Free";

    // Completion, no-show expiry and cancellation all release AppointmentSlot.IsBooked, so the flag
    // alone cannot tell the doctor what happened in a slot; the appointment that occupied it can.
    public static Dictionary<long, DoctorScheduleAppointmentRow> OccupyingAppointments(IEnumerable<DoctorScheduleAppointmentRow> rows) =>
        rows.Where(a => a.Status != AppointmentStatus.Cancelled)
            .GroupBy(a => a.AppointmentSlotId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(a => a.Status.HoldsSlot()).ThenByDescending(a => a.Id).First());

    public static DoctorSlotDetailDto Build(AppointmentSlot slot, DoctorScheduleAppointmentRow? appointment, DateTime vietnamNow)
    {
        var isNoShow = appointment?.Status == AppointmentStatus.NoShow;
        var isBooked = slot.IsBooked || appointment != null;
        var endDate = slot.EndTime <= slot.StartTime ? slot.SlotDate.AddDays(1) : slot.SlotDate;
        var isPast = endDate.ToDateTime(slot.EndTime) <= vietnamNow;
        var state = isNoShow ? NoShow : isBooked ? Booked : isPast ? Past : Free;

        return new DoctorSlotDetailDto
        {
            SlotId = slot.Id,
            SlotDate = slot.SlotDate,
            StartTime = slot.StartTime,
            EndTime = slot.EndTime,
            IsBooked = isBooked,
            IsPast = isPast,
            SlotState = state,
            AppointmentId = appointment?.Id,
            AppointmentCode = appointment?.AppointmentCode,
            PatientName = appointment?.PatientName,
            Status = appointment?.Status.ToString()
        };
    }
}
