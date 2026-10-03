using System;
using System.Threading.Tasks;
using ClinicManagement.Application.Authentication.Interfaces;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Doctors.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Infrastructure.Identity;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Doctors;

public class DoctorContextService : IDoctorContextService
{
    private readonly AppDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;
    private readonly UserManager<ApplicationUser> _userManager;

    public DoctorContextService(
        AppDbContext dbContext,
        ICurrentUserService currentUserService,
        UserManager<ApplicationUser> userManager)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
        _userManager = userManager;
    }

    public async Task<Doctor> GetCurrentActiveDoctorAsync()
    {
        var currentUserId = _currentUserService.UserId;
        if (currentUserId == null || currentUserId == Guid.Empty)
            throw new UnauthorizedException("Chưa đăng nhập.");

        var user = await _userManager.FindByIdAsync(currentUserId.Value.ToString());
        if (user == null || !user.IsActive)
            throw new ForbiddenException("DOCTOR_ACCOUNT_INACTIVE", "Tài khoản người dùng đã bị vô hiệu hóa.");

        var isDoctor = await _userManager.IsInRoleAsync(user, "Doctor");
        if (!isDoctor)
            throw new ForbiddenException("FORBIDDEN", "Người dùng không có quyền Bác sĩ.");

        var doctor = await _dbContext.Doctors
            .Include(d => d.DoctorSpecialties)
                .ThenInclude(ds => ds.Specialty)
            .FirstOrDefaultAsync(d => d.UserId == currentUserId.Value);

        if (doctor == null)
            throw new NotFoundException("Hồ sơ bác sĩ không tồn tại.");

        if (!doctor.IsActive)
            throw new ForbiddenException("DOCTOR_ACCOUNT_INACTIVE", "Hồ sơ bác sĩ đã bị vô hiệu hóa.");

        return doctor;
    }
}
