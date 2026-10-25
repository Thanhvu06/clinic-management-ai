using System;
using System.Collections.Generic;
using ClinicManagement.Application.Appointments.DTOs;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Json;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Doctors.Interfaces;
using ClinicManagement.Application.Appointments.DTOs.Doctor;
using ClinicManagement.Application.Appointments.DTOs.Revisit;
using ClinicManagement.Application.Appointments.Interfaces;
using ClinicManagement.Application.Authentication.Interfaces;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Diagnostics.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Application.Prescriptions.DTOs;
using ClinicManagement.Application.Prescriptions.Interfaces;
using ClinicManagement.Infrastructure.Prescriptions;
using ClinicManagement.Application.Visits.DTOs;
using ClinicManagement.Application.Visits.Interfaces;
using ClinicManagement.Infrastructure.Common;
using ClinicManagement.Infrastructure.Persistence;
using ClinicManagement.Infrastructure.Pharmacy;
using ClinicManagement.Infrastructure.Visits;
using Microsoft.EntityFrameworkCore;


using ClinicManagement.Infrastructure.Expiry;
using ClinicManagement.Application.Common.Expiry;
using Microsoft.Extensions.Options;
namespace ClinicManagement.Infrastructure.Appointments;

public partial class DoctorAppointmentService
{
    private DoctorAppointmentDto MapToDto(Appointment a, string patientName, string patientPhone, Gender? gender, DateOnly? dob) => new()
    {
        Id = a.Id,
        AppointmentCode = a.AppointmentCode,
        PatientId = a.PatientId,
        DoctorId = a.DoctorId,
        SpecialtyId = a.SpecialtyId,
        AppointmentSlotId = a.AppointmentSlotId,
        AppointmentDate = a.AppointmentDate,
        StartTime = a.StartTime,
        EndTime = a.EndTime,
        Reason = a.Reason,
        Status = a.Status.ToString(),
        IsCompletionOverdue = ConsultationCompletionExpiryRule.IsOverdue(a.Status.ToString(), a.AppointmentDate, _dateTimeProvider),
        PatientName = patientName ?? string.Empty,
        PatientPhone = patientPhone ?? string.Empty,
        PatientGender = gender?.ToString() ?? string.Empty,
        PatientDob = dob
    };

}
