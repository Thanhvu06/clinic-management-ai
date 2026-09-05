using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.HealthPackages.DTOs;

namespace ClinicManagement.Application.HealthPackages.Interfaces;

public interface IHealthPackageRegistrationService
{
    Task<HealthPackageRegistrationDto> RegisterPackageAsync(CreatePackageRegistrationRequest request, CancellationToken cancellationToken = default);
    Task<List<HealthPackageRegistrationDto>> GetMyRegistrationsAsync(CancellationToken cancellationToken = default);
    Task<HealthPackageRegistrationDto> GetRegistrationByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<HealthPackageRegistrationDto> CancelMyRegistrationAsync(long id, CancelPackageRegistrationRequest? request = null, CancellationToken cancellationToken = default);

    Task<PagedResult<HealthPackageRegistrationDto>> GetAllRegistrationsForReceptionAsync(string? status, string? search, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<HealthPackageRegistrationDto> ConfirmRegistrationAsync(long id, ConfirmPackageRegistrationRequest? request, CancellationToken cancellationToken = default);
    Task<HealthPackageRegistrationDto> CancelRegistrationByReceptionAsync(long id, CancelPackageRegistrationRequest? request, CancellationToken cancellationToken = default);
}
