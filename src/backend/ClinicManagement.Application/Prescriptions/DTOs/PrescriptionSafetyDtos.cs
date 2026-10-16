using System.ComponentModel.DataAnnotations;
using ClinicManagement.Application.Mpi.DTOs;

namespace ClinicManagement.Application.Prescriptions.DTOs;

public class PrescriptionSafetyWarningDto
{
    public string Level { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public List<long> MedicineIds { get; set; } = new();
    public string Title { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
}

public class PrescriptionSafetyResultDto
{
    public List<PatientAllergyDto> Allergies { get; set; } = new();
    public List<PrescriptionSafetyWarningDto> Warnings { get; set; } = new();
}

public class CheckPrescriptionSafetyRequest
{
    [Required]
    public List<long> MedicineIds { get; set; } = new();
}
