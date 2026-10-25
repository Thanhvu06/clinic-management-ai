using ClinicManagement.Application.Appointments.DTOs.Doctor;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Appointments;

public partial class DoctorAppointmentService
{
    public async Task<PagedResult<DoctorAppointmentDto>> GetMyAppointmentsAsync(DateOnly? date, string? status, string? search, int page, int pageSize)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? 10 : Math.Min(pageSize, 100);

        var doctor = await GetCurrentDoctorAsync();

        var query = from a in _dbContext.Appointments
                    join p in _dbContext.Patients on a.PatientId equals p.Id
                    join pu in _dbContext.Users on p.UserId equals (Guid?)pu.Id into puGroup
                    from pu in puGroup.DefaultIfEmpty()
                    where a.DoctorId == doctor.Id
                    select new
                    {
                        Appointment = a,
                        PatientName = pu != null ? pu.FullName : (p.FullName ?? string.Empty),
                        PatientPhone = pu != null ? (pu.PhoneNumber ?? string.Empty) : (p.PhoneNumber ?? string.Empty),
                        PatientGender = p.Gender,
                        PatientDob = p.DateOfBirth
                    };

        if (date.HasValue)
        {
            query = query.Where(x => x.Appointment.AppointmentDate == date.Value);
        }

        if (!string.IsNullOrEmpty(status) && Enum.TryParse<AppointmentStatus>(status, true, out var parsedStatus))
        {
            query = query.Where(x => x.Appointment.Status == parsedStatus);
        }

        if (!string.IsNullOrEmpty(search))
        {
            query = query.Where(x => x.Appointment.AppointmentCode.Contains(search)
                                  || x.PatientName.Contains(search)
                                  || x.PatientPhone.Contains(search));
        }

        var today = _dateTimeProvider.VietnamToday;
        query = query.OrderBy(x => x.Appointment.AppointmentDate < today)
            .ThenBy(x => x.Appointment.AppointmentDate >= today ? x.Appointment.AppointmentDate : DateOnly.MaxValue)
            .ThenBy(x => x.Appointment.AppointmentDate >= today ? x.Appointment.StartTime : TimeOnly.MinValue)
            .ThenByDescending(x => x.Appointment.AppointmentDate)
            .ThenByDescending(x => x.Appointment.StartTime)
            .ThenBy(x => x.Appointment.Id);

        var totalItems = await query.CountAsync();
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        var resultItems = items.Select(x => MapToDto(x.Appointment, x.PatientName, x.PatientPhone, x.PatientGender, x.PatientDob)).ToList();

        return new PagedResult<DoctorAppointmentDto>(resultItems, totalItems, page, pageSize);
    }

}
