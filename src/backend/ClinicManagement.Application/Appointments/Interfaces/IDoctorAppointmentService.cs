using System.Threading.Tasks;
using ClinicManagement.Application.Appointments.DTOs.Doctor;
using ClinicManagement.Application.Appointments.DTOs.Revisit;
using ClinicManagement.Application.Common.Models;

namespace ClinicManagement.Application.Appointments.Interfaces;

public interface IDoctorAppointmentService
{
    Task<PagedResult<DoctorAppointmentDto>> GetMyAppointmentsAsync(string? status, string? search, int page, int pageSize);
    Task<DoctorAppointmentDto> GetAppointmentByIdAsync(long appointmentId);
    Task CompleteAppointmentAsync(long appointmentId, CompleteAppointmentDto request);
    Task MarkNoShowAsync(long appointmentId, NoShowAppointmentDto request);
    Task<RevisitRequestDto> CreateRevisitRequestAsync(long appointmentId, CreateRevisitRequestDto request);
}

public interface IRevisitService
{
    Task<PagedResult<RevisitRequestDto>> GetMyRevisitRequestsAsync(string? status, int page, int pageSize);
    Task<RevisitRequestDto> GetRevisitRequestByIdAsync(long id);
    Task AcceptRevisitRequestAsync(long id, AcceptRevisitRequestDto request);
    Task RejectRevisitRequestAsync(long id, RejectRevisitRequestDto request);
}
