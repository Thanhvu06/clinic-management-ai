using ClinicManagement.Domain.Enums;
using System.ComponentModel.DataAnnotations.Schema;

namespace ClinicManagement.Domain.Entities;

public class Prescription
{
    public long Id { get; set; }
    public long AppointmentId { get; set; }
    public long PatientId { get; set; }
    public long DoctorId { get; set; }
    
    public PrescriptionStatus Status { get; set; } = PrescriptionStatus.Draft;
    public string? Notes { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DispensedAt { get; set; }
    public Guid? DispensedByUserId { get; set; }

    public byte[]? RowVersion { get; set; }

    [ForeignKey("AppointmentId")]
    public Appointment? Appointment { get; set; }
    
    [ForeignKey("PatientId")]
    public Patient? Patient { get; set; }
    
    [ForeignKey("DoctorId")]
    public Doctor? Doctor { get; set; }
    
    public ICollection<PrescriptionItem> Items { get; set; } = new List<PrescriptionItem>();
}
