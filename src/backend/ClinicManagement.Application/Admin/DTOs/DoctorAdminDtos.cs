using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ClinicManagement.Application.Admin.DTOs;

public class AdminDoctorDto
{
    public long Id { get; set; }
    public Guid UserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? AcademicTitle { get; set; }
    public int ExperienceYears { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    public List<DoctorSpecialtyDto> Specialties { get; set; } = new();
}

public class DoctorSpecialtyDto
{
    public long SpecialtyId { get; set; }
    public string SpecialtyName { get; set; } = string.Empty;
    public bool IsPrimary { get; set; }
}

public class CreateDoctorDto
{
    [Required]
    public Guid UserId { get; set; }
    
    [MaxLength(100)]
    public string? AcademicTitle { get; set; }
    
    [Range(0, 100)]
    public int ExperienceYears { get; set; }
    
    [MaxLength(2000)]
    public string? Description { get; set; }
    
    public bool IsActive { get; set; } = true;

    [Required]
    public List<AssignSpecialtyDto> Specialties { get; set; } = new();
}

public class AssignSpecialtyDto
{
    public long SpecialtyId { get; set; }
    public bool IsPrimary { get; set; }
}

public class UpdateDoctorDto
{
    [MaxLength(100)]
    public string? AcademicTitle { get; set; }
    
    [Range(0, 100)]
    public int ExperienceYears { get; set; }
    
    [MaxLength(2000)]
    public string? Description { get; set; }
    
    public bool IsActive { get; set; }
}
