using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.HealthPackages.DTOs;
using ClinicManagement.Application.HealthPackages.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.HealthPackages;

public class HealthPackageService : IHealthPackageService
{
    private readonly AppDbContext _dbContext;

    public HealthPackageService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<HealthPackageDto>> GetActivePackagesAsync(CancellationToken cancellationToken = default)
    {
        var packages = await _dbContext.HealthPackages
            .AsNoTracking()
            .Where(p => p.IsActive)
            .OrderBy(p => p.Id)
            .ToListAsync(cancellationToken);

        return packages.Select(MapToDto).ToList();
    }

    public async Task<HealthPackageDto> GetPackageByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var package = await _dbContext.HealthPackages
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (package == null)
            throw new NotFoundException("Gói khám không tồn tại.");

        return MapToDto(package);
    }

    public async Task<List<HealthPackageDto>> GetAllPackagesAsync(CancellationToken cancellationToken = default)
    {
        var packages = await _dbContext.HealthPackages
            .AsNoTracking()
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(cancellationToken);

        return packages.Select(MapToDto).ToList();
    }

    public async Task<HealthPackageDto> CreatePackageAsync(CreateHealthPackageRequest request, CancellationToken cancellationToken = default)
    {
        var codeExists = await _dbContext.HealthPackages
            .AnyAsync(p => p.Code.ToLower() == request.Code.Trim().ToLower(), cancellationToken);

        if (codeExists)
            throw new BusinessException("CODE_EXISTS", "Mã gói khám đã tồn tại.");

        var package = new HealthPackage
        {
            Code = request.Code.Trim().ToUpper(),
            Name = request.Name.Trim(),
            TargetAudience = request.TargetAudience.Trim(),
            Description = request.Description.Trim(),
            Price = request.Price,
            ImageUrl = request.ImageUrl?.Trim(),
            IncludedServicesJson = JsonSerializer.Serialize(request.IncludedServices ?? new List<string>()),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.HealthPackages.Add(package);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return MapToDto(package);
    }

    public async Task<HealthPackageDto> UpdatePackageAsync(long id, UpdateHealthPackageRequest request, CancellationToken cancellationToken = default)
    {
        var package = await _dbContext.HealthPackages.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (package == null)
            throw new NotFoundException("Gói khám không tồn tại.");

        package.Name = request.Name.Trim();
        package.TargetAudience = request.TargetAudience.Trim();
        package.Description = request.Description.Trim();
        package.Price = request.Price;
        package.ImageUrl = request.ImageUrl?.Trim();
        package.IncludedServicesJson = JsonSerializer.Serialize(request.IncludedServices ?? new List<string>());
        package.IsActive = request.IsActive;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return MapToDto(package);
    }

    public async Task DeletePackageAsync(long id, CancellationToken cancellationToken = default)
    {
        var package = await _dbContext.HealthPackages.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (package == null)
            throw new NotFoundException("Gói khám không tồn tại.");

        // Soft delete by deactivating
        package.IsActive = false;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static HealthPackageDto MapToDto(HealthPackage package)
    {
        List<string> includedServices = new();
        if (!string.IsNullOrWhiteSpace(package.IncludedServicesJson))
        {
            try
            {
                includedServices = JsonSerializer.Deserialize<List<string>>(package.IncludedServicesJson) ?? new List<string>();
            }
            catch
            {
                includedServices = new List<string>();
            }
        }

        return new HealthPackageDto
        {
            Id = package.Id,
            Code = package.Code,
            Name = package.Name,
            TargetAudience = package.TargetAudience,
            Description = package.Description,
            Price = package.Price,
            ImageUrl = package.ImageUrl,
            IncludedServices = includedServices,
            IsActive = package.IsActive,
            CreatedAt = package.CreatedAt
        };
    }
}
