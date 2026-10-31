using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Common;
using ClinicManagement.Infrastructure.Notifications;
using ClinicManagement.Infrastructure.Pharmacy;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Billing;

public partial class BillingService
{
    private async Task NotifyPaidPrescriptionsAsync(Invoice invoice, CancellationToken cancellationToken)
    {
        var prescriptions = await _dbContext.Prescriptions.Include(p => p.PatientVisit).Include(p => p.Appointment)
            .Where(p => p.PatientId == invoice.PatientId &&
                (invoice.PatientVisitId.HasValue && p.PatientVisitId == invoice.PatientVisitId ||
                 invoice.AppointmentId.HasValue && p.AppointmentId == invoice.AppointmentId) &&
                (p.Status == PrescriptionStatus.Issued || p.Status == PrescriptionStatus.ReservedForPurchase))
            .ToListAsync(cancellationToken);
        var eligibility = new PrescriptionPaymentEligibilityService(_dbContext);
        foreach (var prescription in prescriptions)
        {
            var facilityId = prescription.PatientVisit?.FacilityId ?? prescription.Appointment?.FacilityId;
            if (!facilityId.HasValue || !(await eligibility.EvaluateAsync(prescription.Id, cancellationToken)).IsFullyPaid) continue;
            await FacilityWorkflowNotifications.SendAsync(_dbContext, _notificationService, facilityId.Value, "Pharmacist",
                NotificationType.Prescription,
                $"Đơn {BusinessDisplay.PrescriptionCode(prescription, _dateTimeProvider)} đã thanh toán, chờ cấp phát",
                "/pharmacy/prescriptions", "Prescription", prescription.Id, "prescription_paid", cancellationToken, title: "Đơn thuốc chờ cấp phát");
        }
    }
}
