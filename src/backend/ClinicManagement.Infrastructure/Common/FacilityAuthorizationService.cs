using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Infrastructure.Identity;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Common;

public class FacilityAuthorizationService : IFacilityAuthorizationService
{
    private readonly AppDbContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;

    public FacilityAuthorizationService(AppDbContext dbContext, UserManager<ApplicationUser> userManager)
    {
        _dbContext = dbContext;
        _userManager = userManager;
    }

    public async Task ValidateUserFacilityAccessAsync(Guid userId, long facilityId, CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
            throw new UnauthorizedException("Người dùng chưa đăng nhập.");

        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user == null)
            throw new UnauthorizedException("Tài khoản người dùng không tồn tại.");

        // Admin or SuperAdmin have cross-facility access
        if (await HasFullFacilityAccessAsync(userId, cancellationToken))
            return;

        // Verify that the user has an active StaffFacilityAssignment for this facility
        var hasAccess = await _dbContext.StaffFacilityAssignments
            .AnyAsync(a => a.UserId == userId && a.FacilityId == facilityId && a.IsActive, cancellationToken);

        if (!hasAccess)
        {
            throw new ForbiddenException("ACCESS_DENIED_TO_FACILITY_RESOURCE", "Bạn không có quyền thao tác trên hồ sơ thuộc cơ sở y tế này.");
        }
    }

    public async Task ValidateVisitAccessAsync(Guid userId, long visitId, CancellationToken cancellationToken = default)
    {
        var visit = await _dbContext.PatientVisits
            .AsNoTracking()
            .Select(v => new { v.Id, v.FacilityId })
            .FirstOrDefaultAsync(v => v.Id == visitId, cancellationToken);

        if (visit == null)
            throw new NotFoundException("Lượt khám không tồn tại.");

        await ValidateUserFacilityAccessAsync(userId, visit.FacilityId, cancellationToken);
    }

    public async Task ValidateAppointmentAccessAsync(Guid userId, long appointmentId, CancellationToken cancellationToken = default)
    {
        var appt = await _dbContext.Appointments
            .AsNoTracking()
            .Where(a => a.Id == appointmentId)
            .Select(a => new { 
                a.Id, 
                VisitFacilityId = (long?)(a.PatientVisit != null ? a.PatientVisit.FacilityId : null),
                DoctorUserId = a.Doctor.UserId
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (appt == null)
            throw new NotFoundException("Lịch hẹn không tồn tại.");

        if (await HasFullFacilityAccessAsync(userId, cancellationToken))
            return;

        if (appt.VisitFacilityId.HasValue && appt.VisitFacilityId.Value > 0)
        {
            await ValidateUserFacilityAccessAsync(userId, appt.VisitFacilityId.Value, cancellationToken);
            return;
        }

        var userFacilityIds = await _dbContext.StaffFacilityAssignments
            .Where(a => a.UserId == userId && a.IsActive)
            .Select(a => a.FacilityId)
            .ToListAsync(cancellationToken);

        if (userFacilityIds.Count == 0)
        {
            throw new ForbiddenException("ACCESS_DENIED_TO_FACILITY_RESOURCE", "Bạn không có quyền truy cập lịch hẹn thuộc cơ sở này.");
        }

        var doctorFacilityIds = await _dbContext.StaffFacilityAssignments
            .Where(a => a.UserId == appt.DoctorUserId && a.IsActive)
            .Select(a => a.FacilityId)
            .ToListAsync(cancellationToken);

        if (doctorFacilityIds.Count > 0 && !userFacilityIds.Intersect(doctorFacilityIds).Any())
        {
            throw new ForbiddenException("ACCESS_DENIED_TO_FACILITY_RESOURCE", "Bạn không có quyền truy cập lịch hẹn thuộc cơ sở này.");
        }
    }

    public async Task ValidateInvoiceAccessAsync(Guid userId, long invoiceId, CancellationToken cancellationToken = default)
    {
        var invoice = await _dbContext.Invoices
            .AsNoTracking()
            .Where(i => i.Id == invoiceId)
            .Select(i => new { 
                i.Id, 
                VisitFacilityId = (long?)(i.PatientVisit != null ? i.PatientVisit.FacilityId : null)
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (invoice == null)
            throw new NotFoundException("Hóa đơn không tồn tại.");

        if (invoice.VisitFacilityId.HasValue && invoice.VisitFacilityId.Value > 0)
        {
            await ValidateUserFacilityAccessAsync(userId, invoice.VisitFacilityId.Value, cancellationToken);
        }
    }

    public async Task<bool> HasFullFacilityAccessAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty) return false;
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user == null) return false;
        var roles = await _userManager.GetRolesAsync(user);
        return roles.Contains("Admin") || roles.Contains("SuperAdmin");
    }

    public async Task<List<long>> GetUserAccessibleFacilityIdsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty) return new List<long>();
        if (await HasFullFacilityAccessAsync(userId, cancellationToken))
        {
            return await _dbContext.Facilities.Where(f => f.IsActive).Select(f => f.Id).ToListAsync(cancellationToken);
        }
        return await _dbContext.StaffFacilityAssignments
            .Where(a => a.UserId == userId && a.IsActive)
            .Select(a => a.FacilityId)
            .Distinct()
            .ToListAsync(cancellationToken);
    }
}
