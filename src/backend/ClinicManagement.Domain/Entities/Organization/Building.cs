using System.Collections.Generic;

namespace ClinicManagement.Domain.Entities;

public class Building
{
    public long Id { get; set; }
    public long FacilityId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int NumberOfFloors { get; set; } = 1;
    public bool IsActive { get; set; } = true;

    public Facility Facility { get; set; } = null!;
    public ICollection<Department> Departments { get; set; } = new List<Department>();
    public ICollection<Room> Rooms { get; set; } = new List<Room>();
}
