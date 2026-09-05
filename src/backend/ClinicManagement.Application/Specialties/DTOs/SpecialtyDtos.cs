namespace ClinicManagement.Application.Specialties.DTOs;

public class SpecialtyDto
{
    public long Id { get; set; }
    public string SpecialtyCode { get; set; } = string.Empty;
    public string SpecialtyName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool AiEnabled { get; set; }
}

public class DoctorBasicDto
{
    public long Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string AcademicTitle { get; set; } = string.Empty;
    public int ExperienceYears { get; set; }
    public long? SpecialtyId { get; set; }
    public string SpecialtyName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public class RecommendedDoctorDto
{
    public long DoctorId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public int ExperienceYears { get; set; }
    public double ExperienceScore { get; set; }
    public double AvailabilityScore { get; set; }
    public double TotalScore { get; set; }
    public DateTimeOffset? EarliestAvailableSlot { get; set; }
}
