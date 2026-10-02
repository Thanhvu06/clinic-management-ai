using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ClinicManagement.Application.AI.Interfaces;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.AI;

public class ClinicAiContextService : IClinicAiContextService
{
    private readonly AppDbContext _dbContext;
    private readonly IDateTimeProvider _dateTimeProvider;

    public ClinicAiContextService(AppDbContext dbContext, IDateTimeProvider dateTimeProvider)
    {
        _dbContext = dbContext;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<string> GetClinicContextJsonAsync(CancellationToken cancellationToken = default)
    {
        var activeSpecialties = await _dbContext.Specialties
            .AsNoTracking()
            .Where(s => s.IsActive && s.AiEnabled)
            .Select(s => new
            {
                s.Id,
                s.SpecialtyCode,
                s.Name,
                s.Description
            })
            .ToListAsync(cancellationToken);

        var specialtyIds = activeSpecialties.Select(s => s.Id).ToList();

        var dateToday = _dateTimeProvider.VietnamToday;
        var timeNow = _dateTimeProvider.VietnamTime;
        var toDate = dateToday.AddDays(7); // Next 7 days

        var availableSlots = await _dbContext.AppointmentSlots
            .AsNoTracking()
            .Where(s => !s.IsBooked
                        && s.SlotDate >= dateToday
                        && s.SlotDate <= toDate
                        && (s.SlotDate > dateToday || (s.SlotDate == dateToday && s.StartTime > timeNow)))
            .Select(s => new { s.DoctorId, s.SlotDate })
            .Distinct()
            .ToListAsync(cancellationToken);

        var activeDoctorsWithId = await (from ds in _dbContext.DoctorSpecialties
                                         join d in _dbContext.Doctors on ds.DoctorId equals d.Id
                                         join u in _dbContext.Users on d.UserId equals u.Id
                                         where d.IsActive && u.IsActive && specialtyIds.Contains(ds.SpecialtyId)
                                         select new
                                         {
                                             d.Id,
                                             ds.SpecialtyId,
                                             DoctorName = u.FullName,
                                             d.AcademicTitle,
                                             d.ExperienceYears,
                                             d.Description
                                         })
                                         .AsNoTracking()
                                         .ToListAsync(cancellationToken);

        // Group by specialty
        var clinicData = activeSpecialties.Select(s => new
        {
            SpecialtyName = s.Name,
            SpecialtyCode = s.SpecialtyCode,
            Description = s.Description,
            Doctors = activeDoctorsWithId
                .Where(d => d.SpecialtyId == s.Id)
                .Select(d => new
                {
                    FullName = string.IsNullOrWhiteSpace(d.AcademicTitle) ? d.DoctorName : $"{d.AcademicTitle} {d.DoctorName}",
                    d.ExperienceYears,
                    Description = d.Description,
                    HasAvailableSlotsIn7Days = availableSlots.Any(slot => slot.DoctorId == d.Id)
                })
                .ToList()
        }).ToList();

        var primaryFacility = await _dbContext.Facilities
            .AsNoTracking()
            .Where(f => f.IsActive)
            .OrderBy(f => f.Id)
            .Select(f => new
            {
                f.Name,
                f.Address,
                f.City,
                f.Phone,
                f.Email,
                f.Description
            })
            .FirstOrDefaultAsync(cancellationToken);

        var facilityInfo = primaryFacility != null
            ? new
            {
                TenPhongKham = primaryFacility.Name,
                DiaChi = string.IsNullOrWhiteSpace(primaryFacility.City) ? primaryFacility.Address : $"{primaryFacility.Address}, {primaryFacility.City}",
                HotlineTiepDon = string.IsNullOrWhiteSpace(primaryFacility.Phone) ? "Chưa cấu hình" : primaryFacility.Phone,
                Email = primaryFacility.Email ?? "Chưa cấu hình",
                GioLamViec = "Thứ Hai đến Thứ Bảy: 07:30 - 17:00 (Nghỉ Chủ Nhật)."
            }
            : null;

        var payload = new
        {
            ThongTinCoSo = facilityInfo,
            HuongDanCoSo = facilityInfo != null
                ? $"Phòng khám: {facilityInfo.TenPhongKham}. Địa chỉ: {facilityInfo.DiaChi}. Hotline tiếp đón: {facilityInfo.HotlineTiepDon}. Giờ làm việc: {facilityInfo.GioLamViec}."
                : "Thông tin liên hệ cơ sở và hotline lễ tân hiện chưa được cấu hình trong hệ thống. Nếu người dùng hỏi, trả lời trung thực là hệ thống chưa có cấu hình liên hệ.",
            Specialties = clinicData
        };

        var options = new JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        return JsonSerializer.Serialize(payload, options);
    }
}
