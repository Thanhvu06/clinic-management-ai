using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ClinicManagement.Application.Authentication.Interfaces;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Pharmacy.DTOs;
using ClinicManagement.Application.Pharmacy.Interfaces;
using ClinicManagement.Application.Prescriptions.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Pharmacy;

public class PharmacyService : IPharmacyService
{
    private readonly AppDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;

    public PharmacyService(AppDbContext dbContext, ICurrentUserService currentUserService)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
    }

    public async Task<PharmacyDashboardDto> GetDashboardStatsAsync()
    {
        var today = DateTime.UtcNow.Date;
        var pendingCount = await _dbContext.Prescriptions.CountAsync(p => p.Status == PrescriptionStatus.Issued);
        var dispensedTodayCount = await _dbContext.Prescriptions.CountAsync(p => p.Status == PrescriptionStatus.Dispensed && p.DispensedAt >= today);
        var lowStockCount = await _dbContext.Medicines.CountAsync(m => m.IsActive && m.StockQuantity <= m.ReorderLevel);
        var totalActiveMedicines = await _dbContext.Medicines.CountAsync(m => m.IsActive);

        return new PharmacyDashboardDto
        {
            PendingPrescriptionsCount = pendingCount,
            DispensedTodayCount = dispensedTodayCount,
            LowStockCount = lowStockCount,
            TotalActiveMedicines = totalActiveMedicines
        };
    }

    public async Task<PagedResult<PharmacyPrescriptionListDto>> GetPrescriptionsAsync(string? status, string? search, int page, int pageSize)
    {
        var query = from p in _dbContext.Prescriptions.AsNoTracking()
                    join a in _dbContext.Appointments.AsNoTracking() on p.AppointmentId equals a.Id
                    join pt in _dbContext.Patients.AsNoTracking() on p.PatientId equals pt.Id
                    join ptu in _dbContext.Users.AsNoTracking() on pt.UserId equals ptu.Id
                    join doc in _dbContext.Doctors.AsNoTracking() on p.DoctorId equals doc.Id
                    join docu in _dbContext.Users.AsNoTracking() on doc.UserId equals docu.Id
                    select new
                    {
                        Prescription = p,
                        Appointment = a,
                        PatientName = ptu.FullName,
                        PatientPhone = ptu.PhoneNumber,
                        DoctorName = docu.FullName
                    };

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<PrescriptionStatus>(status, true, out var parsedStatus))
        {
            query = query.Where(x => x.Prescription.Status == parsedStatus);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(x => x.PatientName.ToLower().Contains(s)
                                  || x.PatientPhone.Contains(s)
                                  || x.Appointment.AppointmentCode.ToLower().Contains(s));
        }

        query = query.OrderByDescending(x => x.Prescription.CreatedAt);

        var totalItems = await query.CountAsync();
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new PharmacyPrescriptionListDto
            {
                Id = x.Prescription.Id,
                AppointmentId = x.Prescription.AppointmentId,
                AppointmentCode = x.Appointment.AppointmentCode,
                AppointmentDate = x.Appointment.AppointmentDate,
                PatientName = x.PatientName,
                PatientPhone = x.PatientPhone,
                DoctorName = x.DoctorName,
                Status = x.Prescription.Status.ToString(),
                ItemCount = x.Prescription.Items.Count,
                CreatedAt = x.Prescription.CreatedAt,
                DispensedAt = x.Prescription.DispensedAt,
                Notes = x.Prescription.Notes
            })
            .ToListAsync();

        return new PagedResult<PharmacyPrescriptionListDto>(items, totalItems, page, pageSize);
    }

    public async Task<PrescriptionDetailDto> GetPrescriptionByIdAsync(long id)
    {
        var prescription = await _dbContext.Prescriptions
            .AsNoTracking()
            .Include(p => p.Items)
                .ThenInclude(i => i.Medicine)
            .Include(p => p.Appointment)
            .Include(p => p.Patient)
            .Include(p => p.Doctor)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (prescription == null) throw new NotFoundException("Đơn thuốc không tồn tại.");

        var patientUser = prescription.Patient != null 
            ? await _dbContext.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == prescription.Patient.UserId) 
            : null;
        var doctorUser = prescription.Doctor != null 
            ? await _dbContext.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == prescription.Doctor.UserId) 
            : null;

        return new PrescriptionDetailDto
        {
            Id = prescription.Id,
            AppointmentId = prescription.AppointmentId,
            AppointmentCode = prescription.Appointment?.AppointmentCode ?? $"APT-{prescription.AppointmentId}",
            PatientId = prescription.PatientId,
            PatientName = patientUser?.FullName ?? "Bệnh nhân",
            PatientPhone = patientUser?.PhoneNumber ?? "",
            DoctorId = prescription.DoctorId,
            DoctorName = doctorUser?.FullName ?? "Bác sĩ",
            Status = prescription.Status.ToString(),
            Notes = prescription.Notes,
            CreatedAt = prescription.CreatedAt,
            DispensedAt = prescription.DispensedAt,
            Items = prescription.Items.Select(i => new PrescriptionDetailItemDto
            {
                MedicineId = i.MedicineId,
                MedicineCode = i.Medicine?.Code ?? "",
                MedicineName = i.Medicine?.Name ?? "Thuốc",
                Unit = i.Medicine?.Unit ?? "Hộp",
                Quantity = i.Quantity,
                AvailableStock = i.Medicine?.StockQuantity ?? 0,
                Dosage = i.Dosage,
                Frequency = i.Frequency,
                DurationDays = i.DurationDays,
                Instructions = i.Instructions
            }).ToList()
        };
    }

    public async Task<DispensePrescriptionResultDto> DispensePrescriptionAsync(long prescriptionId)
    {
        var actorUserId = _currentUserService.UserId ?? Guid.Empty;

        using var transaction = await _dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        try
        {
            var prescription = await _dbContext.Prescriptions
                .Include(p => p.Items)
                    .ThenInclude(i => i.Medicine)
                .FirstOrDefaultAsync(p => p.Id == prescriptionId);

            if (prescription == null)
                throw new NotFoundException("Đơn thuốc không tồn tại.");

            if (prescription.Status == PrescriptionStatus.Dispensed)
                throw new BusinessException("ALREADY_DISPENSED", "Đơn thuốc này đã được cấp phát trước đó.");

            if (prescription.Items == null || prescription.Items.Count == 0)
                throw new BusinessException("EMPTY_PRESCRIPTION", "Đơn thuốc không có danh mục thuốc để cấp.");

            // 1. Validate stock availability for all items
            foreach (var item in prescription.Items)
            {
                var med = item.Medicine ?? await _dbContext.Medicines.FirstOrDefaultAsync(m => m.Id == item.MedicineId);
                if (med == null)
                    throw new NotFoundException($"Thuốc ID #{item.MedicineId} không tồn tại trong hệ thống.");

                if (med.StockQuantity < item.Quantity)
                {
                    throw new BusinessException(
                        "INSUFFICIENT_STOCK",
                        $"Thuốc '{med.Name}' không đủ tồn kho để cấp phát! Yêu cầu: {item.Quantity} {med.Unit}, Hiện còn: {med.StockQuantity} {med.Unit}."
                    );
                }
            }

            // 2. Deduct stock and log transactions
            foreach (var item in prescription.Items)
            {
                var med = item.Medicine!;
                med.StockQuantity -= item.Quantity;
                med.UpdatedAt = DateTime.UtcNow;

                _dbContext.MedicineStockTransactions.Add(new MedicineStockTransaction
                {
                    MedicineId = med.Id,
                    Type = MedicineStockTransactionType.Dispense,
                    QuantityChange = -item.Quantity,
                    BalanceAfter = med.StockQuantity,
                    PrescriptionId = prescription.Id,
                    ActorUserId = actorUserId,
                    Reason = $"Cấp phát thuốc cho đơn #{prescription.Id}",
                    CreatedAt = DateTime.UtcNow
                });
            }

            // 3. Update prescription status
            prescription.Status = PrescriptionStatus.Dispensed;
            prescription.DispensedAt = DateTime.UtcNow;
            prescription.DispensedByUserId = actorUserId;

            await _dbContext.SaveChangesAsync();
            await transaction.CommitAsync();

            return new DispensePrescriptionResultDto
            {
                PrescriptionId = prescription.Id,
                DispensedAt = prescription.DispensedAt.Value,
                Message = "Cấp phát thuốc và trừ tồn kho thành công."
            };
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<PagedResult<StockTransactionDto>> GetStockTransactionsAsync(long? medicineId, int page, int pageSize)
    {
        var query = from t in _dbContext.MedicineStockTransactions.AsNoTracking()
                    join m in _dbContext.Medicines.AsNoTracking() on t.MedicineId equals m.Id
                    join u in _dbContext.Users.AsNoTracking() on t.ActorUserId equals u.Id into uJoin
                    from actor in uJoin.DefaultIfEmpty()
                    select new
                    {
                        Transaction = t,
                        MedicineCode = m.Code,
                        MedicineName = m.Name,
                        MedicineUnit = m.Unit,
                        ActorName = actor != null ? actor.FullName : "Hệ thống"
                    };

        if (medicineId.HasValue && medicineId.Value > 0)
        {
            query = query.Where(x => x.Transaction.MedicineId == medicineId.Value);
        }

        query = query.OrderByDescending(x => x.Transaction.CreatedAt);

        var totalItems = await query.CountAsync();
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new StockTransactionDto
            {
                Id = x.Transaction.Id,
                MedicineId = x.Transaction.MedicineId,
                MedicineCode = x.MedicineCode,
                MedicineName = x.MedicineName,
                Unit = x.MedicineUnit,
                Type = x.Transaction.Type.ToString(),
                QuantityChange = x.Transaction.QuantityChange,
                BalanceAfter = x.Transaction.BalanceAfter,
                PrescriptionId = x.Transaction.PrescriptionId,
                Reason = x.Transaction.Reason,
                ActorName = x.ActorName,
                CreatedAt = x.Transaction.CreatedAt
            })
            .ToListAsync();

        return new PagedResult<StockTransactionDto>(items, totalItems, page, pageSize);
    }

    public async Task AdjustStockAsync(AdjustStockDto request)
    {
        var actorUserId = _currentUserService.UserId ?? Guid.Empty;

        using var transaction = await _dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        try
        {
            var med = await _dbContext.Medicines.FirstOrDefaultAsync(m => m.Id == request.MedicineId);
            if (med == null) throw new NotFoundException("Thuốc không tồn tại.");

            int qtyChange = request.Type == MedicineStockTransactionType.StockIn 
                ? request.Quantity 
                : request.Quantity; // In case of manual adjustment, can be positive

            med.StockQuantity += qtyChange;
            if (med.StockQuantity < 0)
                throw new BusinessException("NEGATIVE_STOCK", "Số lượng tồn kho không thể âm.");

            med.UpdatedAt = DateTime.UtcNow;

            _dbContext.MedicineStockTransactions.Add(new MedicineStockTransaction
            {
                MedicineId = med.Id,
                Type = request.Type,
                QuantityChange = qtyChange,
                BalanceAfter = med.StockQuantity,
                ActorUserId = actorUserId,
                Reason = string.IsNullOrWhiteSpace(request.Reason) 
                    ? (request.Type == MedicineStockTransactionType.StockIn ? "Nhập thêm hàng vào kho" : "Điều chỉnh tồn kho")
                    : request.Reason.Trim(),
                CreatedAt = DateTime.UtcNow
            });

            await _dbContext.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}
