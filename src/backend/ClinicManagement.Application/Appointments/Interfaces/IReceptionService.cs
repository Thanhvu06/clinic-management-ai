using System.Collections.Generic;
using System.Threading.Tasks;
using ClinicManagement.Application.Appointments.DTOs;
using ClinicManagement.Application.Appointments.DTOs.Reception;
using ClinicManagement.Application.Common.Models;

namespace ClinicManagement.Application.Appointments.Interfaces;

public interface IReceptionService
{
    Task<PagedResult<ReceptionAppointmentDto>> GetAppointmentsAsync(string? status, string? tab, long? facilityId, string? search, int page, int pageSize);
    Task<PagedResult<ReceptionAppointmentDto>> GetAppointmentsAsync(string? status, string? search, int page, int pageSize)
        => GetAppointmentsAsync(status, null, null, search, page, pageSize);
    Task<ReceptionAppointmentDto> GetAppointmentByIdAsync(long appointmentId);
    Task<List<AppointmentHistoryDto>> GetAppointmentHistoryAsync(long appointmentId);
    Task ConfirmAppointmentAsync(long appointmentId);
    Task CheckInAppointmentAsync(long appointmentId);
    Task<ReceptionStatsDto> GetStatsAsync(long? facilityId = null);
}
