using System.Threading.Tasks;
using ClinicManagement.Application.Appointments.DTOs.ChangeRequests;
using ClinicManagement.Application.Common.Models;

namespace ClinicManagement.Application.Appointments.Interfaces;

public interface IChangeRequestService
{
    // For Patients
    Task<ChangeRequestDto> CreateRescheduleRequestAsync(long appointmentId, CreateRescheduleRequestDto request);
    Task<ChangeRequestDto> CreateCancellationRequestAsync(long appointmentId, CreateCancellationRequestDto request);
    Task WithdrawRequestAsync(long requestId);
    Task<PagedResult<ChangeRequestDto>> GetMyChangeRequestsAsync(string? status, int page, int pageSize);
    
    // For Receptionists
    Task<PagedResult<ChangeRequestDto>> GetAllChangeRequestsAsync(string? status, int page, int pageSize);
    Task<ChangeRequestDto> GetChangeRequestByIdAsync(long requestId);
    Task ApproveRescheduleAsync(long requestId, ProcessChangeRequestDto request);
    Task ApproveCancellationAsync(long requestId, ProcessChangeRequestDto request);
    Task RejectRequestAsync(long requestId, ProcessChangeRequestDto request);
}
