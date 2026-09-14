using System;
using System.Collections.Generic;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.Entities;

public class PatientVisit
{
    public long Id { get; set; }
    public string VisitCode { get; set; } = string.Empty;
    public long PatientId { get; set; }
    public long? AppointmentId { get; set; }
    public long? HealthPackageRegistrationId { get; set; }
    public long FacilityId { get; set; }
    public long DepartmentId { get; set; }
    public long? RoomId { get; set; }
    public long? AssignedDoctorId { get; set; }

    public DateOnly VisitDate { get; set; }
    public VisitArrivalType ArrivalType { get; set; } = VisitArrivalType.WalkIn;
    public VisitPriority Priority { get; set; } = VisitPriority.Normal;
    public string? ChiefComplaint { get; set; }
    public int QueueNumber { get; set; }
    public VisitStatus Status { get; set; } = VisitStatus.WaitingForDoctor;

    public DateTime CheckedInAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ConsultationStartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public string? CancellationReason { get; set; }

    public Guid CreatedByUserId { get; set; }
    public byte[]? RowVersion { get; set; } = Guid.NewGuid().ToByteArray();
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }

    // Navigation properties
    public Patient Patient { get; set; } = null!;
    public Appointment? Appointment { get; set; }
    public HealthPackageRegistration? HealthPackageRegistration { get; set; }
    public Facility Facility { get; set; } = null!;
    public Department Department { get; set; } = null!;
    public Room? Room { get; set; }
    public Doctor? AssignedDoctor { get; set; }

    public VisitSummary? VisitSummary { get; set; }
    public AppointmentVitalSigns? VitalSigns { get; set; }
    public ICollection<DiagnosticOrder> DiagnosticOrders { get; set; } = new List<DiagnosticOrder>();
    public ICollection<Prescription> Prescriptions { get; set; } = new List<Prescription>();
    public ICollection<Invoice> Invoices { get; set; } = new List<Invoice>();
}
