using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ClinicManagement.Application.Visits.DTOs;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Application.Visits.Interfaces;

public interface IPatientVisitService
{
    Task<CheckInTicketDto> ReceptionIntakeAsync(ReceptionIntakeRequest request, CancellationToken cancellationToken = default);
    Task<CheckInTicketDto> CreateWalkInVisitAsync(WalkInRegistrationRequest request, CancellationToken cancellationToken = default);
    Task<CheckInTicketDto> CheckInAppointmentAsync(AppointmentCheckInRequest request, CancellationToken cancellationToken = default);
    Task<PatientVisitDetailDto> GetVisitByIdAsync(long visitId, CancellationToken cancellationToken = default);
    Task<List<DepartmentQueueItemDto>> GetDepartmentQueueAsync(long departmentId, DateOnly? date = null, CancellationToken cancellationToken = default);
    Task<PatientVisitDetailDto> AssignDoctorAsync(long visitId, AssignDoctorRequest request, CancellationToken cancellationToken = default);
    Task<PatientVisitDetailDto> UpdateVisitStatusAsync(long visitId, VisitStatus newStatus, string? reason = null, CancellationToken cancellationToken = default);
    Task<CheckInTicketDto> GetCheckInTicketAsync(long visitId, CancellationToken cancellationToken = default);
}
