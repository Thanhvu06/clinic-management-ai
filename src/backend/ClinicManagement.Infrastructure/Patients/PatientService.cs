using ClinicManagement.Application.Appointments.DTOs.Doctor;
using ClinicManagement.Application.Authentication.Interfaces;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Patients.DTOs;
using ClinicManagement.Application.Patients.Interfaces;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Patients;

public class PatientService : IPatientService
{
    private readonly AppDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;

    public PatientService(AppDbContext dbContext, ICurrentUserService currentUserService)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
    }

    public async Task<PatientProfileDto> GetMyProfileAsync()
    {
        var userId = _currentUserService.UserId ?? throw new UnauthorizedException();

        var profile = await (from p in _dbContext.Patients
                             join u in _dbContext.Users on p.UserId equals u.Id
                             where p.UserId == userId
                             select new PatientProfileDto
                             {
                                 Id = p.Id,
                                 UserId = p.UserId,
                                 FullName = u.FullName,
                                 Email = u.Email ?? string.Empty,
                                 PhoneNumber = u.PhoneNumber ?? string.Empty,
                                 DateOfBirth = p.DateOfBirth,
                                 Gender = p.Gender,
                                 Address = p.Address
                             }).AsNoTracking().FirstOrDefaultAsync();

        if (profile == null)
            throw new NotFoundException("Không tìm thấy thông tin bệnh nhân tương ứng với tài khoản.");

        return profile;
    }

    public async Task UpdateMyProfileAsync(UpdatePatientProfileRequest request)
    {
        var userId = _currentUserService.UserId ?? throw new UnauthorizedException();

        var patient = await _dbContext.Patients.FirstOrDefaultAsync(p => p.UserId == userId);
        if (patient == null)
            throw new NotFoundException("Không tìm thấy thông tin bệnh nhân.");

        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null)
            throw new NotFoundException("Không tìm thấy tài khoản.");

        if (request.DateOfBirth.HasValue && request.DateOfBirth.Value > DateOnly.FromDateTime(DateTime.UtcNow))
            throw new ValidationException("DateOfBirth", "Ngày sinh không được lớn hơn ngày hiện tại.");

        user.FullName = request.FullName;
        user.PhoneNumber = request.PhoneNumber;
        user.UpdatedAt = DateTime.UtcNow;

        patient.DateOfBirth = request.DateOfBirth;
        patient.Gender = request.Gender;
        patient.Address = request.Address;

        await _dbContext.SaveChangesAsync();
    }

    public async Task<List<PatientPrescriptionDto>> GetMyPrescriptionsAsync()
    {
        var userId = _currentUserService.UserId ?? throw new UnauthorizedException();

        var patient = await _dbContext.Patients.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId);
        if (patient == null)
            throw new NotFoundException("Không tìm thấy hồ sơ bệnh nhân.");

        var prescriptions = await _dbContext.Prescriptions
            .AsNoTracking()
            .Where(p => p.PatientId == patient.Id)
            .Include(p => p.Appointment)
                .ThenInclude(a => a!.Specialty)
            .Include(p => p.Appointment)
                .ThenInclude(a => a!.VisitSummary)
            .Include(p => p.Doctor)
            .Include(p => p.Items)
                .ThenInclude(i => i.Medicine)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();

        var doctorUserIds = prescriptions
            .Where(p => p.Doctor != null)
            .Select(p => p.Doctor!.UserId)
            .Distinct()
            .ToList();

        var doctorUsers = await _dbContext.Users
            .AsNoTracking()
            .Where(u => doctorUserIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName);

        return prescriptions.Select(p =>
        {
            var doctorFullName = (p.Doctor != null && doctorUsers.TryGetValue(p.Doctor.UserId, out var name)) ? name : "Bác sĩ";
            var doctorTitle = string.IsNullOrWhiteSpace(p.Doctor?.AcademicTitle) ? "" : p.Doctor.AcademicTitle + ". ";

            return new PatientPrescriptionDto
            {
                Id = p.Id,
                Code = $"RX-{p.CreatedAt:yyyyMMdd}-{p.Id:D4}",
                AppointmentId = p.AppointmentId,
                AppointmentCode = p.Appointment?.AppointmentCode ?? string.Empty,
                AppointmentDate = p.Appointment?.AppointmentDate ?? DateOnly.FromDateTime(p.CreatedAt),
                DoctorName = doctorTitle + doctorFullName,
                SpecialtyName = p.Appointment?.Specialty?.Name ?? "Đa khoa",
                Diagnosis = p.Appointment?.VisitSummary?.Summary ?? "Khám chuyên khoa",
                Status = p.Status.ToString(),
                Notes = p.Notes,
                CreatedAt = p.CreatedAt,
                DispensedAt = p.DispensedAt,
                Items = p.Items.Select(i => new PatientPrescriptionItemDto
                {
                    MedicineId = i.MedicineId,
                    Name = i.Medicine?.Name ?? "Thuốc",
                    Unit = i.Medicine?.Unit ?? "Đơn vị",
                    Quantity = i.Quantity,
                    Dosage = i.Dosage,
                    Frequency = i.Frequency,
                    DurationDays = i.DurationDays,
                    Instructions = i.Instructions
                }).ToList()
            };
        }).ToList();
    }

    public async Task<List<PatientVitalHistoryItemDto>> GetMyVitalsAsync(int limit)
    {
        var userId = _currentUserService.UserId ?? throw new UnauthorizedException();

        var patient = await _dbContext.Patients.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId);
        if (patient == null)
            throw new NotFoundException("Không tìm thấy hồ sơ bệnh nhân.");

        var safeLimit = Math.Clamp(limit, 1, 100);

        var vitalsAppointments = await _dbContext.Appointments
            .AsNoTracking()
            .Include(a => a.VitalSigns)
            .Where(a => a.PatientId == patient.Id &&
                        a.Status != AppointmentStatus.Cancelled &&
                        a.Status != AppointmentStatus.NoShow &&
                        a.VitalSigns != null)
            .OrderByDescending(a => a.AppointmentDate)
            .ThenByDescending(a => a.StartTime)
            .Take(safeLimit)
            .ToListAsync();

        var recorderUserIds = vitalsAppointments
            .Select(a => a.VitalSigns!.RecordedByUserId)
            .Distinct()
            .ToList();

        var recorderUsers = await _dbContext.Users
            .Where(u => recorderUserIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName);

        return vitalsAppointments.Select(a =>
        {
            var vs = a.VitalSigns!;
            recorderUsers.TryGetValue(vs.RecordedByUserId, out var recName);
            return new PatientVitalHistoryItemDto
            {
                AppointmentId = a.Id,
                AppointmentCode = a.AppointmentCode,
                AppointmentDate = a.AppointmentDate,
                RecordedAtUtc = vs.RecordedAtUtc,
                Height = vs.Height,
                Weight = vs.Weight,
                Bmi = vs.Bmi,
                Temperature = vs.Temperature,
                BloodPressureSystolic = vs.BloodPressureSystolic,
                BloodPressureDiastolic = vs.BloodPressureDiastolic,
                HeartRate = vs.HeartRate,
                RespiratoryRate = vs.RespiratoryRate,
                SpO2 = vs.SpO2,
                RecordedByUserName = recName ?? "Nhân viên y tế"
            };
        }).ToList();
    }
}
