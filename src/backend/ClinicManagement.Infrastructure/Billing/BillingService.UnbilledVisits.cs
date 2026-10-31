using ClinicManagement.Application.Billing.DTOs;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Common;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Billing;

public partial class BillingService
{
    public async Task<PagedResult<UnbilledVisitDto>> GetUnbilledVisitsAsync(long? facilityId, Guid userId, int page = 1, int pageSize = 10, CancellationToken cancellationToken = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 10;
        if (pageSize > 100) pageSize = 100;

        var isGlobalAdmin = await _facilityAuthService.HasFullFacilityAccessAsync(userId, cancellationToken);
        var allowedFacilityIds = await _facilityAuthService.GetUserAccessibleFacilityIdsAsync(userId, cancellationToken);

        if (!isGlobalAdmin && allowedFacilityIds.Count == 0)
        {
            return new PagedResult<UnbilledVisitDto>(new List<UnbilledVisitDto>(), 0, page, pageSize);
        }

        if (facilityId.HasValue && facilityId.Value > 0)
        {
            await _facilityAuthService.ValidateUserFacilityAccessAsync(userId, facilityId.Value, cancellationToken);
        }

        var baseQuery = _dbContext.PatientVisits
            .AsNoTracking()
            .Where(v => v.Status != VisitStatus.Cancelled && v.Status != VisitStatus.NoShow);

        if (facilityId.HasValue && facilityId.Value > 0)
        {
            baseQuery = baseQuery.Where(v => v.FacilityId == facilityId.Value);
        }
        else if (!isGlobalAdmin)
        {
            baseQuery = baseQuery.Where(v => allowedFacilityIds.Contains(v.FacilityId));
        }

        // Database-level filtering: Only select visits that have unbilled charges, pending unpaid invoices, or are in billing status
        baseQuery = baseQuery.Where(v =>
            v.Status == VisitStatus.InBilling
            || _dbContext.Invoices.Any(i => i.PatientVisitId == v.Id && i.Status == InvoiceStatus.Unpaid)
            || ((v.Department != null && v.Department.Specialty != null && v.Department.Specialty.ConsultationFee > 0)
                && !_dbContext.InvoiceItems.Any(ii => !ii.IsCancelled && ii.Invoice.Status != InvoiceStatus.Cancelled && ii.ReferenceType == "Consultation" && ii.ReferenceId == v.Id))
            || v.DiagnosticOrders.Any(o => o.Status != DiagnosticOrderStatus.Cancelled && o.Items.Any(i => i.Status != DiagnosticItemStatus.Cancelled && !_dbContext.InvoiceItems.Any(ii => !ii.IsCancelled && ii.Invoice.Status != InvoiceStatus.Cancelled && ii.ReferenceType == "DiagnosticItem" && ii.ReferenceId == i.Id)))
            || v.Prescriptions.Any(p => (p.Status == PrescriptionStatus.ReservedForPurchase || p.Status == PrescriptionStatus.Issued || p.Status == PrescriptionStatus.Dispensed) && p.Items.Any(pi => !_dbContext.InvoiceItems.Any(ii => !ii.IsCancelled && ii.Invoice.Status != InvoiceStatus.Cancelled && (((ii.ReferenceType == "PrescriptionItem:v2" || ii.ReferenceType == "PrescriptionItem") && ii.ReferenceId == p.Id * 4294967296L + pi.MedicineId) || (ii.ReferenceType == "PrescriptionItem" && (ii.ReferenceId == p.Id * 100000L + pi.MedicineId || ii.ReferenceId == p.Id))))))
        );

        var totalCount = await baseQuery.CountAsync(cancellationToken);

        var visits = await baseQuery
            .Include(v => v.Patient)
            .Include(v => v.Department)
                .ThenInclude(d => d!.Specialty)
            .Include(v => v.AssignedDoctor)
            .Include(v => v.DiagnosticOrders)
                .ThenInclude(o => o.Items)
                    .ThenInclude(i => i.DiagnosticService)
            .Include(v => v.Prescriptions)
                .ThenInclude(p => p.Items)
                    .ThenInclude(pi => pi.Medicine)
            .OrderByDescending(v => v.VisitDate)
            .ThenByDescending(v => v.QueueNumber)
            .ThenByDescending(v => v.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var visitIds = visits.Select(v => v.Id).ToList();

        var billedItemKeys = await _dbContext.InvoiceItems
            .Where(ii => !ii.IsCancelled && ii.Invoice.Status != InvoiceStatus.Cancelled)
            .Where(ii => ii.Invoice.PatientVisitId.HasValue && visitIds.Contains(ii.Invoice.PatientVisitId.Value))
            .Select(ii => new { VisitId = ii.Invoice.PatientVisitId!.Value, Key = $"{ii.ReferenceType}:{ii.ReferenceId}" })
            .ToListAsync(cancellationToken);

        var billedMap = billedItemKeys
            .GroupBy(x => x.VisitId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Key).ToHashSet());

        var unpaidInvoices = await _dbContext.Invoices
            .Where(i => i.PatientVisitId.HasValue && visitIds.Contains(i.PatientVisitId.Value) && i.Status == InvoiceStatus.Unpaid)
            .Select(i => new { VisitId = i.PatientVisitId!.Value, i.TotalAmount })
            .ToListAsync(cancellationToken);

        var unpaidMap = unpaidInvoices
            .GroupBy(x => x.VisitId)
            .ToDictionary(g => g.Key, g => new { Count = g.Count(), Total = g.Sum(x => x.TotalAmount) });

        var userIds = visits.Where(v => v.Patient.UserId.HasValue).Select(v => v.Patient.UserId!.Value)
            .Concat(visits.Where(v => v.AssignedDoctor != null).Select(v => v.AssignedDoctor!.UserId))
            .Distinct()
            .ToList();

        var userMap = await _dbContext.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, cancellationToken);

        var result = new List<UnbilledVisitDto>();

        foreach (var v in visits)
        {
            billedMap.TryGetValue(v.Id, out var billedSet);
            billedSet ??= new HashSet<string>();

            var unbilledCount = 0;
            var estTotal = 0m;

            // Check consultation fee
            if (!billedSet.Contains($"Consultation:{v.Id}"))
            {
                var fee = v.Department?.Specialty?.ConsultationFee ?? 0m;
                if (fee > 0)
                {
                    unbilledCount++;
                    estTotal += fee;
                }
            }

            // Check diagnostics
            foreach (var o in v.DiagnosticOrders.Where(o => o.Status != DiagnosticOrderStatus.Cancelled))
            {
                foreach (var item in o.Items.Where(i => i.Status != DiagnosticItemStatus.Cancelled))
                {
                    if (!billedSet.Contains($"DiagnosticItem:{item.Id}"))
                    {
                        unbilledCount++;
                        if (!item.IsPackageCovered && item.DiagnosticService?.Price.HasValue == true)
                        {
                            estTotal += item.DiagnosticService.Price.Value;
                        }
                    }
                }
            }

            // Check prescriptions
            foreach (var p in v.Prescriptions.Where(p => p.Status == PrescriptionStatus.ReservedForPurchase || p.Status == PrescriptionStatus.Issued || p.Status == PrescriptionStatus.Dispensed))
            {
                foreach (var item in p.Items)
                {
                    var refId = PrescriptionItemBillingReference.Encode(p.Id, item.MedicineId);
                    var legacyRefId = p.Id * 100000L + item.MedicineId;
                    if (!billedSet.Contains($"PrescriptionItem:v2:{refId}") &&
                        !billedSet.Contains($"PrescriptionItem:{refId}") &&
                        !billedSet.Contains($"PrescriptionItem:{legacyRefId}") &&
                        !billedSet.Contains($"PrescriptionItem:{p.Id}"))
                    {
                        unbilledCount++;
                        if (item.Medicine?.UnitPrice.HasValue == true)
                        {
                            estTotal += item.Medicine.UnitPrice.Value * item.Quantity;
                        }
                    }
                }
            }

            unpaidMap.TryGetValue(v.Id, out var unpaidInfo);

            // If no unbilled items left to bill, but unpaid invoice exists, reflect unpaid invoice amount
            if (unbilledCount == 0 && unpaidInfo != null)
            {
                unbilledCount = unpaidInfo.Count;
                estTotal = unpaidInfo.Total;
            }
            else if (unpaidInfo != null)
            {
                estTotal += unpaidInfo.Total;
            }

            var patName = v.Patient.UserId.HasValue && userMap.TryGetValue(v.Patient.UserId.Value, out var pn) ? pn : (v.Patient.FullName ?? "Bệnh nhân");
            var docName = v.AssignedDoctor != null && userMap.TryGetValue(v.AssignedDoctor.UserId, out var dn) ? dn : "Bác sĩ";

            result.Add(new UnbilledVisitDto
            {
                HasPendingPayment = unbilledCount > 0 || unpaidInfo != null,
                VisitId = v.Id,
                VisitCode = v.VisitCode,
                PatientId = v.PatientId,
                PatientName = patName,
                MedicalRecordNumber = v.Patient.MedicalRecordNumber,
                PhoneNumber = v.Patient.PhoneNumber,
                DepartmentName = v.Department?.Name ?? "Phòng khám",
                DoctorName = docName,
                VisitDate = v.VisitDate,
                Status = v.Status.ToString(),
                UnbilledItemCount = unbilledCount,
                EstimatedTotal = estTotal
            });
        }

        return new PagedResult<UnbilledVisitDto>(result, totalCount, page, pageSize);
    }

}
