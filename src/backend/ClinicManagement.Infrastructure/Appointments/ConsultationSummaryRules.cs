using ClinicManagement.Application.Common.Exceptions;

namespace ClinicManagement.Infrastructure.Appointments;

public static class ConsultationSummaryRules
{
    // Older builds pre-filled this sentence when a consultation started; drafts saved then still carry it.
    public const string LegacyPlaceholder = "Đang trong quá trình thăm khám.";

    public static bool IsMissing(string? summary)
    {
        if (string.IsNullOrWhiteSpace(summary)) return true;
        return string.Equals(summary.Trim(), LegacyPlaceholder, StringComparison.OrdinalIgnoreCase);
    }

    public static string? HideLegacyPlaceholder(string? summary) =>
        summary != null && IsMissing(summary) ? string.Empty : summary;

    public static void EnsureProvided(string? summary)
    {
        if (IsMissing(summary))
            throw new BusinessException("VALIDATION_ERROR", "Tóm tắt kết luận khám không được để trống.");
    }
}
