using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.HealthPackages.DTOs;

namespace ClinicManagement.Application.HealthPackages.Interfaces;

public interface IHealthPackageService
{
    Task<List<HealthPackageDto>> GetActivePackagesAsync(CancellationToken cancellationToken = default);
    Task<HealthPackageDto> GetPackageByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<PagedResult<HealthPackageDto>> GetAllPackagesAsync(string? search, bool? isActive, int page = 1, int pageSize = 10, CancellationToken cancellationToken = default);
    Task<HealthPackageDto> CreatePackageAsync(CreateHealthPackageRequest request, CancellationToken cancellationToken = default);
    Task<HealthPackageDto> UpdatePackageAsync(long id, UpdateHealthPackageRequest request, CancellationToken cancellationToken = default);
    Task DeletePackageAsync(long id, CancellationToken cancellationToken = default);
}
