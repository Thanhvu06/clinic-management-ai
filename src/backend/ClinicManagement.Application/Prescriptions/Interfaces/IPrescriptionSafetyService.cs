using ClinicManagement.Application.Prescriptions.DTOs;

namespace ClinicManagement.Application.Prescriptions.Interfaces;

public interface IPrescriptionSafetyService
{
    Task<PrescriptionSafetyResultDto> CheckAsync(long patientId, IEnumerable<long> medicineIds);
}
