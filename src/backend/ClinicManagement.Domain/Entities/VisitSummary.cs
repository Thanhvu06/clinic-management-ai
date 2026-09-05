namespace ClinicManagement.Domain.Entities;

public class VisitSummary
{
    public long Id { get; set; }
    public long AppointmentId { get; set; }
    public long DoctorId { get; set; }

    public string? ChiefComplaint { get; set; }
    public string? ClinicalFindings { get; set; }
    public string? Diagnosis { get; set; }
    public string? DiagnosisCode { get; set; }
    public string? TreatmentPlan { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string? FollowUpInstruction { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }

    public byte[]? RowVersion { get; set; } = Guid.NewGuid().ToByteArray();

    public Appointment Appointment { get; set; } = null!;
    public Doctor Doctor { get; set; } = null!;
}
