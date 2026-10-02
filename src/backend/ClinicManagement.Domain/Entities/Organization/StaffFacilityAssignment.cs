using System;

namespace ClinicManagement.Domain.Entities;

public class StaffFacilityAssignment
{
    public long Id { get; set; }
    public Guid UserId { get; set; }
    public long FacilityId { get; set; }
    public long? DepartmentId { get; set; }
    public string Role { get; set; } = string.Empty;
    public bool IsPrimary { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public DateTime AssignedAtUtc { get; set; } = DateTime.UtcNow;

    public Facility Facility { get; set; } = null!;
    public Department? Department { get; set; }
}
