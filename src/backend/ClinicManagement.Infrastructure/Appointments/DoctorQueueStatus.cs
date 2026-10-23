namespace ClinicManagement.Infrastructure.Appointments;

internal static class DoctorQueueStatus
{
    internal static bool IsConsultationInProgress(string status) =>
        status is "InConsultation" or "WaitingForDiagnostics" or "InDiagnostics" or "ResultsReady";
}
