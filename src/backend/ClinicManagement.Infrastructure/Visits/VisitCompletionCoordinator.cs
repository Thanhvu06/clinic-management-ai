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
            .Include(v => v.Appointment)
                .ThenInclude(a => a!.Specialty)
            .Include(v => v.Department)
                .ThenInclude(d => d!.Specialty)
            .FirstOrDefaultAsync(v => v.Id == visitId, cancellationToken);

        if (visit == null)
            return (false, "Lượt khám không tồn tại.");

        if (visit.Status == VisitStatus.Cancelled)
            return (false, "Lượt khám đã bị hủy.");

        var appointmentId = visit.AppointmentId;

        // 1. Clinical consultation check: Doctor must have completed clinical encounter
        var hasCompletedSummary = (visit.VisitSummary != null && visit.VisitSummary.CompletedAtUtc.HasValue) ||
                                  dbContext.VisitSummaries.Local.Any(
                                      s => (s.PatientVisitId == visit.Id || (appointmentId.HasValue && s.AppointmentId == appointmentId.Value)) &&
                                           s.CompletedAtUtc.HasValue) ||
                                  await dbContext.VisitSummaries.AnyAsync(
                                      s => (s.PatientVisitId == visit.Id || (appointmentId.HasValue && s.AppointmentId == appointmentId.Value)) &&
                                           s.CompletedAtUtc.HasValue,
                                      cancellationToken);

        if (!hasCompletedSummary)
            return (false, "Bác sĩ chưa hoàn tất phiên khám lâm sàng.");

        // 2. Diagnostic orders check
        var diagnosticOrders = await dbContext.DiagnosticOrders
            .Include(o => o.Items)
            .Where(o => (o.PatientVisitId == visit.Id || (appointmentId.HasValue && o.AppointmentId == appointmentId.Value)) &&
                        o.Status != DiagnosticOrderStatus.Cancelled)
            .ToListAsync(cancellationToken);

        var pendingOrders = diagnosticOrders
            .Where(o => o.Status == DiagnosticOrderStatus.Ordered || o.Status == DiagnosticOrderStatus.InProgress)
            .ToList();

        if (pendingOrders.Count > 0)
            return (false, "Còn chỉ định cận lâm sàng đang chờ thực hiện hoặc chờ kết quả.");

        var unreviewedOrders = diagnosticOrders
            .Where(o => o.Status == DiagnosticOrderStatus.Completed && !o.ReviewedAtUtc.HasValue)
            .ToList();

        if (unreviewedOrders.Count > 0)
            return (false, "Kết quả cận lâm sàng chưa được bác sĩ xem và duyệt.");

        // 3. Invoices check: No unpaid invoices
        var invoices = await dbContext.Invoices
            .Include(i => i.Items)
            .Where(i => (i.PatientVisitId == visit.Id || (appointmentId.HasValue && i.AppointmentId == appointmentId.Value)) &&
                        i.Status != InvoiceStatus.Cancelled)
            .ToListAsync(cancellationToken);

        if (invoices.Any(i => i.Status == InvoiceStatus.Unpaid))
            return (false, "Còn hóa đơn viện phí chưa thanh toán.");

        // 4. Prescription dispensing check
        var prescriptions = await dbContext.Prescriptions
            .Include(p => p.Items)
            .Where(p => (p.PatientVisitId == visit.Id || (appointmentId.HasValue && p.AppointmentId == appointmentId.Value)) &&
                        p.Status != PrescriptionStatus.Cancelled)
            .ToListAsync(cancellationToken);

        var undispensedRx = prescriptions
            .Where(p => (p.Status == PrescriptionStatus.Issued || p.Status == PrescriptionStatus.ReservedForPurchase) && p.Items.Count > 0)
            .ToList();

        if (undispensedRx.Count > 0)
            return (false, "Đơn thuốc chưa được cấp phát tại quầy dược.");

        // 5. Unbilled obligations check
        // A. Consultation fee check
        var consultationFee = visit.Department?.Specialty?.ConsultationFee ??
                              visit.Appointment?.Specialty?.ConsultationFee ?? 0m;

        if (consultationFee > 0)
        {
            var isConsultationPaid = invoices
                .Where(i => i.Status == InvoiceStatus.Paid)
                .SelectMany(i => i.Items)
                .Any(ii => !ii.IsCancelled &&
                           (ii.ReferenceType == "Consultation" || ii.ReferenceType == "Appointment") &&
                           (ii.ReferenceId == visit.Id || (appointmentId.HasValue && ii.ReferenceId == appointmentId.Value)));

            if (!isConsultationPaid)
                return (false, "Tiền khám chưa được lập hóa đơn hoặc chưa thanh toán.");
        }

        // B. Diagnostic items check (non-package covered items must be paid)
        foreach (var order in diagnosticOrders)
        {
            foreach (var item in order.Items.Where(i => i.Status != DiagnosticItemStatus.Cancelled && !i.IsPackageCovered))
            {
                var isDiagPaid = invoices
                    .Where(i => i.Status == InvoiceStatus.Paid)
                    .SelectMany(i => i.Items)
                    .Any(ii => !ii.IsCancelled && ii.ReferenceType == "DiagnosticItem" && ii.ReferenceId == item.Id);

                if (!isDiagPaid)
                    return (false, "Chỉ định cận lâm sàng chưa được lập hóa đơn hoặc chưa thanh toán.");
            }
        }

        // C. Prescription items check (all medicines in active prescriptions must be paid)
        var activePrescriptions = prescriptions
            .Where(p => p.Status != PrescriptionStatus.Draft && p.Items.Count > 0)
            .ToList();

        if (activePrescriptions.Count > 0)
        {
            var paidItems = invoices
                .Where(i => i.Status == InvoiceStatus.Paid)
                .SelectMany(i => i.Items)
                .Where(ii => !ii.IsCancelled)
                .ToList();

            foreach (var rx in activePrescriptions)
            {
                foreach (var item in rx.Items)
                {
                    var paidQty = paidItems
                        .Where(ii => PrescriptionItemBillingReference.TryDecodeMedicineId(ii.ReferenceType, ii.ReferenceId, rx.Id, out var medId) && medId == item.MedicineId)
                        .Sum(ii => ii.Quantity);

                    if (paidQty < item.Quantity)
                        return (false, "Thuốc trong đơn chưa được lập hóa đơn hoặc chưa thanh toán đủ số lượng.");
                }
            }
        }

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
                .ThenInclude(a => a!.Specialty)
            .Include(v => v.Department)
                .ThenInclude(d => d!.Specialty)
            .FirstOrDefaultAsync(v => v.Id == visitId, cancellationToken);

        if (visit == null || visit.Status == VisitStatus.Cancelled)
            return visit?.Status ?? VisitStatus.Cancelled;

        var appointmentId = visit.AppointmentId;

        // Check clinical consultation completion evidence
        var hasCompletedSummary = (visit.VisitSummary != null && visit.VisitSummary.CompletedAtUtc.HasValue) ||
                                  dbContext.VisitSummaries.Local.Any(
                                      s => (s.PatientVisitId == visit.Id || (appointmentId.HasValue && s.AppointmentId == appointmentId.Value)) &&
                                           s.CompletedAtUtc.HasValue) ||
                                  await dbContext.VisitSummaries.AnyAsync(
                                      s => (s.PatientVisitId == visit.Id || (appointmentId.HasValue && s.AppointmentId == appointmentId.Value)) &&
                                           s.CompletedAtUtc.HasValue,
                                      cancellationToken);

        if (!hasCompletedSummary)
        {
            // Clinical consultation not yet finished by doctor.
            // Ensure CompletedAtUtc is not set.
            if (visit.CompletedAtUtc.HasValue)
            {
                visit.CompletedAtUtc = null;
                visit.UpdatedAtUtc = nowUtc;
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            return visit.Status;
        }

        // Diagnostic orders
        var diagnosticOrders = await dbContext.DiagnosticOrders
            .Include(o => o.Items)
            .Where(o => (o.PatientVisitId == visit.Id || (appointmentId.HasValue && o.AppointmentId == appointmentId.Value)) &&
                        o.Status != DiagnosticOrderStatus.Cancelled)
            .ToListAsync(cancellationToken);

        var pendingOrders = diagnosticOrders
            .Where(o => o.Status == DiagnosticOrderStatus.Ordered || o.Status == DiagnosticOrderStatus.InProgress)
            .ToList();

        var unreviewedOrders = diagnosticOrders
            .Where(o => o.Status == DiagnosticOrderStatus.Completed && !o.ReviewedAtUtc.HasValue)
            .ToList();

        // Prescriptions
        var prescriptions = await dbContext.Prescriptions
            .Include(p => p.Items)
            .Where(p => (p.PatientVisitId == visit.Id || (appointmentId.HasValue && p.AppointmentId == appointmentId.Value)) &&
                        p.Status != PrescriptionStatus.Cancelled)
            .ToListAsync(cancellationToken);

        var undispensedRx = prescriptions
            .Where(p => (p.Status == PrescriptionStatus.Issued || p.Status == PrescriptionStatus.ReservedForPurchase) && p.Items.Count > 0)
            .ToList();

        // Invoices
        var invoices = await dbContext.Invoices
            .Include(i => i.Items)
            .Where(i => (i.PatientVisitId == visit.Id || (appointmentId.HasValue && i.AppointmentId == appointmentId.Value)) &&
                        i.Status != InvoiceStatus.Cancelled)
            .ToListAsync(cancellationToken);

        var hasUnpaidInvoices = invoices.Any(i => i.Status == InvoiceStatus.Unpaid);

        // Check unbilled obligations
        bool hasUnbilledObligations = false;

        // A. Consultation fee
        var consultationFee = visit.Department?.Specialty?.ConsultationFee ??
                              visit.Appointment?.Specialty?.ConsultationFee ?? 0m;

        if (consultationFee > 0)
        {
            var isConsultationPaid = invoices
                .Where(i => i.Status == InvoiceStatus.Paid)
                .SelectMany(i => i.Items)
                .Any(ii => !ii.IsCancelled &&
                           (ii.ReferenceType == "Consultation" || ii.ReferenceType == "Appointment") &&
                           (ii.ReferenceId == visit.Id || (appointmentId.HasValue && ii.ReferenceId == appointmentId.Value)));

            if (!isConsultationPaid)
                hasUnbilledObligations = true;
        }

        // B. Diagnostic items
        if (!hasUnbilledObligations)
        {
            foreach (var order in diagnosticOrders)
            {
                foreach (var item in order.Items.Where(i => i.Status != DiagnosticItemStatus.Cancelled && !i.IsPackageCovered))
                {
                    var isDiagPaid = invoices
                        .Where(i => i.Status == InvoiceStatus.Paid)
                        .SelectMany(i => i.Items)
                        .Any(ii => !ii.IsCancelled && ii.ReferenceType == "DiagnosticItem" && ii.ReferenceId == item.Id);

                    if (!isDiagPaid)
                    {
                        hasUnbilledObligations = true;
                        break;
                    }
                }
                if (hasUnbilledObligations) break;
            }
        }

        // C. Prescription items
        if (!hasUnbilledObligations)
        {
            var activePrescriptions = prescriptions
                .Where(p => p.Status != PrescriptionStatus.Draft && p.Items.Count > 0)
                .ToList();

            if (activePrescriptions.Count > 0)
            {
                var paidItems = invoices
                    .Where(i => i.Status == InvoiceStatus.Paid)
                    .SelectMany(i => i.Items)
                    .Where(ii => !ii.IsCancelled)
                    .ToList();

                foreach (var rx in activePrescriptions)
                {
                    foreach (var item in rx.Items)
                    {
                        var paidQty = paidItems
                            .Where(ii => PrescriptionItemBillingReference.TryDecodeMedicineId(ii.ReferenceType, ii.ReferenceId, rx.Id, out var medId) && medId == item.MedicineId)
                            .Sum(ii => ii.Quantity);

                        if (paidQty < item.Quantity)
                        {
                            hasUnbilledObligations = true;
                            break;
                        }
                    }
                    if (hasUnbilledObligations) break;
                }
            }
        }

        // Determine progress state
        if (pendingOrders.Count > 0)
        {
            if (visit.Status != VisitStatus.InConsultation)
            {
                visit.Status = VisitStatus.WaitingForDiagnostics;
            }
            visit.CompletedAtUtc = null;
        }
        else if (unreviewedOrders.Count > 0)
        {
            if (visit.Status != VisitStatus.InConsultation)
            {
                visit.Status = VisitStatus.ResultsReady;
            }
            visit.CompletedAtUtc = null;
        }
        else if (undispensedRx.Count > 0)
        {
            visit.Status = VisitStatus.InPharmacy;
            visit.CompletedAtUtc = null;
        }
        else if (hasUnpaidInvoices || hasUnbilledObligations)
        {
            visit.Status = VisitStatus.InBilling;
            visit.CompletedAtUtc = null;
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
