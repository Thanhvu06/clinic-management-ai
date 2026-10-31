using ClinicManagement.Application.Notifications.Interfaces;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Notifications;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Visits;

public static partial class VisitCompletionCoordinator
{
    public static async Task RecordClinicalCompletionAsync(long visitId, AppDbContext db,
        INotificationService notifications, CancellationToken cancellationToken = default)
    {
        var visit = await db.PatientVisits.SingleAsync(v => v.Id == visitId, cancellationToken);
        await FacilityWorkflowNotifications.SendAsync(db, notifications, visit.FacilityId, "Receptionist",
            NotificationType.Appointment, $"Lượt khám {visit.VisitCode} đã hoàn tất, chờ lập hóa đơn",
            "/reception/billing", "PatientVisit", visit.Id, "clinical_completed", cancellationToken);
    }
}
