using System.ComponentModel.DataAnnotations;

namespace ClinicManagement.Application.Admin.DTOs;

public class AdminSpecialtyDto
{
    public long Id { get; set; }
    public string SpecialtyCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool AiEnabled { get; set; }
}

public class CreateSpecialtyDto
{
    [Required]
    [MaxLength(50)]
    public string SpecialtyCode { get; set; } = string.Empty;
    
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;
    
    [MaxLength(1000)]
    public string? Description { get; set; }
    
    public bool IsActive { get; set; } = true;
}

public class UpdateSpecialtyDto
{
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;
    
    [MaxLength(1000)]
    public string? Description { get; set; }
    
    public bool IsActive { get; set; }
}
