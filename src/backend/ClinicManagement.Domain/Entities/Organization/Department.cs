using System.Collections.Generic;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.Entities;

public class Department
{
    public long Id { get; set; }
    public long FacilityId { get; set; }
    public long? BuildingId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DepartmentType DepartmentType { get; set; } = DepartmentType.Clinical;
    public long? HeadOfDepartmentDoctorId { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;

    public Facility Facility { get; set; } = null!;
    public Building? Building { get; set; }
    public Doctor? HeadOfDepartmentDoctor { get; set; }
    public ICollection<Room> Rooms { get; set; } = new List<Room>();
}
