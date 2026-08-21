using System;
using System.Collections.Generic;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.Entities;

public class Patient
{
    public long Id { get; set; }
    public Guid UserId { get; set; }
    public Gender? Gender { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public string? Address { get; set; }

    public ICollection<Appointment> Appointments { get; set; } = new List<Appointment>();
    public ICollection<RevisitRequest> RevisitRequests { get; set; } = new List<RevisitRequest>();
    public ICollection<AiSuggestionLog> AiSuggestionLogs { get; set; } = new List<AiSuggestionLog>();
}
