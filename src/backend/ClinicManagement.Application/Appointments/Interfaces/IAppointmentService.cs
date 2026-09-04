using System.Collections.Generic;
using System.Threading.Tasks;
using ClinicManagement.Application.Appointments.DTOs;
using ClinicManagement.Application.Common.Models;

namespace ClinicManagement.Application.Appointments.Interfaces;

public interface IAppointmentService
{
    Task<AppointmentDto> CreateAppointmentAsync(CreateAppointmentRequest request);
    Task<PagedResult<AppointmentDto>> GetPatientAppointmentsAsync(string? status, int page, int pageSize);
    Task<AppointmentDto> GetPatientAppointmentByIdAsync(long appointmentId);
    Task<List<AppointmentHistoryDto>> GetAppointmentHistoryAsync(long appointmentId);
    Task<List<AppointmentLookupDto>> LookupAppointmentsAsync(string query);
}
