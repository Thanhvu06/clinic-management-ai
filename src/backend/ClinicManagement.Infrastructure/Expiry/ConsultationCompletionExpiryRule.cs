using ClinicManagement.Application.Common.Interfaces;

namespace ClinicManagement.Infrastructure.Expiry;

public static class ConsultationCompletionExpiryRule
{
    public static bool IsOverdue(string status, DateOnly date, IDateTimeProvider clock) =>
        status == "InConsultation" && date < clock.VietnamToday;
}
