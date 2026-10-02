using System;
using System.Threading;
using System.Threading.Tasks;

namespace ClinicManagement.Application.Appointments.Interfaces;

public class SlotAvailabilityRequest
{
    public long SlotId { get; set; }
    public long? DoctorId { get; set; }
    public long? SpecialtyId { get; set; }
    public long? PatientId { get; set; }
    public bool CheckAiEnabledSpecialty { get; set; } = false;
}

public class SlotAvailabilityResult
{
    public bool IsAvailable { get; set; }
    public string? ReasonCode { get; set; }
    public string? FailureReason { get; set; }

    public long? SlotId { get; set; }
    public long? DoctorId { get; set; }
    public string? DoctorName { get; set; }
    public string? AcademicTitle { get; set; }
    public long? SpecialtyId { get; set; }
    public string? SpecialtyName { get; set; }
    public DateOnly? SlotDate { get; set; }
    public TimeOnly? StartTime { get; set; }
    public TimeOnly? EndTime { get; set; }

    public static SlotAvailabilityResult Success(
        long slotId,
        long doctorId,
        string doctorName,
        string? academicTitle,
        long specialtyId,
        string specialtyName,
        DateOnly slotDate,
        TimeOnly startTime,
        TimeOnly endTime) => new()
    {
        IsAvailable = true,
        SlotId = slotId,
        DoctorId = doctorId,
        DoctorName = doctorName,
        AcademicTitle = academicTitle,
        SpecialtyId = specialtyId,
        SpecialtyName = specialtyName,
        SlotDate = slotDate,
        StartTime = startTime,
        EndTime = endTime
    };

    public static SlotAvailabilityResult Failure(string reasonCode, string failureReason) => new()
    {
        IsAvailable = false,
        ReasonCode = reasonCode,
        FailureReason = failureReason
    };
}

public class BatchSlotAvailabilityRequest
{
    public long? DoctorId { get; set; }
    public List<long>? DoctorIds { get; set; }
    public long? SpecialtyId { get; set; }
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
    public long? PatientId { get; set; }
    public bool CheckAiEnabledSpecialty { get; set; } = false;
    public string? TimePreference { get; set; }
    public int? Limit { get; set; }
    public bool ThrowOnValidationFailure { get; set; } = false;
}

public interface IAppointmentAvailabilityPolicy
{
    Task<SlotAvailabilityResult> EvaluateSlotAvailabilityAsync(SlotAvailabilityRequest request, CancellationToken cancellationToken = default);
    Task<List<ClinicManagement.Application.Doctors.DTOs.AvailableSlotDto>> GetAvailableSlotsAsync(BatchSlotAvailabilityRequest request, CancellationToken cancellationToken = default);
}
