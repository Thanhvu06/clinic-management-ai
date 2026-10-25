using ClinicManagement.Application.AI.Tools;
using ClinicManagement.Application.Appointments.Interfaces;
using ClinicManagement.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.AI.Tools;

public sealed partial class RoleCopilotToolHandler
{
    private readonly IDoctorAppointmentService? _doctorAppointments;

    private async Task<AiToolExecutionResult> GetDoctorQueueAsync(AiToolExecutionContext context, CancellationToken cancellationToken)
    {
        var facilities = await ResolveFacilityScopeAsync(context, nameof(AiActorRole.Doctor), cancellationToken);
        if (facilities.Count == 0) return ScopeDenied();
        if (_doctorAppointments is null) return AiToolExecutionResult.Failed("DATA_UNAVAILABLE", "Chưa thể đọc bàn làm việc bác sĩ.");
        var dashboard = await _doctorAppointments.GetDoctorDashboardAsync(_clock.VietnamToday);
        var appointmentIds = dashboard.Queue.Where(q => q.AppointmentId.HasValue).Select(q => q.AppointmentId!.Value).ToList();
        var visitIds = dashboard.Queue.Where(q => q.PatientVisitId.HasValue).Select(q => q.PatientVisitId!.Value).ToList();
        var allowedAppointments = await _db.Appointments.AsNoTracking()
            .Where(a => appointmentIds.Contains(a.Id) && a.FacilityId.HasValue && facilities.Contains(a.FacilityId.Value))
            .Select(a => a.Id).ToListAsync(cancellationToken);
        var allowedVisits = await _db.PatientVisits.AsNoTracking()
            .Where(v => visitIds.Contains(v.Id) && facilities.Contains(v.FacilityId))
            .Select(v => v.Id).ToListAsync(cancellationToken);
        var items = dashboard.Queue
            .Where(q => q.AppointmentId.HasValue ? allowedAppointments.Contains(q.AppointmentId.Value) : q.PatientVisitId.HasValue && allowedVisits.Contains(q.PatientVisitId.Value))
            .Select(q => new
            {
                id = q.PatientVisitId ?? q.AppointmentId, q.VisitCode, q.AppointmentCode, q.AppointmentId,
                q.QueueNumber, q.Status, q.PatientName, q.ChiefComplaint, q.AppointmentDate,
                startTime = q.StartTime.ToString("HH:mm"), endTime = q.EndTime.ToString("HH:mm"),
                intakeStatus = q.PatientVisitId.HasValue ? "Đã tiếp nhận" : "Chưa tiếp nhận"
            }).ToList();
        var received = items.Count(q => q.intakeStatus == "Đã tiếp nhận");
        return Completed(items, "doctor_queue", $"Bàn làm việc của bạn có {items.Count} lượt: {received} đã tiếp nhận, {items.Count - received} chưa tiếp nhận.");
    }

    private async Task<AiToolExecutionResult> GetPharmacyQueueAsync(AiToolExecutionContext context, CancellationToken cancellationToken)
    {
        var facilities = await ResolveFacilityScopeAsync(context, nameof(AiActorRole.Pharmacist), cancellationToken);
        if (facilities.Count == 0) return ScopeDenied();
        var prescriptions = await _pharmacy.GetPrescriptionsAsync(nameof(PrescriptionStatus.Issued), null, 1, 100);
        var items = new List<object>(prescriptions.Items.Count);
        foreach (var prescription in prescriptions.Items)
        {
            var payment = await _pharmacy.EvaluatePrescriptionPaymentAsync(prescription.Id, cancellationToken);
            items.Add(new
            {
                prescription.Id,
                status = prescription.Status,
                prescription.CreatedAt,
                prescription.PatientVisitId,
                paymentStatus = payment.PaymentStatus,
                paymentItems = payment.Items.Select(item => new
                {
                    medicine = item.MedicineName,
                    requiredQuantity = item.RequiredQuantity,
                    paidQuantity = item.PaidQuantity,
                    itemPaymentStatus = item.IsPaidInFull ? "paid_in_full" : item.PaidQuantity > 0 ? "partially_paid" : "unpaid"
                }).ToList()
            });
        }
        return Completed(items, "pharmacist_prescription_queue", $"Có {prescriptions.TotalItems} đơn thuốc chờ cấp." +
            (prescriptions.TotalItems > items.Count ? $" Hiển thị {items.Count} đơn mới nhất." : string.Empty));
    }

}
