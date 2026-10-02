using System.Threading;
using System.Threading.Tasks;
using ClinicManagement.Application.Common.Constants;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Mpi.DTOs;
using ClinicManagement.Application.Mpi.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicManagement.Api.Controllers.Mpi;

[ApiController]
[Route("api/v1/mpi/patients")]
[Authorize(Roles = $"{RoleNames.Admin},{RoleNames.Receptionist},{RoleNames.Doctor}")]
public class MpiPatientsController : ControllerBase
{
    private readonly IMpiPatientService _mpiPatientService;

    public MpiPatientsController(IMpiPatientService mpiPatientService)
    {
        _mpiPatientService = mpiPatientService;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<MpiPatientDto>>>> SearchPatients(
        [FromQuery] PatientSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        var result = await _mpiPatientService.SearchPatientsAsync(query, cancellationToken);
        return Ok(ApiResponse<PagedResult<MpiPatientDto>>.Ok(result));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<MpiPatientDto>>> GetPatientById(long id, CancellationToken cancellationToken = default)
    {
        var patient = await _mpiPatientService.GetPatientByIdAsync(id, cancellationToken);
        return Ok(ApiResponse<MpiPatientDto>.Ok(patient));
    }

    [HttpGet("by-mrn/{mrn}")]
    public async Task<ActionResult<ApiResponse<MpiPatientDto>>> GetPatientByMrn(string mrn, CancellationToken cancellationToken = default)
    {
        var patient = await _mpiPatientService.GetPatientByMrnAsync(mrn, cancellationToken);
        return Ok(ApiResponse<MpiPatientDto>.Ok(patient));
    }

    [HttpPost("walk-in")]
    [Authorize(Roles = $"{RoleNames.Admin},{RoleNames.Receptionist}")]
    public async Task<ActionResult<ApiResponse<MpiPatientDto>>> RegisterWalkInPatient(
        [FromBody] RegisterWalkInPatientRequest request,
        CancellationToken cancellationToken = default)
    {
        var created = await _mpiPatientService.RegisterWalkInPatientAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetPatientById), new { id = created.Id }, ApiResponse<MpiPatientDto>.Ok(created, "Đăng ký tiếp nhận bệnh nhân thành công."));
    }

    [HttpPut("{id}")]
    [Authorize(Roles = $"{RoleNames.Admin},{RoleNames.Receptionist}")]
    public async Task<ActionResult<ApiResponse<MpiPatientDto>>> UpdatePatientMpi(
        long id,
        [FromBody] UpdateMpiPatientRequest request,
        CancellationToken cancellationToken = default)
    {
        var updated = await _mpiPatientService.UpdatePatientMpiAsync(id, request, cancellationToken);
        return Ok(ApiResponse<MpiPatientDto>.Ok(updated, "Cập nhật hồ sơ bệnh nhân thành công."));
    }

    [HttpPost("{id}/allergies")]
    public async Task<ActionResult<ApiResponse<PatientAllergyDto>>> AddAllergy(
        long id,
        [FromBody] CreatePatientAllergyRequest request,
        CancellationToken cancellationToken = default)
    {
        var allergy = await _mpiPatientService.AddAllergyAsync(id, request, cancellationToken);
        return Ok(ApiResponse<PatientAllergyDto>.Ok(allergy, "Ghi nhận thông tin dị ứng thành công."));
    }

    [HttpDelete("{id}/allergies/{allergyId}")]
    [Authorize(Roles = $"{RoleNames.Admin},{RoleNames.Doctor},{RoleNames.Receptionist}")]
    public async Task<ActionResult<ApiResponse<object?>>> RemoveAllergy(
        long id,
        long allergyId,
        CancellationToken cancellationToken = default)
    {
        await _mpiPatientService.RemoveAllergyAsync(id, allergyId, cancellationToken);
        return Ok(ApiResponse<object?>.Ok(null, "Xóa thông tin dị ứng thành công."));
    }
}
