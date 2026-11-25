namespace ClinicManagement.Infrastructure.AI;

public partial class AiSpecialtyService
{
    private static string? ResolveDoctorSearchName(string message, string? localName, string? providerName,
        List<DoctorLookupItem> doctors)
    {
        if (PatientChatDoctorName.LooksLikeName(localName)) return localName;

        var candidate = PatientChatDoctorName.ExtractCandidate(message);
        if (candidate is not null)
            return PatientChatDoctorName.IsCandidate(candidate) && MatchDoctors(candidate, doctors).Count > 0
                ? candidate : null;

        return PatientChatDoctorName.LooksLikeName(providerName) ? providerName : null;
    }
}
