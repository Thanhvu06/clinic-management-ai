using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ClinicManagement.Application.Locations.DTOs;

namespace ClinicManagement.Application.Locations.Interfaces;

public interface ILocationService
{
    Task<List<ClinicLocationDto>> GetActiveLocationsAsync(CancellationToken cancellationToken = default);
    Task<ClinicLocationDto> GetLocationByIdAsync(long id, CancellationToken cancellationToken = default);
}
