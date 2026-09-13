using System;
using System.Collections.Generic;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.Entities;

public class Patient
{
    public long Id { get; set; }
    public Guid? UserId { get; set; }
    public string MedicalRecordNumber { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string? Email { get; set; }
    public Gender? Gender { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public string? Address { get; set; }
    public string? NationalId { get; set; }
    public string? BhytNumber { get; set; }
    public string? BloodType { get; set; }
    public string? RhFactor { get; set; }
    public long? PrimaryFacilityId { get; set; }

    public Facility? PrimaryFacility { get; set; }
    public ICollection<PatientAllergy> Allergies { get; set; } = new List<PatientAllergy>();
    public ICollection<EmergencyContact> EmergencyContacts { get; set; } = new List<EmergencyContact>();
    public ICollection<Appointment> Appointments { get; set; } = new List<Appointment>();
    public ICollection<RevisitRequest> RevisitRequests { get; set; } = new List<RevisitRequest>();
    public ICollection<AiSuggestionLog> AiSuggestionLogs { get; set; } = new List<AiSuggestionLog>();
}
