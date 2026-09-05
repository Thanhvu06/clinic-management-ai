using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ClinicManagement.Application.HealthPackages.DTOs;

namespace ClinicManagement.Application.HealthPackages.Interfaces;

public interface IHealthPackageRegistrationService
{
    Task<HealthPackageRegistrationDto> RegisterPackageAsync(CreatePackageRegistrationRequest request, CancellationToken cancellationToken = default);
    Task<List<HealthPackageRegistrationDto>> GetMyRegistrationsAsync(CancellationToken cancellationToken = default);
    Task<HealthPackageRegistrationDto> GetRegistrationByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<HealthPackageRegistrationDto> CancelMyRegistrationAsync(long id, CancellationToken cancellationToken = default);

    Task<List<HealthPackageRegistrationDto>> GetAllRegistrationsForReceptionAsync(string? status, CancellationToken cancellationToken = default);
    Task<HealthPackageRegistrationDto> ConfirmRegistrationAsync(long id, CancellationToken cancellationToken = default);
    Task<HealthPackageRegistrationDto> CancelRegistrationByReceptionAsync(long id, CancellationToken cancellationToken = default);
}
