using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Application.Organization.DTOs;

public class FacilityDto
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? TaxCode { get; set; }
    public string? HospitalLevel { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    public int BuildingCount { get; set; }
    public int DepartmentCount { get; set; }
}

public class CreateFacilityRequest
{
    [Required(ErrorMessage = "Mã cơ sở là bắt buộc")]
    [MaxLength(50)]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "Tên cơ sở là bắt buộc")]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Địa chỉ là bắt buộc")]
    public string Address { get; set; } = string.Empty;

    [Required(ErrorMessage = "Tỉnh/Thành phố là bắt buộc")]
    public string City { get; set; } = string.Empty;

    [Required(ErrorMessage = "Số điện thoại là bắt buộc")]
    public string Phone { get; set; } = string.Empty;

    [EmailAddress]
    public string? Email { get; set; }
    public string? TaxCode { get; set; }
    public string? HospitalLevel { get; set; }
    public string? Description { get; set; }
}

public class UpdateFacilityRequest
{
    [Required(ErrorMessage = "Tên cơ sở là bắt buộc")]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Địa chỉ là bắt buộc")]
    public string Address { get; set; } = string.Empty;

    [Required(ErrorMessage = "Tỉnh/Thành phố là bắt buộc")]
    public string City { get; set; } = string.Empty;

    [Required(ErrorMessage = "Số điện thoại là bắt buộc")]
    public string Phone { get; set; } = string.Empty;

    [EmailAddress]
    public string? Email { get; set; }
    public string? TaxCode { get; set; }
    public string? HospitalLevel { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; }
}

public class BuildingDto
{
    public long Id { get; set; }
    public long FacilityId { get; set; }
    public string FacilityName { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int NumberOfFloors { get; set; }
    public bool IsActive { get; set; }
    public int RoomCount { get; set; }
}

public class CreateBuildingRequest
{
    [Required(ErrorMessage = "Cơ sở là bắt buộc")]
    public long FacilityId { get; set; }

    [Required(ErrorMessage = "Mã tòa nhà là bắt buộc")]
    [MaxLength(50)]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "Tên tòa nhà là bắt buộc")]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Range(1, 100)]
    public int NumberOfFloors { get; set; } = 1;
}

public class DepartmentDto
{
    public long Id { get; set; }
    public long FacilityId { get; set; }
    public string FacilityName { get; set; } = string.Empty;
    public long? BuildingId { get; set; }
    public string? BuildingName { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DepartmentType DepartmentType { get; set; }
    public string DepartmentTypeName { get; set; } = string.Empty;
    public long? HeadOfDepartmentDoctorId { get; set; }
    public string? HeadOfDepartmentDoctorName { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    public int RoomCount { get; set; }
}

public class CreateDepartmentRequest
{
    [Required(ErrorMessage = "Cơ sở là bắt buộc")]
    public long FacilityId { get; set; }

    public long? BuildingId { get; set; }

    [Required(ErrorMessage = "Mã khoa phòng là bắt buộc")]
    [MaxLength(50)]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "Tên khoa phòng là bắt buộc")]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    public DepartmentType DepartmentType { get; set; } = DepartmentType.Clinical;
    public long? HeadOfDepartmentDoctorId { get; set; }
    public string? Description { get; set; }
}

public class RoomDto
{
    public long Id { get; set; }
    public long DepartmentId { get; set; }
    public string DepartmentName { get; set; } = string.Empty;
    public long FacilityId { get; set; }
    public string FacilityName { get; set; } = string.Empty;
    public long? BuildingId { get; set; }
    public string? BuildingName { get; set; }
    public string RoomNumber { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public RoomType RoomType { get; set; }
    public string RoomTypeName { get; set; } = string.Empty;
    public int FloorNumber { get; set; }
    public int MaxCapacity { get; set; }
    public bool IsActive { get; set; }
    public int BedCount { get; set; }
    public int AvailableBedCount { get; set; }
}

public class CreateRoomRequest
{
    [Required(ErrorMessage = "Khoa phòng là bắt buộc")]
    public long DepartmentId { get; set; }

    public long? BuildingId { get; set; }

    [Required(ErrorMessage = "Số phòng là bắt buộc")]
    [MaxLength(50)]
    public string RoomNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "Tên phòng là bắt buộc")]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    public RoomType RoomType { get; set; } = RoomType.Consultation;
    public int FloorNumber { get; set; } = 1;
    public int MaxCapacity { get; set; } = 1;
}

public class BedDto
{
    public long Id { get; set; }
    public long RoomId { get; set; }
    public string RoomNumber { get; set; } = string.Empty;
    public long DepartmentId { get; set; }
    public string DepartmentName { get; set; } = string.Empty;
    public string BedNumber { get; set; } = string.Empty;
    public BedType BedType { get; set; }
    public string BedTypeName { get; set; } = string.Empty;
    public decimal DailyRate { get; set; }
    public BedStatus Status { get; set; }
    public string StatusName { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public bool IsActive { get; set; }
}

public class CreateBedRequest
{
    [Required(ErrorMessage = "Phòng bệnh là bắt buộc")]
    public long RoomId { get; set; }

    [Required(ErrorMessage = "Số giường là bắt buộc")]
    [MaxLength(50)]
    public string BedNumber { get; set; } = string.Empty;

    public BedType BedType { get; set; } = BedType.Standard;
    public decimal DailyRate { get; set; }
    public string? Notes { get; set; }
}

public class UpdateBedStatusRequest
{
    [Required]
    public BedStatus Status { get; set; }
    public string? Notes { get; set; }
}
