namespace ClinicManagement.Domain.Enums;

public enum SpecimenRejectionReason
{
    Hemolyzed = 1,
    Clotted = 2,
    InsufficientVolume = 3,
    WrongContainer = 4,
    LeakedOrBroken = 5,
    LabelMismatch = 6,
    PatientNotPrepared = 7,
    Other = 99
}
