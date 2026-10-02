namespace ClinicManagement.Domain.Enums;

public enum VisitStatus
{
    Registered = 1,
    CheckedIn = 2,
    WaitingForDoctor = 3,
    WaitingDoctor = 3,
    InConsultation = 4,
    WaitingForDiagnostics = 5,
    InDiagnostics = 5,
    ResultsReady = 6,
    Completed = 7,
    Cancelled = 8,
    NoShow = 9,
    Transferred = 10,
    ConsultationCompleted = 11,
    InPharmacy = 12,
    InBilling = 13
}

