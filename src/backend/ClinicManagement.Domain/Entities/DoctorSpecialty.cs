namespace ClinicManagement.Domain.Entities;

public class DoctorSpecialty
{
    public long DoctorId { get; set; }
    public long SpecialtyId { get; set; }
    public bool IsPrimary { get; set; }

    public Doctor Doctor { get; set; } = null!;
    public Specialty Specialty { get; set; } = null!;
}
