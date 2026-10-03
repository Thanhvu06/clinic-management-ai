using ClinicManagement.Application.Common.Constants;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Common;

internal static class VisitDoctorAuthorizationGuard
{
    public static PatientVisit ValidateAssignedDoctor(PatientVisit? visit, Doctor doctor, bool allowUnassigned = false)
    {
        if (visit == null || (visit.AssignedDoctorId != doctor.Id &&
            !(allowUnassigned && !visit.AssignedDoctorId.HasValue)))
            throw new NotFoundException("Lượt khám không tồn tại hoặc không thuộc quyền quản lý.");
        return visit;
    }

    public static async Task<long> ResolveFacilityAsync(
        AppDbContext dbContext, Doctor doctor, PatientVisit visit, long? explicitFacilityId = null)
    {
        if (visit.AppointmentId.HasValue && visit.Appointment == null)
            throw new BusinessException("FACILITY_SCOPE_DENIED", "Không thể xác minh cơ sở của lịch hẹn liên kết.");

        if (visit.Appointment?.FacilityId is long appointmentFacilityId && appointmentFacilityId != visit.FacilityId)
            throw new BusinessException("FACILITY_SCOPE_DENIED", "Các nguồn cơ sở của lượt khám không nhất quán.");

        if (explicitFacilityId.HasValue && explicitFacilityId.Value != visit.FacilityId)
            throw new BusinessException("FACILITY_SCOPE_DENIED", "Lượt khám không thuộc cơ sở chỉ định.");

        var hasExactDoctorAssignment = await dbContext.StaffFacilityAssignments
            .AsNoTracking()
            .AnyAsync(x => x.UserId == doctor.UserId && x.IsActive && x.Role == RoleNames.Doctor && x.FacilityId == visit.FacilityId);

        if (!hasExactDoctorAssignment)
            throw new BusinessException("FACILITY_SCOPE_DENIED", "Bác sĩ không được phân quyền tại cơ sở của lượt khám.");

        return visit.FacilityId;
    }
}
