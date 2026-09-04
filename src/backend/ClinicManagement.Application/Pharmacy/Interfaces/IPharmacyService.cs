using System.Threading.Tasks;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Pharmacy.DTOs;
using ClinicManagement.Application.Prescriptions.DTOs;

namespace ClinicManagement.Application.Pharmacy.Interfaces;

public interface IPharmacyService
{
    Task<PharmacyDashboardDto> GetDashboardStatsAsync();
    Task<PagedResult<PharmacyPrescriptionListDto>> GetPrescriptionsAsync(string? status, string? search, int page, int pageSize);
    Task<PrescriptionDetailDto> GetPrescriptionByIdAsync(long id);
    Task<DispensePrescriptionResultDto> DispensePrescriptionAsync(long prescriptionId);
    Task<PagedResult<StockTransactionDto>> GetStockTransactionsAsync(long? medicineId, int page, int pageSize);
    Task AdjustStockAsync(AdjustStockDto request);
}
