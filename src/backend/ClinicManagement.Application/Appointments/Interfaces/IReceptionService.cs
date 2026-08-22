using System.Threading.Tasks;
using ClinicManagement.Application.Appointments.DTOs.Reception;
using ClinicManagement.Application.Common.Models;

namespace ClinicManagement.Application.Appointments.Interfaces;

public interface IReceptionService
{
    Task<PagedResult<ReceptionAppointmentDto>> GetAppointmentsAsync(string? status, string? search, int page, int pageSize);
    Task<ReceptionAppointmentDto> GetAppointmentByIdAsync(long appointmentId);
    Task ConfirmAppointmentAsync(long appointmentId);
}
