using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Application.Diagnostics;

/// <summary>
/// Chỉ tiêu thời gian trả kết quả (TAT, từ chỉ định đến hoàn tất phiếu) theo loại dịch vụ CLS.
/// </summary>
public class DiagnosticTatOptions
{
    public const string SectionName = "DiagnosticTat";

    public DiagnosticTatTargets TargetMinutes { get; set; } = new();

    public int TargetFor(DiagnosticCategory category) => category switch
    {
        DiagnosticCategory.Laboratory => TargetMinutes.Laboratory,
        DiagnosticCategory.Ultrasound => TargetMinutes.Ultrasound,
        DiagnosticCategory.Imaging => TargetMinutes.Imaging,
        _ => TargetMinutes.Other
    };
}

public class DiagnosticTatTargets
{
    public int Laboratory { get; set; } = 60;
    public int Ultrasound { get; set; } = 45;
    public int Imaging { get; set; } = 60;
    public int Other { get; set; } = 60;
}
