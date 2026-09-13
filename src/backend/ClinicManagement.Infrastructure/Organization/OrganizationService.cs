using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Organization.DTOs;
using ClinicManagement.Application.Organization.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Organization;

public class OrganizationService : IOrganizationService
{
    private readonly AppDbContext _dbContext;

    public OrganizationService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<FacilityDto>> GetFacilitiesAsync(bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Facilities.AsNoTracking();
        if (!includeInactive)
        {
            query = query.Where(f => f.IsActive);
        }

        return await query
            .OrderBy(f => f.Code)
            .Select(f => new FacilityDto
            {
                Id = f.Id,
                Code = f.Code,
                Name = f.Name,
                Address = f.Address,
                City = f.City,
                Phone = f.Phone,
                Email = f.Email,
                TaxCode = f.TaxCode,
                HospitalLevel = f.HospitalLevel,
                Description = f.Description,
                IsActive = f.IsActive,
                BuildingCount = f.Buildings.Count,
                DepartmentCount = f.Departments.Count
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<FacilityDto> GetFacilityByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var facility = await _dbContext.Facilities
            .AsNoTracking()
            .Where(f => f.Id == id)
            .Select(f => new FacilityDto
            {
                Id = f.Id,
                Code = f.Code,
                Name = f.Name,
                Address = f.Address,
                City = f.City,
                Phone = f.Phone,
                Email = f.Email,
                TaxCode = f.TaxCode,
                HospitalLevel = f.HospitalLevel,
                Description = f.Description,
                IsActive = f.IsActive,
                BuildingCount = f.Buildings.Count,
                DepartmentCount = f.Departments.Count
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (facility == null)
            throw new NotFoundException($"Không tìm thấy cơ sở y tế với mã ID: {id}");

        return facility;
    }

    public async Task<FacilityDto> CreateFacilityAsync(CreateFacilityRequest request, CancellationToken cancellationToken = default)
    {
        var codeExists = await _dbContext.Facilities.AnyAsync(f => f.Code == request.Code, cancellationToken);
        if (codeExists)
            throw new ConflictException($"Mã cơ sở '{request.Code}' đã tồn tại trong hệ thống.");

        var facility = new Facility
        {
            Code = request.Code.Trim().ToUpperInvariant(),
            Name = request.Name.Trim(),
            Address = request.Address.Trim(),
            City = request.City.Trim(),
            Phone = request.Phone.Trim(),
            Email = request.Email?.Trim(),
            TaxCode = request.TaxCode?.Trim(),
            HospitalLevel = request.HospitalLevel?.Trim(),
            Description = request.Description?.Trim(),
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };

        _dbContext.Facilities.Add(facility);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return await GetFacilityByIdAsync(facility.Id, cancellationToken);
    }

    public async Task<FacilityDto> UpdateFacilityAsync(long id, UpdateFacilityRequest request, CancellationToken cancellationToken = default)
    {
        var facility = await _dbContext.Facilities.FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
        if (facility == null)
            throw new NotFoundException($"Không tìm thấy cơ sở y tế với mã ID: {id}");

        facility.Name = request.Name.Trim();
        facility.Address = request.Address.Trim();
        facility.City = request.City.Trim();
        facility.Phone = request.Phone.Trim();
        facility.Email = request.Email?.Trim();
        facility.TaxCode = request.TaxCode?.Trim();
        facility.HospitalLevel = request.HospitalLevel?.Trim();
        facility.Description = request.Description?.Trim();
        facility.IsActive = request.IsActive;
        facility.UpdatedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return await GetFacilityByIdAsync(facility.Id, cancellationToken);
    }

    public async Task<FacilityDto> ToggleFacilityStatusAsync(long id, CancellationToken cancellationToken = default)
    {
        var facility = await _dbContext.Facilities.FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
        if (facility == null)
            throw new NotFoundException($"Không tìm thấy cơ sở y tế với mã ID: {id}");

        facility.IsActive = !facility.IsActive;
        facility.UpdatedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return await GetFacilityByIdAsync(facility.Id, cancellationToken);
    }

    public async Task<IReadOnlyList<BuildingDto>> GetBuildingsByFacilityAsync(long facilityId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Buildings
            .AsNoTracking()
            .Where(b => b.FacilityId == facilityId)
            .OrderBy(b => b.Code)
            .Select(b => new BuildingDto
            {
                Id = b.Id,
                FacilityId = b.FacilityId,
                FacilityName = b.Facility.Name,
                Code = b.Code,
                Name = b.Name,
                NumberOfFloors = b.NumberOfFloors,
                IsActive = b.IsActive,
                RoomCount = b.Rooms.Count
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<BuildingDto> CreateBuildingAsync(CreateBuildingRequest request, CancellationToken cancellationToken = default)
    {
        var facilityExists = await _dbContext.Facilities.AnyAsync(f => f.Id == request.FacilityId, cancellationToken);
        if (!facilityExists)
            throw new NotFoundException($"Không tìm thấy cơ sở y tế với ID: {request.FacilityId}");

        var codeExists = await _dbContext.Buildings
            .AnyAsync(b => b.FacilityId == request.FacilityId && b.Code == request.Code, cancellationToken);
        if (codeExists)
            throw new ConflictException($"Mã tòa nhà '{request.Code}' đã tồn tại trong cơ sở này.");

        var building = new Building
        {
            FacilityId = request.FacilityId,
            Code = request.Code.Trim().ToUpperInvariant(),
            Name = request.Name.Trim(),
            NumberOfFloors = request.NumberOfFloors,
            IsActive = true
        };

        _dbContext.Buildings.Add(building);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var facility = await _dbContext.Facilities.FindAsync(new object[] { request.FacilityId }, cancellationToken);
        return new BuildingDto
        {
            Id = building.Id,
            FacilityId = building.FacilityId,
            FacilityName = facility?.Name ?? string.Empty,
            Code = building.Code,
            Name = building.Name,
            NumberOfFloors = building.NumberOfFloors,
            IsActive = building.IsActive,
            RoomCount = 0
        };
    }

    public async Task<IReadOnlyList<DepartmentDto>> GetDepartmentsAsync(long? facilityId = null, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Departments
            .AsNoTracking()
            .Include(d => d.Facility)
            .Include(d => d.Building)
            .Include(d => d.HeadOfDepartmentDoctor)
            .Include(d => d.Rooms)
            .AsQueryable();

        if (facilityId.HasValue)
        {
            query = query.Where(d => d.FacilityId == facilityId.Value);
        }

        var departments = await query
            .OrderBy(d => d.Code)
            .ToListAsync(cancellationToken);

        var doctorUserIds = departments
            .Where(d => d.HeadOfDepartmentDoctor != null)
            .Select(d => d.HeadOfDepartmentDoctor!.UserId)
            .Distinct()
            .ToList();

        var usersMap = await _dbContext.Users
            .Where(u => doctorUserIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, cancellationToken);

        return departments.Select(d => new DepartmentDto
        {
            Id = d.Id,
            FacilityId = d.FacilityId,
            FacilityName = d.Facility.Name,
            BuildingId = d.BuildingId,
            BuildingName = d.Building?.Name,
            Code = d.Code,
            Name = d.Name,
            DepartmentType = d.DepartmentType,
            DepartmentTypeName = d.DepartmentType.ToString(),
            HeadOfDepartmentDoctorId = d.HeadOfDepartmentDoctorId,
            HeadOfDepartmentDoctorName = d.HeadOfDepartmentDoctor != null && usersMap.TryGetValue(d.HeadOfDepartmentDoctor.UserId, out var name) ? name : null,
            Description = d.Description,
            IsActive = d.IsActive,
            RoomCount = d.Rooms.Count
        }).ToList();
    }

    public async Task<DepartmentDto> GetDepartmentByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var d = await _dbContext.Departments
            .AsNoTracking()
            .Include(d => d.Facility)
            .Include(d => d.Building)
            .Include(d => d.HeadOfDepartmentDoctor)
            .Include(d => d.Rooms)
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

        if (d == null)
            throw new NotFoundException($"Không tìm thấy khoa phòng với ID: {id}");

        string? doctorName = null;
        if (d.HeadOfDepartmentDoctor != null)
        {
            var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == d.HeadOfDepartmentDoctor.UserId, cancellationToken);
            doctorName = user?.FullName;
        }

        return new DepartmentDto
        {
            Id = d.Id,
            FacilityId = d.FacilityId,
            FacilityName = d.Facility.Name,
            BuildingId = d.BuildingId,
            BuildingName = d.Building?.Name,
            Code = d.Code,
            Name = d.Name,
            DepartmentType = d.DepartmentType,
            DepartmentTypeName = d.DepartmentType.ToString(),
            HeadOfDepartmentDoctorId = d.HeadOfDepartmentDoctorId,
            HeadOfDepartmentDoctorName = doctorName,
            Description = d.Description,
            IsActive = d.IsActive,
            RoomCount = d.Rooms.Count
        };
    }

    public async Task<DepartmentDto> CreateDepartmentAsync(CreateDepartmentRequest request, CancellationToken cancellationToken = default)
    {
        var facilityExists = await _dbContext.Facilities.AnyAsync(f => f.Id == request.FacilityId, cancellationToken);
        if (!facilityExists)
            throw new NotFoundException($"Không tìm thấy cơ sở y tế với ID: {request.FacilityId}");

        var codeExists = await _dbContext.Departments
            .AnyAsync(d => d.FacilityId == request.FacilityId && d.Code == request.Code, cancellationToken);
        if (codeExists)
            throw new ConflictException($"Mã khoa phòng '{request.Code}' đã tồn tại trong cơ sở này.");

        var department = new Department
        {
            FacilityId = request.FacilityId,
            BuildingId = request.BuildingId,
            Code = request.Code.Trim().ToUpperInvariant(),
            Name = request.Name.Trim(),
            DepartmentType = request.DepartmentType,
            HeadOfDepartmentDoctorId = request.HeadOfDepartmentDoctorId,
            Description = request.Description?.Trim(),
            IsActive = true
        };

        _dbContext.Departments.Add(department);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return await GetDepartmentByIdAsync(department.Id, cancellationToken);
    }

    public async Task<IReadOnlyList<RoomDto>> GetRoomsAsync(long? departmentId = null, long? facilityId = null, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Rooms
            .AsNoTracking()
            .Include(r => r.Department)
                .ThenInclude(d => d.Facility)
            .Include(r => r.Building)
            .Include(r => r.Beds)
            .AsQueryable();

        if (departmentId.HasValue)
        {
            query = query.Where(r => r.DepartmentId == departmentId.Value);
        }

        if (facilityId.HasValue)
        {
            query = query.Where(r => r.Department.FacilityId == facilityId.Value);
        }

        return await query
            .OrderBy(r => r.RoomNumber)
            .Select(r => new RoomDto
            {
                Id = r.Id,
                DepartmentId = r.DepartmentId,
                DepartmentName = r.Department.Name,
                FacilityId = r.Department.FacilityId,
                FacilityName = r.Department.Facility.Name,
                BuildingId = r.BuildingId,
                BuildingName = r.Building != null ? r.Building.Name : null,
                RoomNumber = r.RoomNumber,
                Name = r.Name,
                RoomType = r.RoomType,
                RoomTypeName = r.RoomType.ToString(),
                FloorNumber = r.FloorNumber,
                MaxCapacity = r.MaxCapacity,
                IsActive = r.IsActive,
                BedCount = r.Beds.Count,
                AvailableBedCount = r.Beds.Count(b => b.Status == BedStatus.Available && b.IsActive)
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<RoomDto> GetRoomByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var room = await _dbContext.Rooms
            .AsNoTracking()
            .Where(r => r.Id == id)
            .Select(r => new RoomDto
            {
                Id = r.Id,
                DepartmentId = r.DepartmentId,
                DepartmentName = r.Department.Name,
                FacilityId = r.Department.FacilityId,
                FacilityName = r.Department.Facility.Name,
                BuildingId = r.BuildingId,
                BuildingName = r.Building != null ? r.Building.Name : null,
                RoomNumber = r.RoomNumber,
                Name = r.Name,
                RoomType = r.RoomType,
                RoomTypeName = r.RoomType.ToString(),
                FloorNumber = r.FloorNumber,
                MaxCapacity = r.MaxCapacity,
                IsActive = r.IsActive,
                BedCount = r.Beds.Count,
                AvailableBedCount = r.Beds.Count(b => b.Status == BedStatus.Available && b.IsActive)
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (room == null)
            throw new NotFoundException($"Không tìm thấy phòng với ID: {id}");

        return room;
    }

    public async Task<RoomDto> CreateRoomAsync(CreateRoomRequest request, CancellationToken cancellationToken = default)
    {
        var departmentExists = await _dbContext.Departments.AnyAsync(d => d.Id == request.DepartmentId, cancellationToken);
        if (!departmentExists)
            throw new NotFoundException($"Không tìm thấy khoa phòng với ID: {request.DepartmentId}");

        var numberExists = await _dbContext.Rooms
            .AnyAsync(r => r.DepartmentId == request.DepartmentId && r.RoomNumber == request.RoomNumber, cancellationToken);
        if (numberExists)
            throw new ConflictException($"Số phòng '{request.RoomNumber}' đã tồn tại trong khoa này.");

        var room = new Room
        {
            DepartmentId = request.DepartmentId,
            BuildingId = request.BuildingId,
            RoomNumber = request.RoomNumber.Trim(),
            Name = request.Name.Trim(),
            RoomType = request.RoomType,
            FloorNumber = request.FloorNumber,
            MaxCapacity = request.MaxCapacity,
            IsActive = true
        };

        _dbContext.Rooms.Add(room);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return await GetRoomByIdAsync(room.Id, cancellationToken);
    }

    public async Task<IReadOnlyList<BedDto>> GetBedsByRoomAsync(long roomId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Beds
            .AsNoTracking()
            .Where(b => b.RoomId == roomId)
            .OrderBy(b => b.BedNumber)
            .Select(b => new BedDto
            {
                Id = b.Id,
                RoomId = b.RoomId,
                RoomNumber = b.Room.RoomNumber,
                DepartmentId = b.Room.DepartmentId,
                DepartmentName = b.Room.Department.Name,
                BedNumber = b.BedNumber,
                BedType = b.BedType,
                BedTypeName = b.BedType.ToString(),
                DailyRate = b.DailyRate,
                Status = b.Status,
                StatusName = b.Status.ToString(),
                Notes = b.Notes,
                IsActive = b.IsActive
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<BedDto>> GetBedsByDepartmentAsync(long departmentId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Beds
            .AsNoTracking()
            .Where(b => b.Room.DepartmentId == departmentId)
            .OrderBy(b => b.Room.RoomNumber)
            .ThenBy(b => b.BedNumber)
            .Select(b => new BedDto
            {
                Id = b.Id,
                RoomId = b.RoomId,
                RoomNumber = b.Room.RoomNumber,
                DepartmentId = b.Room.DepartmentId,
                DepartmentName = b.Room.Department.Name,
                BedNumber = b.BedNumber,
                BedType = b.BedType,
                BedTypeName = b.BedType.ToString(),
                DailyRate = b.DailyRate,
                Status = b.Status,
                StatusName = b.Status.ToString(),
                Notes = b.Notes,
                IsActive = b.IsActive
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<BedDto> CreateBedAsync(CreateBedRequest request, CancellationToken cancellationToken = default)
    {
        var room = await _dbContext.Rooms.FirstOrDefaultAsync(r => r.Id == request.RoomId, cancellationToken);
        if (room == null)
            throw new NotFoundException($"Không tìm thấy phòng với ID: {request.RoomId}");

        var bedNumberExists = await _dbContext.Beds
            .AnyAsync(b => b.RoomId == request.RoomId && b.BedNumber == request.BedNumber, cancellationToken);
        if (bedNumberExists)
            throw new ConflictException($"Số giường '{request.BedNumber}' đã tồn tại trong phòng này.");

        var bed = new Bed
        {
            RoomId = request.RoomId,
            BedNumber = request.BedNumber.Trim(),
            BedType = request.BedType,
            DailyRate = request.DailyRate,
            Status = BedStatus.Available,
            Notes = request.Notes?.Trim(),
            IsActive = true
        };

        _dbContext.Beds.Add(bed);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new BedDto
        {
            Id = bed.Id,
            RoomId = bed.RoomId,
            RoomNumber = room.RoomNumber,
            DepartmentId = room.DepartmentId,
            DepartmentName = string.Empty,
            BedNumber = bed.BedNumber,
            BedType = bed.BedType,
            BedTypeName = bed.BedType.ToString(),
            DailyRate = bed.DailyRate,
            Status = bed.Status,
            StatusName = bed.Status.ToString(),
            Notes = bed.Notes,
            IsActive = bed.IsActive
        };
    }

    public async Task<BedDto> UpdateBedStatusAsync(long id, UpdateBedStatusRequest request, CancellationToken cancellationToken = default)
    {
        var bed = await _dbContext.Beds
            .Include(b => b.Room)
                .ThenInclude(r => r.Department)
            .FirstOrDefaultAsync(b => b.Id == id, cancellationToken);

        if (bed == null)
            throw new NotFoundException($"Không tìm thấy giường bệnh với ID: {id}");

        bed.Status = request.Status;
        if (!string.IsNullOrWhiteSpace(request.Notes))
        {
            bed.Notes = request.Notes.Trim();
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new BedDto
        {
            Id = bed.Id,
            RoomId = bed.RoomId,
            RoomNumber = bed.Room.RoomNumber,
            DepartmentId = bed.Room.DepartmentId,
            DepartmentName = bed.Room.Department.Name,
            BedNumber = bed.BedNumber,
            BedType = bed.BedType,
            BedTypeName = bed.BedType.ToString(),
            DailyRate = bed.DailyRate,
            Status = bed.Status,
            StatusName = bed.Status.ToString(),
            Notes = bed.Notes,
            IsActive = bed.IsActive
        };
    }
}
