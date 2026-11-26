using ClinicManagement.Application.Admin.DTOs;
using ClinicManagement.Application.Common.Constants;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Admin;

public static class AuditActorRoles
{
    private static readonly string[] Priority =
    {
        RoleNames.Admin, RoleNames.Doctor, RoleNames.LabSupervisor, RoleNames.DiagnosticTechnician,
        RoleNames.Pharmacist, RoleNames.Receptionist, RoleNames.Patient
    };

    public static async Task PopulateAsync(AppDbContext db, IReadOnlyList<SystemAuditLogDto> logs)
    {
        var userIds = logs.Where(l => l.UserId.HasValue).Select(l => l.UserId!.Value).Distinct().ToList();
        if (userIds.Count == 0) return;
        var rows = await (from ur in db.UserRoles
                          join r in db.Roles on ur.RoleId equals r.Id
                          where userIds.Contains(ur.UserId)
                          select new { ur.UserId, r.Name }).ToListAsync();
        var byUser = rows.GroupBy(r => r.UserId).ToDictionary(
            g => g.Key,
            g => g.Select(r => r.Name ?? string.Empty).OrderBy(Rank).First());
        foreach (var log in logs)
            log.UserRole = log.UserId.HasValue ? byUser.GetValueOrDefault(log.UserId.Value, string.Empty) : string.Empty;
    }

    private static int Rank(string role)
    {
        var index = Array.IndexOf(Priority, role);
        return index < 0 ? Priority.Length : index;
    }
}
