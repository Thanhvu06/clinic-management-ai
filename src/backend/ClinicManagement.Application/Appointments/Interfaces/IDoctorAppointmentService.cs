using System.Collections.Generic;
using System.Threading.Tasks;
using ClinicManagement.Application.Appointments.DTOs;
using ClinicManagement.Application.Appointments.DTOs.Doctor;
using ClinicManagement.Application.Appointments.DTOs.Revisit;
using ClinicManagement.Application.Common.Models;

namespace ClinicManagement.Application.Appointments.Interfaces;

public interface IDoctorAppointmentService
{
    // Dashboard & Schedule
    Task<DoctorDashboardDto> GetDoctorDashboardAsync(DateOnly? date);
    Task<List<DoctorScheduleDayDto>> GetDoctorScheduleAsync(DateOnly fromDate, DateOnly toDate);

    // Appointments query & detail
    Task<PagedResult<DoctorAppointmentDto>> GetMyAppointmentsAsync(DateOnly? date, string? status, string? search, int page, int pageSize);
    Task<DoctorAppointmentDto> GetAppointmentByIdAsync(long appointmentId);
    Task<List<AppointmentHistoryDto>> GetAppointmentHistoryAsync(long appointmentId);

    // Patient clinical context
    Task<PatientClinicalContextDto> GetPatientClinicalContextAsync(long appointmentId);

    // Workflow state transitions
    Task CheckInAppointmentAsync(long appointmentId);
    Task StartConsultationAsync(long appointmentId);
    Task CompleteAppointmentAsync(long appointmentId, CompleteConsultationRequest request);
    Task MarkNoShowAsync(long appointmentId, NoShowAppointmentDto request);
    Task<RevisitRequestDto> CreateRevisitRequestAsync(long appointmentId, CreateRevisitRequestDto request);

    // Clinical encounter & vital signs
    Task<ClinicalEncounterDto?> GetEncounterAsync(long appointmentId);
    Task<ClinicalEncounterDto> SaveEncounterAsync(long appointmentId, SaveEncounterRequest request);
    Task<VitalSignsDto?> GetVitalSignsAsync(long appointmentId);
    Task<VitalSignsDto> SaveVitalSignsAsync(long appointmentId, SaveVitalSignsRequest request);

    // Prescription draft & issuance
    Task<PrescriptionDraftDto?> GetPrescriptionDraftAsync(long appointmentId);
    Task<PrescriptionDraftDto> SavePrescriptionDraftAsync(long appointmentId, SavePrescriptionDraftRequest request);
}

public interface IRevisitService
{
    Task<PagedResult<RevisitRequestDto>> GetMyRevisitRequestsAsync(string? status, int page, int pageSize);
    Task<RevisitRequestDto> GetRevisitRequestByIdAsync(long id);
    Task AcceptRevisitRequestAsync(long id, AcceptRevisitRequestDto request);
    Task RejectRevisitRequestAsync(long id, RejectRevisitRequestDto request);
}
