using System.Threading.Tasks;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Leaves.DTOs;

namespace ClinicManagement.Application.Leaves.Interfaces;

public interface IDoctorLeaveService
{
    Task<PagedResult<LeaveRequestDto>> GetMyLeaveRequestsAsync(string? status, int page, int pageSize);
    Task<LeaveRequestDto> GetLeaveRequestByIdAsync(long id);
    Task<LeaveRequestDto> CreateLeaveRequestAsync(CreateLeaveRequestDto request);
    Task WithdrawLeaveRequestAsync(long id);
}

public interface IAdminLeaveService
{
    Task<PagedResult<LeaveRequestDto>> GetLeaveRequestsAsync(long? doctorId, string? status, int page, int pageSize);
    Task<LeaveRequestDto> GetLeaveRequestByIdAsync(long id);
    Task ApproveLeaveRequestAsync(long id, AdminProcessLeaveRequestDto request);
    Task RejectLeaveRequestAsync(long id, AdminProcessLeaveRequestDto request);
}
