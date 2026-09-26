using System;
using System.Collections.Generic;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.Entities;

public class DiagnosticOrder
{
    public long Id { get; set; }
    public string OrderCode { get; set; } = string.Empty;

    public long? AppointmentId { get; set; }
    public long? PatientVisitId { get; set; }
    public long PatientId { get; set; }
    public long OrderingDoctorId { get; set; }
    public long? FacilityId { get; set; }
    public long? PerformingDepartmentId { get; set; }

    public string ClinicalIndication { get; set; } = string.Empty;
    public string? Note { get; set; }

    public DiagnosticOrderStatus Status { get; set; } = DiagnosticOrderStatus.Ordered;

    public DateTime OrderedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime? CancelledAtUtc { get; set; }

    public Guid? StartedByUserId { get; set; }
    public Guid? CompletedByUserId { get; set; }

    public DateTime? ReviewedAtUtc { get; set; }
    public long? ReviewedByDoctorId { get; set; }

    public byte[]? RowVersion { get; set; } = Guid.NewGuid().ToByteArray();

    public Appointment? Appointment { get; set; }
    public PatientVisit? PatientVisit { get; set; }
    public Patient Patient { get; set; } = null!;
    public Doctor OrderingDoctor { get; set; } = null!;
    public Doctor? ReviewedByDoctor { get; set; }
    public Facility? Facility { get; set; }
    public Department? PerformingDepartment { get; set; }

    public ICollection<DiagnosticOrderItem> Items { get; set; } = new List<DiagnosticOrderItem>();
}