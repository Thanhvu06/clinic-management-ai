using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Application.Mpi.DTOs;

public class MpiPatientDto
{
    public long Id { get; set; }
    public Guid? UserId { get; set; }
    public string MedicalRecordNumber { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string? Email { get; set; }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public Gender? Gender { get; set; }
    public string? GenderName => Gender switch
    {
        Domain.Enums.Gender.Male => "Nam",
        Domain.Enums.Gender.Female => "Nữ",
        Domain.Enums.Gender.Other => "Khác",
        _ => null
    };
    public DateOnly? DateOfBirth { get; set; }
    public int? Age { get; set; }
    public string? Address { get; set; }
    public string? NationalId { get; set; }
    public string? BhytNumber { get; set; }
    public string? BloodType { get; set; }
    public string? RhFactor { get; set; }
    public long? PrimaryFacilityId { get; set; }
    public string? PrimaryFacilityName { get; set; }
    public List<PatientAllergyDto> Allergies { get; set; } = new();
    public List<EmergencyContactDto> EmergencyContacts { get; set; } = new();
}

public class PatientAllergyDto
{
    public long Id { get; set; }
    public long PatientId { get; set; }
    public AllergenType AllergenType { get; set; }
    public string AllergenTypeName { get; set; } = string.Empty;
    public string AllergenName { get; set; } = string.Empty;
    public AllergySeverity Severity { get; set; }
    public string SeverityName { get; set; } = string.Empty;
    public string? ReactionDescription { get; set; }
    public DateTime RecordedAtUtc { get; set; }
}

public class CreatePatientAllergyRequest
{
    [Required(ErrorMessage = "Loại dị nguyên là bắt buộc")]
    public AllergenType AllergenType { get; set; } = AllergenType.Drug;

    [Required(ErrorMessage = "Tên dị nguyên/thuốc là bắt buộc")]
    [MaxLength(200)]
    public string AllergenName { get; set; } = string.Empty;

    public AllergySeverity Severity { get; set; } = AllergySeverity.Moderate;

    [MaxLength(500)]
    public string? ReactionDescription { get; set; }
}

public class EmergencyContactDto
{
    public long Id { get; set; }
    public long PatientId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Relationship { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string? Address { get; set; }
    public bool IsPrimary { get; set; }
}

public class RegisterWalkInPatientRequest
{
    [Required(ErrorMessage = "Họ và tên bệnh nhân là bắt buộc")]
    [MaxLength(200)]
    public string FullName { get; set; } = string.Empty;

    [Phone(ErrorMessage = "Số điện thoại không hợp lệ")]
    public string? PhoneNumber { get; set; }

    [EmailAddress(ErrorMessage = "Email không hợp lệ")]
    public string? Email { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    [EnumDataType(typeof(Gender), ErrorMessage = "Giới tính không hợp lệ")]
    public Gender? Gender { get; set; }
    public DateOnly? DateOfBirth { get; set; }

    [MaxLength(500)]
    public string? Address { get; set; }

    [MaxLength(20)]
    public string? NationalId { get; set; }

    [MaxLength(20)]
    public string? BhytNumber { get; set; }

    [MaxLength(10)]
    public string? BloodType { get; set; }

    [MaxLength(10)]
    public string? RhFactor { get; set; }

    public long? PrimaryFacilityId { get; set; }

    public List<CreatePatientAllergyRequest>? Allergies { get; set; }
    public EmergencyContactDto? EmergencyContact { get; set; }
}

public class UpdateMpiPatientRequest
{
    [Required(ErrorMessage = "Họ và tên là bắt buộc")]
    [MaxLength(200)]
    public string FullName { get; set; } = string.Empty;

    public string? PhoneNumber { get; set; }
    public string? Email { get; set; }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    [EnumDataType(typeof(Gender), ErrorMessage = "Giới tính không hợp lệ")]
    public Gender? Gender { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public string? Address { get; set; }
    public string? NationalId { get; set; }
    public string? BhytNumber { get; set; }
    public string? BloodType { get; set; }
    public string? RhFactor { get; set; }
    public long? PrimaryFacilityId { get; set; }
}

public class PatientSearchQuery
{
    public string? SearchTerm { get; set; }
    public string? MedicalRecordNumber { get; set; }
    public string? NationalId { get; set; }
    public string? BhytNumber { get; set; }
    public string? PhoneNumber { get; set; }
    public long? FacilityId { get; set; }
    public int Page { get; set; } = 1;

    private int _pageSize = 20;
    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = Math.Clamp(value, 1, 100);
    }
}
