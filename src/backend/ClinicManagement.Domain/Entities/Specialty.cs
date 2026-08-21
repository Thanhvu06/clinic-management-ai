using System.Collections.Generic;

namespace ClinicManagement.Domain.Entities;

public class Specialty
{
    public long Id { get; set; }
    public string SpecialtyCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    public bool AiEnabled { get; set; }

    public ICollection<DoctorSpecialty> DoctorSpecialties { get; set; } = new List<DoctorSpecialty>();
    public ICollection<Appointment> Appointments { get; set; } = new List<Appointment>();
}
