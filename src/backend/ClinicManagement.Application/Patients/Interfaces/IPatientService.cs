using ClinicManagement.Application.Appointments.DTOs.Doctor;
using ClinicManagement.Application.Patients.DTOs;

namespace ClinicManagement.Application.Patients.Interfaces;

public interface IPatientService
{
    Task<PatientProfileDto> GetMyProfileAsync();
    Task UpdateMyProfileAsync(UpdatePatientProfileRequest request);
    Task<List<PatientPrescriptionDto>> GetMyPrescriptionsAsync();
    Task<List<PatientVitalHistoryItemDto>> GetMyVitalsAsync(int limit);
}
