using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClinicManagement.Application.Authentication.Interfaces;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.HealthPackages.DTOs;
using ClinicManagement.Application.HealthPackages.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.HealthPackages;

public class HealthPackageRegistrationService : IHealthPackageRegistrationService
{
    private readonly AppDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;

    public HealthPackageRegistrationService(AppDbContext dbContext, ICurrentUserService currentUserService)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
    }

    public async Task<HealthPackageRegistrationDto> RegisterPackageAsync(CreatePackageRegistrationRequest request, CancellationToken cancellationToken = default)
    {
        var userId = _currentUserService.UserId ?? throw new UnauthorizedException("Chưa đăng nhập.");

        var patient = await _dbContext.Patients.FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        if (patient == null)
            throw new NotFoundException("Hồ sơ bệnh nhân không tồn tại.");

        var user = await _dbContext.Users.FindAsync(new object[] { userId }, cancellationToken);

        var package = await _dbContext.HealthPackages
            .FirstOrDefaultAsync(hp => hp.Id == request.HealthPackageId && hp.IsActive, cancellationToken);
        if (package == null)
            throw new BusinessException("PACKAGE_NOT_FOUND", "Gói khám không tồn tại hoặc đã ngừng hoạt động.");

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (request.PreferredDate < today)
            throw new BusinessException("INVALID_DATE", "Ngày mong muốn khám không được nằm trong quá khứ.");

        var phone = request.ContactPhone?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(phone))
            throw new BusinessException("INVALID_PHONE", "Số điện thoại liên hệ không được để trống.");

        var registrationCode = $"REG-{DateTime.UtcNow:yyMMdd}-{Guid.NewGuid():N}"[..18].ToUpper();

        var registration = new HealthPackageRegistration
        {
            RegistrationCode = registrationCode,
            HealthPackageId = package.Id,
            PatientId = patient.Id,
            PreferredDate = request.PreferredDate,
            ContactPhone = phone,
            Note = request.Note?.Trim(),
            Status = HealthPackageRegistrationStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.HealthPackageRegistrations.Add(registration);

        _dbContext.SystemAuditLogs.Add(new SystemAuditLog
        {
            UserId = userId,
            Action = "REGISTER_HEALTH_PACKAGE",
            EntityName = nameof(HealthPackageRegistration),
            EntityId = registrationCode,
            Description = $"Đăng ký gói khám {package.Name} (Mã: {registrationCode})",
            CreatedAt = DateTime.UtcNow
        });

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new HealthPackageRegistrationDto
        {
            Id = registration.Id,
            RegistrationCode = registration.RegistrationCode,
            HealthPackageId = package.Id,
            HealthPackageCode = package.Code,
            HealthPackageName = package.Name,
            HealthPackagePrice = package.Price,
            PatientId = patient.Id,
            PatientName = user?.FullName ?? "Bệnh nhân",
            PatientPhone = user?.PhoneNumber ?? "",
            PreferredDate = registration.PreferredDate,
            ContactPhone = registration.ContactPhone,
            Note = registration.Note,
            AdminNotes = registration.AdminNotes,
            CancellationReason = registration.CancellationReason,
            Status = registration.Status.ToString(),
            CreatedAt = registration.CreatedAt,
            UpdatedAt = registration.UpdatedAt
        };
    }

    public async Task<List<HealthPackageRegistrationDto>> GetMyRegistrationsAsync(CancellationToken cancellationToken = default)
    {
        var userId = _currentUserService.UserId ?? throw new UnauthorizedException("Chưa đăng nhập.");

        var patient = await _dbContext.Patients.FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        if (patient == null)
            throw new NotFoundException("Hồ sơ bệnh nhân không tồn tại.");

        var user = await _dbContext.Users.FindAsync(new object[] { userId }, cancellationToken);

        var query = from r in _dbContext.HealthPackageRegistrations.AsNoTracking()
                    join hp in _dbContext.HealthPackages.AsNoTracking() on r.HealthPackageId equals hp.Id
                    where r.PatientId == patient.Id
                    orderby r.CreatedAt descending
                    select new HealthPackageRegistrationDto
                    {
                        Id = r.Id,
                        RegistrationCode = r.RegistrationCode,
                        HealthPackageId = hp.Id,
                        HealthPackageCode = hp.Code,
                        HealthPackageName = hp.Name,
                        HealthPackagePrice = hp.Price,
                        PatientId = patient.Id,
                        PatientName = user != null ? user.FullName : "Bệnh nhân",
                        PatientPhone = user != null ? user.PhoneNumber ?? "" : "",
                        PreferredDate = r.PreferredDate,
                        ContactPhone = r.ContactPhone,
                        Note = r.Note,
                        AdminNotes = r.AdminNotes,
                        CancellationReason = r.CancellationReason,
                        Status = r.Status.ToString(),
                        CreatedAt = r.CreatedAt,
                        UpdatedAt = r.UpdatedAt
                    };

        return await query.ToListAsync(cancellationToken);
    }

    public async Task<HealthPackageRegistrationDto> GetRegistrationByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var userId = _currentUserService.UserId ?? throw new UnauthorizedException("Chưa đăng nhập.");

        var patient = await _dbContext.Patients.FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        if (patient == null)
            throw new NotFoundException("Hồ sơ bệnh nhân không tồn tại.");

        var user = await _dbContext.Users.FindAsync(new object[] { userId }, cancellationToken);

        var result = await (from r in _dbContext.HealthPackageRegistrations.AsNoTracking()
                            join hp in _dbContext.HealthPackages.AsNoTracking() on r.HealthPackageId equals hp.Id
                            where r.Id == id && r.PatientId == patient.Id
                            select new HealthPackageRegistrationDto
                            {
                                Id = r.Id,
                                RegistrationCode = r.RegistrationCode,
                                HealthPackageId = hp.Id,
                                HealthPackageCode = hp.Code,
                                HealthPackageName = hp.Name,
                                HealthPackagePrice = hp.Price,
                                PatientId = patient.Id,
                                PatientName = user != null ? user.FullName : "Bệnh nhân",
                                PatientPhone = user != null ? user.PhoneNumber ?? "" : "",
                                PreferredDate = r.PreferredDate,
                                ContactPhone = r.ContactPhone,
                                Note = r.Note,
                                AdminNotes = r.AdminNotes,
                                CancellationReason = r.CancellationReason,
                                Status = r.Status.ToString(),
                                CreatedAt = r.CreatedAt,
                                UpdatedAt = r.UpdatedAt
                            }).FirstOrDefaultAsync(cancellationToken);

        if (result == null)
            throw new NotFoundException("Không tìm thấy đăng ký gói khám.");

        return result;
    }

    public async Task<HealthPackageRegistrationDto> CancelMyRegistrationAsync(long id, CancelPackageRegistrationRequest? request = null, CancellationToken cancellationToken = default)
    {
        var userId = _currentUserService.UserId ?? throw new UnauthorizedException("Chưa đăng nhập.");

        var patient = await _dbContext.Patients.FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        if (patient == null)
            throw new NotFoundException("Hồ sơ bệnh nhân không tồn tại.");

        var reg = await _dbContext.HealthPackageRegistrations
            .Include(r => r.HealthPackage)
            .FirstOrDefaultAsync(r => r.Id == id && r.PatientId == patient.Id, cancellationToken);

        if (reg == null)
            throw new NotFoundException("Không tìm thấy đăng ký gói khám.");

        if (reg.Status != HealthPackageRegistrationStatus.Pending)
            throw new BusinessException("CANNOT_CANCEL", "Chỉ có thể hủy đăng ký gói khám khi đang ở trạng thái Chờ xác nhận (Pending).");

        reg.Status = HealthPackageRegistrationStatus.Cancelled;
        if (!string.IsNullOrWhiteSpace(request?.CancellationReason))
        {
            reg.CancellationReason = request.CancellationReason.Trim();
        }
        reg.UpdatedAt = DateTime.UtcNow;

        _dbContext.SystemAuditLogs.Add(new SystemAuditLog
        {
            UserId = userId,
            Action = "CANCEL_PACKAGE_REGISTRATION",
            EntityName = nameof(HealthPackageRegistration),
            EntityId = reg.RegistrationCode,
            Description = $"Bệnh nhân hủy đăng ký gói khám {reg.HealthPackage.Name} (Mã: {reg.RegistrationCode}){(string.IsNullOrWhiteSpace(reg.CancellationReason) ? "" : $". Lý do: {reg.CancellationReason}")}",
            CreatedAt = DateTime.UtcNow
        });

        await _dbContext.SaveChangesAsync(cancellationToken);

        var user = await _dbContext.Users.FindAsync(new object[] { userId }, cancellationToken);

        return new HealthPackageRegistrationDto
        {
            Id = reg.Id,
            RegistrationCode = reg.RegistrationCode,
            HealthPackageId = reg.HealthPackage.Id,
            HealthPackageCode = reg.HealthPackage.Code,
            HealthPackageName = reg.HealthPackage.Name,
            HealthPackagePrice = reg.HealthPackage.Price,
            PatientId = patient.Id,
            PatientName = user?.FullName ?? "Bệnh nhân",
            PatientPhone = user?.PhoneNumber ?? "",
            PreferredDate = reg.PreferredDate,
            ContactPhone = reg.ContactPhone,
            Note = reg.Note,
            AdminNotes = reg.AdminNotes,
            CancellationReason = reg.CancellationReason,
            Status = reg.Status.ToString(),
            CreatedAt = reg.CreatedAt,
            UpdatedAt = reg.UpdatedAt
        };
    }

    public async Task<PagedResult<HealthPackageRegistrationDto>> GetAllRegistrationsForReceptionAsync(string? status, string? search, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 10;
        if (pageSize > 100) pageSize = 100;

        var query = from r in _dbContext.HealthPackageRegistrations.AsNoTracking()
                    join hp in _dbContext.HealthPackages.AsNoTracking() on r.HealthPackageId equals hp.Id
                    join p in _dbContext.Patients.AsNoTracking() on r.PatientId equals p.Id
                    join u in _dbContext.Users.AsNoTracking() on p.UserId equals u.Id
                    select new
                    {
                        Registration = r,
                        Package = hp,
                        Patient = p,
                        User = u
                    };

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<HealthPackageRegistrationStatus>(status, true, out var parsedStatus))
        {
            query = query.Where(x => x.Registration.Status == parsedStatus);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(x => x.Registration.RegistrationCode.ToLower().Contains(s)
                                  || x.Package.Name.ToLower().Contains(s)
                                  || x.Package.Code.ToLower().Contains(s)
                                  || x.User.FullName.ToLower().Contains(s)
                                  || x.Registration.ContactPhone.Contains(s));
        }

        var totalItems = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(x => x.Registration.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new HealthPackageRegistrationDto
            {
                Id = x.Registration.Id,
                RegistrationCode = x.Registration.RegistrationCode,
                HealthPackageId = x.Package.Id,
                HealthPackageCode = x.Package.Code,
                HealthPackageName = x.Package.Name,
                HealthPackagePrice = x.Package.Price,
                PatientId = x.Patient.Id,
                PatientName = x.User.FullName,
                PatientPhone = x.User.PhoneNumber ?? "",
                PreferredDate = x.Registration.PreferredDate,
                ContactPhone = x.Registration.ContactPhone,
                Note = x.Registration.Note,
                AdminNotes = x.Registration.AdminNotes,
                CancellationReason = x.Registration.CancellationReason,
                Status = x.Registration.Status.ToString(),
                CreatedAt = x.Registration.CreatedAt,
                UpdatedAt = x.Registration.UpdatedAt
            }).ToListAsync(cancellationToken);

        return new PagedResult<HealthPackageRegistrationDto>(items, totalItems, page, pageSize);
    }

    public async Task<HealthPackageRegistrationDto> ConfirmRegistrationAsync(long id, ConfirmPackageRegistrationRequest? request, CancellationToken cancellationToken = default)
    {
        var userId = _currentUserService.UserId ?? throw new UnauthorizedException("Chưa đăng nhập.");

        var reg = await _dbContext.HealthPackageRegistrations
            .Include(r => r.HealthPackage)
            .Include(r => r.Patient)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (reg == null)
            throw new NotFoundException("Không tìm thấy đăng ký gói khám.");

        if (reg.Status != HealthPackageRegistrationStatus.Pending)
            throw new BusinessException("INVALID_STATE", "Chỉ có thể xác nhận đăng ký đang ở trạng thái Chờ xác nhận (Pending).");

        reg.Status = HealthPackageRegistrationStatus.Confirmed;
        if (!string.IsNullOrWhiteSpace(request?.Notes))
        {
            reg.AdminNotes = request.Notes.Trim();
        }
        reg.UpdatedAt = DateTime.UtcNow;

        _dbContext.SystemAuditLogs.Add(new SystemAuditLog
        {
            UserId = userId,
            Action = "CONFIRM_PACKAGE_REGISTRATION",
            EntityName = nameof(HealthPackageRegistration),
            EntityId = reg.RegistrationCode,
            Description = $"Lễ tân xác nhận đăng ký gói khám {reg.HealthPackage.Name} (Mã: {reg.RegistrationCode}){(string.IsNullOrWhiteSpace(reg.AdminNotes) ? "" : $". Ghi chú: {reg.AdminNotes}")}",
            CreatedAt = DateTime.UtcNow
        });

        await _dbContext.SaveChangesAsync(cancellationToken);

        var patientUser = await _dbContext.Users.FindAsync(new object[] { reg.Patient.UserId }, cancellationToken);

        return new HealthPackageRegistrationDto
        {
            Id = reg.Id,
            RegistrationCode = reg.RegistrationCode,
            HealthPackageId = reg.HealthPackage.Id,
            HealthPackageCode = reg.HealthPackage.Code,
            HealthPackageName = reg.HealthPackage.Name,
            HealthPackagePrice = reg.HealthPackage.Price,
            PatientId = reg.Patient.Id,
            PatientName = patientUser?.FullName ?? "Bệnh nhân",
            PatientPhone = patientUser?.PhoneNumber ?? "",
            PreferredDate = reg.PreferredDate,
            ContactPhone = reg.ContactPhone,
            Note = reg.Note,
            AdminNotes = reg.AdminNotes,
            CancellationReason = reg.CancellationReason,
            Status = reg.Status.ToString(),
            CreatedAt = reg.CreatedAt,
            UpdatedAt = reg.UpdatedAt
        };
    }

    public async Task<HealthPackageRegistrationDto> CancelRegistrationByReceptionAsync(long id, CancelPackageRegistrationRequest? request, CancellationToken cancellationToken = default)
    {
        var userId = _currentUserService.UserId ?? throw new UnauthorizedException("Chưa đăng nhập.");

        var reg = await _dbContext.HealthPackageRegistrations
            .Include(r => r.HealthPackage)
            .Include(r => r.Patient)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (reg == null)
            throw new NotFoundException("Không tìm thấy đăng ký gói khám.");

        if (reg.Status == HealthPackageRegistrationStatus.Cancelled)
            throw new BusinessException("ALREADY_CANCELLED", "Đăng ký gói khám này đã được hủy trước đó.");

        reg.Status = HealthPackageRegistrationStatus.Cancelled;
        if (!string.IsNullOrWhiteSpace(request?.CancellationReason))
        {
            reg.CancellationReason = request.CancellationReason.Trim();
        }
        reg.UpdatedAt = DateTime.UtcNow;

        _dbContext.SystemAuditLogs.Add(new SystemAuditLog
        {
            UserId = userId,
            Action = "RECEPTION_CANCEL_PACKAGE_REGISTRATION",
            EntityName = nameof(HealthPackageRegistration),
            EntityId = reg.RegistrationCode,
            Description = $"Lễ tân hủy đăng ký gói khám {reg.HealthPackage.Name} (Mã: {reg.RegistrationCode}){(string.IsNullOrWhiteSpace(reg.CancellationReason) ? "" : $". Lý do: {reg.CancellationReason}")}",
            CreatedAt = DateTime.UtcNow
        });

        await _dbContext.SaveChangesAsync(cancellationToken);

        var patientUser = await _dbContext.Users.FindAsync(new object[] { reg.Patient.UserId }, cancellationToken);

        return new HealthPackageRegistrationDto
        {
            Id = reg.Id,
            RegistrationCode = reg.RegistrationCode,
            HealthPackageId = reg.HealthPackage.Id,
            HealthPackageCode = reg.HealthPackage.Code,
            HealthPackageName = reg.HealthPackage.Name,
            HealthPackagePrice = reg.HealthPackage.Price,
            PatientId = reg.Patient.Id,
            PatientName = patientUser?.FullName ?? "Bệnh nhân",
            PatientPhone = patientUser?.PhoneNumber ?? "",
            PreferredDate = reg.PreferredDate,
            ContactPhone = reg.ContactPhone,
            Note = reg.Note,
            AdminNotes = reg.AdminNotes,
            CancellationReason = reg.CancellationReason,
            Status = reg.Status.ToString(),
            CreatedAt = reg.CreatedAt,
            UpdatedAt = reg.UpdatedAt
        };
    }
}
