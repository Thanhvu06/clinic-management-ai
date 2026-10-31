using ClinicManagement.Application.Notifications.DTOs;
using ClinicManagement.Application.Notifications.Interfaces;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Notifications;

internal static class FacilityWorkflowNotifications
{
    internal static async Task SendAsync(AppDbContext db, INotificationService notifications, long facilityId,
        string role, NotificationType type, string message, string route, string entity, long entityId,
        string eventKey, CancellationToken cancellationToken = default)
    {
        var recipients = await (from assignment in db.StaffFacilityAssignments
            join user in db.Users on assignment.UserId equals user.Id
            where assignment.FacilityId == facilityId && assignment.IsActive && assignment.Role == role && user.IsActive
            select user.Id).Distinct().ToListAsync(cancellationToken);
        foreach (var recipient in recipients)
            await notifications.CreateNotificationAsync(new CreateNotificationRequest
            {
                UserId = recipient, Type = type, Title = message, Message = message, Route = route,
                RelatedEntityType = entity, RelatedEntityId = entityId.ToString(),
                DedupeKey = $"{eventKey}_{entityId}_{recipient}"
            }, cancellationToken);
    }
}
