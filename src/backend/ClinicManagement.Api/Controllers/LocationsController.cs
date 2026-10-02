using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Locations.DTOs;
using ClinicManagement.Application.Locations.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicManagement.Api.Controllers;

[ApiController]
[Route("api/v1/locations")]
[AllowAnonymous]
public class LocationsController : ControllerBase
{
    private readonly ILocationService _locationService;

    public LocationsController(ILocationService locationService)
    {
        _locationService = locationService;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<ClinicLocationDto>>>> GetActiveLocations(CancellationToken cancellationToken)
    {
        var locations = await _locationService.GetActiveLocationsAsync(cancellationToken);
        return Ok(ApiResponse<List<ClinicLocationDto>>.Ok(locations));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<ClinicLocationDto>>> GetLocationById(long id, CancellationToken cancellationToken)
    {
        var location = await _locationService.GetLocationByIdAsync(id, cancellationToken);
        return Ok(ApiResponse<ClinicLocationDto>.Ok(location));
    }
}
