using System.Collections.Generic;

namespace ClinicManagement.Application.Locations.DTOs;

public class ClinicLocationDto
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string OpeningHours { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<string> Features { get; set; } = new();
    public bool IsActive { get; set; }
}
