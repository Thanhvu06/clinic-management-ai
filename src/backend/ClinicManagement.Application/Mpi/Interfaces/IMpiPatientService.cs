using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Mpi.DTOs;

namespace ClinicManagement.Application.Mpi.Interfaces;

public interface IMpiPatientService
{
    Task<PagedResult<MpiPatientDto>> SearchPatientsAsync(PatientSearchQuery query, CancellationToken cancellationToken = default);
    Task<MpiPatientDto> GetPatientByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<MpiPatientDto> GetPatientByMrnAsync(string mrn, CancellationToken cancellationToken = default);
    Task<MpiPatientDto> RegisterWalkInPatientAsync(RegisterWalkInPatientRequest request, CancellationToken cancellationToken = default);
    Task<MpiPatientDto> UpdatePatientMpiAsync(long patientId, UpdateMpiPatientRequest request, CancellationToken cancellationToken = default);
    Task<PatientAllergyDto> AddAllergyAsync(long patientId, CreatePatientAllergyRequest request, CancellationToken cancellationToken = default);
    Task RemoveAllergyAsync(long patientId, long allergyId, CancellationToken cancellationToken = default);
}
