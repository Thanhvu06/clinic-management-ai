using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.Policies;

/// <summary>
/// Single source of truth for when a diagnostic result may be shown to a patient.
/// A completed order is not patient-visible until a doctor has reviewed it.
/// </summary>
public static class DiagnosticResultPublicationPolicy
{
    public static bool IsPublishedToPatient(DiagnosticOrderStatus status, DateTime? reviewedAtUtc) =>
        status == DiagnosticOrderStatus.Completed && reviewedAtUtc.HasValue;
}
