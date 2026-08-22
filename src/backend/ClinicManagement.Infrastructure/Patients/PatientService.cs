using ClinicManagement.Application.Authentication.Interfaces;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Patients.DTOs;
using ClinicManagement.Application.Patients.Interfaces;
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
}
