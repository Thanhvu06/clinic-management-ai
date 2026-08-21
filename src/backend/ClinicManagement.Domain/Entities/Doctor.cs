using System;
using System.Collections.Generic;

namespace ClinicManagement.Domain.Entities;

public class Doctor
{
    public long Id { get; set; }
    public Guid UserId { get; set; }
    public string? AcademicTitle { get; set; }
    public int ExperienceYears { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; }

    public ICollection<DoctorSpecialty> DoctorSpecialties { get; set; } = new List<DoctorSpecialty>();
}
