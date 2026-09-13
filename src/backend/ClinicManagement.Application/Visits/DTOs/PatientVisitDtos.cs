using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Application.Visits.DTOs;

public class WalkInRegistrationRequest
{
    [Required(ErrorMessage = "Họ và tên không được để trống.")]
    [MaxLength(150)]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Số điện thoại không được để trống.")]
    [Phone(ErrorMessage = "Số điện thoại không hợp lệ.")]
    [MaxLength(20)]
    public string PhoneNumber { get; set; } = string.Empty;

    public DateOnly? DateOfBirth { get; set; }

    public Gender? Gender { get; set; }

    [MaxLength(250)]
    public string? Address { get; set; }

    [MaxLength(20)]
    public string? IdentityCardNumber { get; set; }

    public long? FacilityId { get; set; }

    [Required(ErrorMessage = "Khoa tiếp nhận không được để trống.")]
    public long DepartmentId { get; set; }

    public long? RoomId { get; set; }

    public long? AssignedDoctorId { get; set; }

    [MaxLength(1000)]
    public string? ChiefComplaint { get; set; }

    public VisitPriority Priority { get; set; } = VisitPriority.Normal;
}

public class AppointmentCheckInRequest
{
    [Required(ErrorMessage = "Mã cuộc hẹn không được để trống.")]
    public long AppointmentId { get; set; }

    public long? FacilityId { get; set; }

    public long? DepartmentId { get; set; }

    public long? RoomId { get; set; }

    public long? AssignedDoctorId { get; set; }
}

public class AssignDoctorRequest
{
    [Required(ErrorMessage = "Bác sĩ không được để trống.")]
    public long DoctorId { get; set; }

    public long? RoomId { get; set; }
}

public class CheckInTicketDto
{
    public long VisitId { get; set; }
    public string VisitCode { get; set; } = string.Empty;
    public long? AppointmentId { get; set; }
    public string? AppointmentCode { get; set; }
    public long PatientId { get; set; }
    public string PatientName { get; set; } = string.Empty;
    public string MedicalRecordNumber { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public int QueueNumber { get; set; }
    public string QueueDisplay { get; set; } = string.Empty;
    public long FacilityId { get; set; }
    public string FacilityName { get; set; } = string.Empty;
    public long DepartmentId { get; set; }
    public string DepartmentName { get; set; } = string.Empty;
    public long? RoomId { get; set; }
    public string? RoomNumber { get; set; }
    public long? AssignedDoctorId { get; set; }
    public string? DoctorName { get; set; }
    public DateTime CheckedInAtUtc { get; set; }
    public string ReceptionistName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public string ArrivalType { get; set; } = string.Empty;
}

public class DepartmentQueueItemDto
{
    public long VisitId { get; set; }
    public string VisitCode { get; set; } = string.Empty;
    public long? AppointmentId { get; set; }
    public string? AppointmentCode { get; set; }
    public int QueueNumber { get; set; }
    public string QueueDisplay { get; set; } = string.Empty;
    public long PatientId { get; set; }
    public string PatientName { get; set; } = string.Empty;
    public string MedicalRecordNumber { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string? Gender { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public int? Age { get; set; }
    public string Priority { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string ArrivalType { get; set; } = string.Empty;
    public string? ChiefComplaint { get; set; }
    public DateTime CheckedInAtUtc { get; set; }
    public long? AssignedDoctorId { get; set; }
    public string? AssignedDoctorName { get; set; }
    public long? RoomId { get; set; }
    public string? RoomNumber { get; set; }
    public bool HasVitalSigns { get; set; }
}

public class PatientVisitDetailDto
{
    public long Id { get; set; }
    public string VisitCode { get; set; } = string.Empty;
    public long? AppointmentId { get; set; }
    public string? AppointmentCode { get; set; }
    public long PatientId { get; set; }
    public string PatientName { get; set; } = string.Empty;
    public string MedicalRecordNumber { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public DateOnly? DateOfBirth { get; set; }
    public string? Gender { get; set; }
    public string? Address { get; set; }
    public string? IdentityCardNumber { get; set; }

    public long FacilityId { get; set; }
    public string FacilityName { get; set; } = string.Empty;
    public long DepartmentId { get; set; }
    public string DepartmentName { get; set; } = string.Empty;
    public long? RoomId { get; set; }
    public string? RoomNumber { get; set; }
    public long? AssignedDoctorId { get; set; }
    public string? DoctorName { get; set; }

    public DateOnly VisitDate { get; set; }
    public string ArrivalType { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public string? ChiefComplaint { get; set; }
    public int QueueNumber { get; set; }
    public string QueueDisplay { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;

    public DateTime CheckedInAtUtc { get; set; }
    public DateTime? ConsultationStartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public string? CancellationReason { get; set; }

    public bool HasVitalSigns { get; set; }
    public bool HasEncounter { get; set; }
    public int DiagnosticOrdersCount { get; set; }
    public int PendingDiagnosticOrdersCount { get; set; }
    public bool HasPrescription { get; set; }
    public string? PrescriptionStatus { get; set; }
    public bool HasInvoice { get; set; }
    public string? InvoiceStatus { get; set; }
    public string? RowVersion { get; set; }
}
