using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Billing;

internal static class UnbilledVisitQuery
{
    internal static IQueryable<PatientVisit> Create(AppDbContext dbContext) =>
        dbContext.PatientVisits
            .AsNoTracking()
            .Where(v => v.Status != VisitStatus.Cancelled && v.Status != VisitStatus.NoShow)
            .Where(v =>
                v.Status == VisitStatus.InBilling
                || dbContext.Invoices.Any(i => i.PatientVisitId == v.Id && i.Status == InvoiceStatus.Unpaid)
                || ((v.Department != null && v.Department.Specialty != null && v.Department.Specialty.ConsultationFee > 0)
                    && !dbContext.InvoiceItems.Any(ii => !ii.IsCancelled && ii.Invoice.Status != InvoiceStatus.Cancelled && ii.ReferenceType == "Consultation" && ii.ReferenceId == v.Id))
                || v.DiagnosticOrders.Any(o => o.Status != DiagnosticOrderStatus.Cancelled && o.Items.Any(i => i.Status != DiagnosticItemStatus.Cancelled && !dbContext.InvoiceItems.Any(ii => !ii.IsCancelled && ii.Invoice.Status != InvoiceStatus.Cancelled && ii.ReferenceType == "DiagnosticItem" && ii.ReferenceId == i.Id)))
                || v.Prescriptions.Any(p => (p.Status == PrescriptionStatus.ReservedForPurchase || p.Status == PrescriptionStatus.Issued || p.Status == PrescriptionStatus.Dispensed) && p.Items.Any(pi => !dbContext.InvoiceItems.Any(ii => !ii.IsCancelled && ii.Invoice.Status != InvoiceStatus.Cancelled && (((ii.ReferenceType == "PrescriptionItem:v2" || ii.ReferenceType == "PrescriptionItem") && ii.ReferenceId == p.Id * 4294967296L + pi.MedicineId) || (ii.ReferenceType == "PrescriptionItem" && (ii.ReferenceId == p.Id * 100000L + pi.MedicineId || ii.ReferenceId == p.Id))))))
            );
}
