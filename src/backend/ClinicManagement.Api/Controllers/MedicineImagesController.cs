using ClinicManagement.Application.Common.Constants;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Medicines.DTOs;
using ClinicManagement.Application.Medicines.Interfaces;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicManagement.Api.Controllers;

[ApiController, Route("api/v1/admin/medicines/{id}/image")]
[Authorize(Roles = RoleNames.Admin + "," + RoleNames.Pharmacist)]
public class MedicineImagesController(MedicineImageStorage storage, AppDbContext db, IMedicineService service) : ControllerBase
{
    [HttpPost, Consumes("multipart/form-data")]
    public async Task<IActionResult> Upload(long id, [FromForm] IFormFile? file, CancellationToken cancellationToken)
    {
        if (file == null) throw new BusinessException("INVALID_IMAGE_TYPE", "Vui lòng chọn ảnh thuốc.");
        return Ok(ApiResponse<MedicineDto>.Ok(await storage.UploadAsync(id, file, db, service, cancellationToken)));
    }
    [HttpDelete]
    public async Task<IActionResult> Delete(long id, CancellationToken cancellationToken) =>
        Ok(ApiResponse<MedicineDto>.Ok(await storage.DeleteAsync(id, db, service, cancellationToken)));
}
