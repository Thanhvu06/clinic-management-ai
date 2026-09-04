using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ClinicManagement.Application.HealthPackages.DTOs;

namespace ClinicManagement.Application.HealthPackages.Interfaces;

public interface IHealthPackageService
{
    Task<List<HealthPackageDto>> GetActivePackagesAsync(CancellationToken cancellationToken = default);
    Task<HealthPackageDto> GetPackageByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<List<HealthPackageDto>> GetAllPackagesAsync(CancellationToken cancellationToken = default);
    Task<HealthPackageDto> CreatePackageAsync(CreateHealthPackageRequest request, CancellationToken cancellationToken = default);
    Task<HealthPackageDto> UpdatePackageAsync(long id, UpdateHealthPackageRequest request, CancellationToken cancellationToken = default);
    Task DeletePackageAsync(long id, CancellationToken cancellationToken = default);
}
