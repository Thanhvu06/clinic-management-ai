using System.Collections.Generic;
using System.Threading.Tasks;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Medicines.DTOs;

namespace ClinicManagement.Application.Medicines.Interfaces;

public interface IMedicineService
{
    Task<List<ActiveMedicineDto>> GetActiveMedicinesAsync();
    Task<PagedResult<MedicineDto>> GetMedicinesAsync(string? search, bool? isActive, int page, int pageSize);
    Task<MedicineDto> GetMedicineByIdAsync(long id);
    Task<MedicineDto> CreateMedicineAsync(CreateMedicineDto dto);
    Task<MedicineDto> UpdateMedicineAsync(long id, UpdateMedicineDto dto);
    Task ToggleMedicineStatusAsync(long id);
}
