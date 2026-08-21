namespace ClinicManagement.Domain.Entities;

public class VisitSummary
{
    public long Id { get; set; }
    public long AppointmentId { get; set; }
    public long DoctorId { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string? FollowUpInstruction { get; set; }

    public Appointment Appointment { get; set; } = null!;
    public Doctor Doctor { get; set; } = null!;
}
