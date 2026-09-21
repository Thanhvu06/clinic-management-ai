using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Common;
using ClinicManagement.Infrastructure.Persistence;

namespace ClinicManagement.Infrastructure.Visits;

/// <summary>
/// Central coordinator for evaluating visit progression and safe completion conditions.
/// Ensures that visits only transition to Completed when all clinical, diagnostic, pharmacy,
/// and billing requirements are genuinely fulfilled for both walk-in and appointment visits.
/// </summary>
public static class VisitCompletionCoordinator
{
    /// <summary>
    /// Checks whether a patient visit satisfies all criteria to be marked as Completed.
    /// Returns (true, null) if eligible, or (false, reason) detailing the blocker.
    /// </summary>
    public static async Task<(bool CanComplete, string? Reason)> CanCompleteVisitAsync(
        long visitId,
        AppDbContext dbContext,
        CancellationToken cancellationToken = default)
    {
        var visit = await dbContext.PatientVisits
            .Include(v => v.Department)
                .ThenInclude(d => d!.Specialty)
            .FirstOrDefaultAsync(v => v.Id == visitId, cancellationToken);

        if (visit == null)
            return (false, "Lượt khám không tồn tại.");

        if (visit.Status == VisitStatus.Cancelled)
            return (false, "Lượt khám đã bị hủy.");

        var appointmentId = visit.AppointmentId;

        // Diagnostic orders check
        var diagnosticOrders = await dbContext.DiagnosticOrders
            .Where(o => o.PatientVisitId == visit.Id || (appointmentId.HasValue && o.AppointmentId == appointmentId.Value))
            .ToListAsync(cancellationToken);

        var pendingOrders = diagnosticOrders
            .Where(o => o.Status == DiagnosticOrderStatus.Ordered || o.Status == DiagnosticOrderStatus.InProgress)
            .ToList();

        if (pendingOrders.Count > 0)
            return (false, "Còn chỉ định cận lâm sàng đang chờ thực hiện hoặc chờ kết quả.");

        // Prescription dispensing check
        var prescriptions = await dbContext.Prescriptions
            .Include(p => p.Items)
            .Where(p => p.PatientVisitId == visit.Id || (appointmentId.HasValue && p.AppointmentId == appointmentId.Value))
            .ToListAsync(cancellationToken);

        var undispensedRx = prescriptions
            .Where(p => (p.Status == PrescriptionStatus.Issued || p.Status == PrescriptionStatus.ReservedForPurchase) && p.Items.Count > 0)
            .ToList();

        if (undispensedRx.Count > 0)
            return (false, "Đơn thuốc chưa được cấp phát tại quầy dược.");

        // Clinical consultation check
        var isConsultationDone = visit.Status == VisitStatus.ConsultationCompleted ||
                                 visit.Status == VisitStatus.InPharmacy ||
                                 visit.Status == VisitStatus.InBilling ||
                                 visit.Status == VisitStatus.Completed ||
                                 prescriptions.Any(p => p.Status == PrescriptionStatus.Dispensed) ||
                                 await dbContext.VisitSummaries.AnyAsync(
                                     s => (s.PatientVisitId == visit.Id || (appointmentId.HasValue && s.AppointmentId == appointmentId.Value)) &&
                                          s.CompletedAtUtc.HasValue,
                                     cancellationToken);

        if (!isConsultationDone)
            return (false, "Bác sĩ chưa hoàn tất phiên khám lâm sàng.");

        // Invoices check
        var invoices = await dbContext.Invoices
            .Where(i => i.PatientVisitId == visit.Id || (appointmentId.HasValue && i.AppointmentId == appointmentId.Value))
            .ToListAsync(cancellationToken);

        if (invoices.Any(i => i.Status == InvoiceStatus.Unpaid))
            return (false, "Còn hóa đơn viện phí chưa thanh toán.");

        return (true, null);
    }

    /// <summary>
    /// Evaluates the current state of a visit and updates its progress (Status, CompletedAtUtc, etc.).
    /// Call this after payment, after dispensing, or after clinical consultation completion.
    /// </summary>
    public static async Task<VisitStatus> TryUpdateVisitProgressAsync(
        long visitId,
        AppDbContext dbContext,
        DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        var visit = await dbContext.PatientVisits
            .Include(v => v.Appointment)
            .Include(v => v.Department)
                .ThenInclude(d => d!.Specialty)
            .FirstOrDefaultAsync(v => v.Id == visitId, cancellationToken);

        if (visit == null || visit.Status == VisitStatus.Cancelled)
            return visit?.Status ?? VisitStatus.Cancelled;

        var appointmentId = visit.AppointmentId;

        // Prescriptions
        var prescriptions = await dbContext.Prescriptions
            .Include(p => p.Items)
            .Where(p => p.PatientVisitId == visit.Id || (appointmentId.HasValue && p.AppointmentId == appointmentId.Value))
            .ToListAsync(cancellationToken);

        var undispensedRx = prescriptions
            .Where(p => (p.Status == PrescriptionStatus.Issued || p.Status == PrescriptionStatus.ReservedForPurchase) && p.Items.Count > 0)
            .ToList();

        // Check if clinical consultation is completed
        var isConsultationDone = visit.Status == VisitStatus.ConsultationCompleted ||
                                 visit.Status == VisitStatus.InPharmacy ||
                                 visit.Status == VisitStatus.InBilling ||
                                 visit.Status == VisitStatus.Completed ||
                                 prescriptions.Any(p => p.Status == PrescriptionStatus.Dispensed) ||
                                 await dbContext.VisitSummaries.AnyAsync(
                                     s => (s.PatientVisitId == visit.Id || (appointmentId.HasValue && s.AppointmentId == appointmentId.Value)) &&
                                          s.CompletedAtUtc.HasValue,
                                     cancellationToken);

        if (!isConsultationDone)
        {
            // Clinical consultation not yet finished, do not move to Billing/Pharmacy/Completed
            return visit.Status;
        }

        // Diagnostic orders
        var diagnosticOrders = await dbContext.DiagnosticOrders
            .Where(o => o.PatientVisitId == visit.Id || (appointmentId.HasValue && o.AppointmentId == appointmentId.Value))
            .ToListAsync(cancellationToken);

        var pendingOrders = diagnosticOrders
            .Where(o => o.Status == DiagnosticOrderStatus.Ordered || o.Status == DiagnosticOrderStatus.InProgress)
            .ToList();

        // Invoices
        var invoices = await dbContext.Invoices
            .Where(i => i.PatientVisitId == visit.Id || (appointmentId.HasValue && i.AppointmentId == appointmentId.Value))
            .ToListAsync(cancellationToken);

        var hasUnpaidInvoices = invoices.Any(i => i.Status == InvoiceStatus.Unpaid);

        if (undispensedRx.Count > 0)
        {
            if (visit.Status != VisitStatus.InConsultation)
            {
                visit.Status = VisitStatus.InPharmacy;
            }
        }
        else if (hasUnpaidInvoices)
        {
            visit.Status = VisitStatus.InBilling;
        }
        else if (pendingOrders.Count > 0)
        {
            if (visit.Status != VisitStatus.InConsultation)
            {
                visit.Status = VisitStatus.WaitingForDiagnostics;
            }
        }
        else
        {
            visit.Status = VisitStatus.Completed;
            visit.CompletedAtUtc = nowUtc;

            if (visit.Appointment != null && visit.Appointment.Status != AppointmentStatus.Completed)
            {
                visit.Appointment.Status = AppointmentStatus.Completed;
            }
        }

        visit.UpdatedAtUtc = nowUtc;
        await dbContext.SaveChangesAsync(cancellationToken);

        return visit.Status;
    }
}
