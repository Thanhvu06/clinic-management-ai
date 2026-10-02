namespace ClinicManagement.Domain.Entities;

public class EmergencyContact
{
    public long Id { get; set; }
    public long PatientId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Relationship { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string? Address { get; set; }
    public bool IsPrimary { get; set; } = true;

    public Patient Patient { get; set; } = null!;
}
