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
                DoctorUserId = a.Doctor.UserId,
                a.SpecialtyId
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
            .Distinct()
            .ToListAsync(cancellationToken);

        if (userFacilityIds.Count == 0)
        {
            throw new ForbiddenException("ACCESS_DENIED_TO_FACILITY_RESOURCE", "Bạn không có quyền truy cập lịch hẹn thuộc cơ sở này.");
        }

        var doctorAssignments = await _dbContext.StaffFacilityAssignments
            .Where(a => a.UserId == appt.DoctorUserId && a.IsActive)
            .Include(a => a.Department)
            .ToListAsync(cancellationToken);

        if (doctorAssignments.Count == 0)
        {
            throw new ForbiddenException("ACCESS_DENIED_TO_FACILITY_RESOURCE", "Bác sĩ của lịch hẹn chưa được phân công cơ sở y tế nào.");
        }

        // Try to resolve facility based on appointment's specialty matching doctor's department assignment
        var specialtyMatchingFacilities = doctorAssignments
            .Where(a => a.Department != null && a.Department.SpecialtyId == appt.SpecialtyId)
            .Select(a => a.FacilityId)
            .Distinct()
            .ToList();

        if (specialtyMatchingFacilities.Count == 1)
        {
            var resolvedFacilityId = specialtyMatchingFacilities[0];
            if (!userFacilityIds.Contains(resolvedFacilityId))
            {
                throw new ForbiddenException("ACCESS_DENIED_TO_FACILITY_RESOURCE", "Bạn không có quyền truy cập lịch hẹn thuộc cơ sở này.");
            }
            return;
        }

        // If not uniquely resolved by specialty department, examine candidate doctor facilities
        var candidateFacilityIds = specialtyMatchingFacilities.Count > 1
            ? specialtyMatchingFacilities
            : doctorAssignments.Select(a => a.FacilityId).Distinct().ToList();

        if (candidateFacilityIds.Count == 1)
        {
            if (!userFacilityIds.Contains(candidateFacilityIds[0]))
            {
                throw new ForbiddenException("ACCESS_DENIED_TO_FACILITY_RESOURCE", "Bạn không có quyền truy cập lịch hẹn thuộc cơ sở này.");
            }
            return;
        }

        // Multi-facility doctor: if the appointment's facility cannot be uniquely determined,
        // do NOT guess facility or grant global access.
        // User must have access to all candidate facilities, otherwise access is indeterminate and denied.
        if (!candidateFacilityIds.All(cf => userFacilityIds.Contains(cf)))
        {
            throw new ForbiddenException("ACCESS_DENIED_TO_FACILITY_RESOURCE", "Không thể xác định chính xác cơ sở y tế của lịch hẹn giữa các cơ sở của bác sĩ.");
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
