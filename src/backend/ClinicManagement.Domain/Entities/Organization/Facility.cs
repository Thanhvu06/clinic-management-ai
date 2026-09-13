using System;
using System.Collections.Generic;

namespace ClinicManagement.Domain.Entities;

public class Facility
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? TaxCode { get; set; }
    public string? HospitalLevel { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }

    public ICollection<Building> Buildings { get; set; } = new List<Building>();
    public ICollection<Department> Departments { get; set; } = new List<Department>();
}
