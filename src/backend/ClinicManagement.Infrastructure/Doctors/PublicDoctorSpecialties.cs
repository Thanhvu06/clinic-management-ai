using ClinicManagement.Application.Specialties.DTOs;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Doctors;

internal static class PublicDoctorSpecialties
{
    internal static async Task<Dictionary<long, List<SpecialtyDto>>> LoadAsync(AppDbContext db, IEnumerable<long> doctorIds)
    {
        var ids = doctorIds.ToArray();
        var rows = await db.DoctorSpecialties.AsNoTracking()
            .Where(link => ids.Contains(link.DoctorId) && link.Specialty.IsActive)
            .OrderByDescending(link => link.IsPrimary)
            .ThenBy(link => link.SpecialtyId)
            .Select(link => new
            {
                link.DoctorId,
                Specialty = new SpecialtyDto
                {
                    Id = link.SpecialtyId,
                    SpecialtyCode = link.Specialty.SpecialtyCode,
                    SpecialtyName = link.Specialty.Name,
                    Description = link.Specialty.Description ?? string.Empty,
                    AiEnabled = link.Specialty.AiEnabled,
                    ConsultationFee = link.Specialty.ConsultationFee
                }
            }).ToListAsync();
        return rows.GroupBy(row => row.DoctorId)
            .ToDictionary(group => group.Key, group => group.Select(row => row.Specialty).ToList());
    }
}
