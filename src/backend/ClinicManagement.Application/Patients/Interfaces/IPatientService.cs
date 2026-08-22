using ClinicManagement.Application.Patients.DTOs;

namespace ClinicManagement.Application.Patients.Interfaces;

public interface IPatientService
{
    Task<PatientProfileDto> GetMyProfileAsync();
    Task UpdateMyProfileAsync(UpdatePatientProfileRequest request);
}
