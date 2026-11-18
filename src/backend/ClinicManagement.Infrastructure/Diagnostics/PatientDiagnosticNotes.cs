using ClinicManagement.Application.Diagnostics.DTOs;

namespace ClinicManagement.Infrastructure.Diagnostics;

internal static class PatientDiagnosticNotes
{
    internal static void ClearInternalNotes(DiagnosticOrderDto order)
    {
        order.VerificationNote = null;
        order.Note = order.Note?.Replace("Dọn dữ liệu demo cũ",
            "Chỉ định đã quá hạn thực hiện. Vui lòng tái khám để được đánh giá lại.", StringComparison.Ordinal);
    }
}
