using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Appointments;

public sealed record EligibleAppointmentFacility(long Id, string Name, bool IsPrimary);

public static class AppointmentFacilityResolver
{
    public static async Task<List<EligibleAppointmentFacility>> GetEligibleAsync(AppDbContext db, long doctorId, long specialtyId, CancellationToken ct = default)
    {
        var candidates = await (from doctor in db.Doctors.AsNoTracking()
                                join assignment in db.StaffFacilityAssignments.AsNoTracking() on doctor.UserId equals assignment.UserId
                                join facility in db.Facilities.AsNoTracking() on assignment.FacilityId equals facility.Id
                                join department in db.Departments.AsNoTracking() on assignment.FacilityId equals department.FacilityId
                                where doctor.Id == doctorId && assignment.IsActive && assignment.Role == "Doctor" &&
                                      facility.IsActive && department.IsActive && department.SpecialtyId == specialtyId &&
                                      (!assignment.DepartmentId.HasValue || assignment.DepartmentId == department.Id)
                                select new { facility.Id, facility.Name, assignment.IsPrimary }).ToListAsync(ct);
        return candidates.GroupBy(x => x.Id)
            .Select(group => new EligibleAppointmentFacility(group.Key, group.First().Name, group.Any(x => x.IsPrimary)))
            .OrderBy(x => x.Id).ToList();
    }
}
