using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Locations.DTOs;
using ClinicManagement.Application.Locations.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Locations;

public class LocationService : ILocationService
{
    private readonly AppDbContext _dbContext;

    public LocationService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<ClinicLocationDto>> GetActiveLocationsAsync(CancellationToken cancellationToken = default)
    {
        var locations = await _dbContext.ClinicLocations
            .AsNoTracking()
            .Where(l => l.IsActive)
            .OrderBy(l => l.Id)
            .ToListAsync(cancellationToken);

        return locations.Select(MapToDto).ToList();
    }

    public async Task<ClinicLocationDto> GetLocationByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var location = await _dbContext.ClinicLocations
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == id && l.IsActive, cancellationToken);

        if (location == null)
            throw new NotFoundException("Cơ sở phòng khám không tồn tại hoặc đã tạm dừng hoạt động.");

        return MapToDto(location);
    }

    private static ClinicLocationDto MapToDto(ClinicLocation location)
    {
        var features = new List<string>();
        if (!string.IsNullOrWhiteSpace(location.ServicesJson))
        {
            try
            {
                features = JsonSerializer.Deserialize<List<string>>(location.ServicesJson) ?? new List<string>();
            }
            catch
            {
                features = new List<string>();
            }
        }

        return new ClinicLocationDto
        {
            Id = location.Id,
            Code = location.Code,
            Name = location.Name,
            Address = location.Address,
            City = location.City,
            Phone = location.Phone,
            OpeningHours = location.OpeningHours,
            Description = location.Description,
            Features = features,
            IsActive = location.IsActive
        };
    }
}
