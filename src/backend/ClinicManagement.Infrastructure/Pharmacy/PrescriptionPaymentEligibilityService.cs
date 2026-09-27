using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClinicManagement.Application.Pharmacy.DTOs;
using ClinicManagement.Application.Pharmacy.Interfaces;
using ClinicManagement.Infrastructure.Common;
using ClinicManagement.Infrastructure.Persistence;
using ClinicManagement.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Pharmacy;

/// <summary>
/// Shared read-only payment eligibility calculation used by both the pharmacy
/// write path and read-only copilot projections.
/// </summary>
public sealed class PrescriptionPaymentEligibilityService : IPrescriptionPaymentEligibilityService
{
    private readonly AppDbContext _db;

    public PrescriptionPaymentEligibilityService(AppDbContext db) => _db = db;

    public async Task<PrescriptionPaymentEligibilityDto> EvaluateAsync(long prescriptionId, CancellationToken cancellationToken = default)
    {
        var prescription = await _db.Prescriptions.AsNoTracking()
            .Where(p => p.Id == prescriptionId)
            .Select(p => new
            {
                p.Id,
                p.PatientId,
                p.PatientVisitId,
                p.AppointmentId,
                Items = p.Items.Select(i => new
                {
                    i.MedicineId,
                    i.Quantity,
                    MedicineName = i.Medicine == null ? string.Empty : i.Medicine.Name
                }).ToList()
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (prescription is null)
        {
            return new PrescriptionPaymentEligibilityDto
            {
                PrescriptionId = prescriptionId,
                PaymentStatus = "unverifiable"
            };
        }

        if (prescription.Items.Count == 0)
        {
            return new PrescriptionPaymentEligibilityDto
            {
                PrescriptionId = prescription.Id,
                PaymentStatus = "unverifiable"
            };
        }

        var minModern = prescription.Id * 4294967296L;
        var maxModern = (prescription.Id + 1) * 4294967296L - 1;
        var minLegacy = prescription.Id * 100000L;
        var maxLegacy = (prescription.Id + 1) * 100000L - 1;

        var paidInvoiceItems = await _db.InvoiceItems.AsNoTracking()
            .Where(ii => !ii.IsCancelled && ii.Invoice.Status == InvoiceStatus.Paid)
            .Where(ii => ii.Invoice.PatientId == prescription.PatientId &&
                         (prescription.PatientVisitId.HasValue && ii.Invoice.PatientVisitId == prescription.PatientVisitId.Value ||
                          prescription.AppointmentId.HasValue && ii.Invoice.AppointmentId == prescription.AppointmentId.Value))
            .Where(ii => (ii.ReferenceType == PrescriptionItemBillingReference.ModernReferenceType && ii.ReferenceId >= minModern && ii.ReferenceId <= maxModern) ||
                         (ii.ReferenceType == PrescriptionItemBillingReference.LegacyReferenceType && ii.ReferenceId >= minLegacy && ii.ReferenceId <= maxLegacy))
            .Select(ii => new { ii.ReferenceType, ii.ReferenceId, ii.Quantity })
            .ToListAsync(cancellationToken);

        var paidQtyByMedicine = new Dictionary<long, int>();
        foreach (var invoiceItem in paidInvoiceItems)
        {
            if (!PrescriptionItemBillingReference.TryDecodeMedicineId(invoiceItem.ReferenceType, invoiceItem.ReferenceId, prescription.Id, out var medicineId))
                continue;

            paidQtyByMedicine[medicineId] = paidQtyByMedicine.GetValueOrDefault(medicineId) + invoiceItem.Quantity;
        }

        var items = prescription.Items.Select(item =>
        {
            var paidQuantity = paidQtyByMedicine.GetValueOrDefault(item.MedicineId);
            return new PrescriptionItemPaymentStatusDto
            {
                MedicineId = item.MedicineId,
                MedicineName = item.MedicineName,
                RequiredQuantity = item.Quantity,
                PaidQuantity = paidQuantity,
                IsPaidInFull = paidQuantity >= item.Quantity
            };
        }).ToList();

        var allPaid = items.All(item => item.IsPaidInFull);
        var anyPaid = items.Any(item => item.PaidQuantity > 0);
        return new PrescriptionPaymentEligibilityDto
        {
            PrescriptionId = prescription.Id,
            PaymentStatus = allPaid ? "paid_in_full" : anyPaid ? "partially_paid" : "unpaid",
            IsFullyPaid = allPaid,
            Items = items
        };
    }
}
