using System.Threading.Tasks;
using ClinicManagement.Application.Common.Constants;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Pharmacy.DTOs;
using ClinicManagement.Application.Pharmacy.Interfaces;
using ClinicManagement.Application.Prescriptions.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicManagement.Api.Controllers;

[ApiController]
[Route("api/v1/pharmacy")]
[Authorize(Roles = RoleNames.Pharmacist + "," + RoleNames.Admin)]
public class PharmacyController : ControllerBase
{
    private readonly IPharmacyService _pharmacyService;

    public PharmacyController(IPharmacyService pharmacyService)
    {
        _pharmacyService = pharmacyService;
    }

    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboardStats()
    {
        var result = await _pharmacyService.GetDashboardStatsAsync();
        return Ok(ApiResponse<PharmacyDashboardDto>.Ok(result));
    }

    [HttpGet("prescriptions")]
    public async Task<IActionResult> GetPrescriptions([FromQuery] string? status, [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 10)
    {
        var result = await _pharmacyService.GetPrescriptionsAsync(status, search, page, pageSize);
        return Ok(ApiResponse<PagedResult<PharmacyPrescriptionListDto>>.Ok(result));
    }

    [HttpGet("prescriptions/{id}")]
    public async Task<IActionResult> GetPrescriptionById(long id)
    {
        var result = await _pharmacyService.GetPrescriptionByIdAsync(id);
        return Ok(ApiResponse<PrescriptionDetailDto>.Ok(result));
    }

    [HttpPost("prescriptions/{id}/dispense")]
    public async Task<IActionResult> DispensePrescription(long id)
    {
        var result = await _pharmacyService.DispensePrescriptionAsync(id);
        return Ok(ApiResponse<DispensePrescriptionResultDto>.Ok(result, "Cấp phát thuốc thành công."));
    }

    [HttpGet("inventory-transactions")]
    public async Task<IActionResult> GetInventoryTransactions([FromQuery] long? medicineId, [FromQuery] int page = 1, [FromQuery] int pageSize = 15)
    {
        var result = await _pharmacyService.GetStockTransactionsAsync(medicineId, page, pageSize);
        return Ok(ApiResponse<PagedResult<StockTransactionDto>>.Ok(result));
    }

    [HttpPost("inventory/adjust")]
    public async Task<IActionResult> AdjustStock([FromBody] AdjustStockDto request)
    {
        await _pharmacyService.AdjustStockAsync(request);
        return Ok(ApiResponse.Ok("Cập nhật tồn kho thành công."));
    }
}
