using System.Collections.Generic;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.Entities;

public class Room
{
    public long Id { get; set; }
    public long DepartmentId { get; set; }
    public long? BuildingId { get; set; }
    public string RoomNumber { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public RoomType RoomType { get; set; } = RoomType.Consultation;
    public int FloorNumber { get; set; } = 1;
    public int MaxCapacity { get; set; } = 1;
    public bool IsActive { get; set; } = true;

    public Department Department { get; set; } = null!;
    public Building? Building { get; set; }
    public ICollection<Bed> Beds { get; set; } = new List<Bed>();
}
