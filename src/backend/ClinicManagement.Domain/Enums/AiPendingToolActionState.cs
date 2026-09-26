namespace ClinicManagement.Domain.Enums;

public enum AiPendingToolActionState
{
    PendingConfirmation,
    Executing,
    Completed,
    FailedRetryable,
    FailedTerminal,
    Cancelled,
    Expired
}
