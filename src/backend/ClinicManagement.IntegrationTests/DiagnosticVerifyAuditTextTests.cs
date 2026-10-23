using ClinicManagement.Application.Common.Constants;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Infrastructure.Diagnostics;

namespace ClinicManagement.IntegrationTests;

public class DiagnosticVerifyAuditTextTests
{
    private static readonly DiagnosticOrder Order = new() { OrderCode = "DX-20261023-0001" };

    [Fact]
    public void VerifiedAudit_StartsWithReviewSentence_AndKeepsNote()
    {
        var description = DiagnosticAuditDescriptions.Verification(Order, "DiagnosticResultsVerified", RoleNames.Doctor, "  Đã đối chiếu  ");

        Assert.Equal("Duyệt kết quả phiếu #DX-20261023-0001 (Bác sĩ). Đã đối chiếu", description);
        Assert.DoesNotContain("Bác sĩ: Phiếu", description);
    }

    [Fact]
    public void VerifiedAudit_WithoutNote_EndsAfterRole()
    {
        Assert.Equal("Duyệt kết quả phiếu #DX-20261023-0001 (Kỹ thuật viên).",
            DiagnosticAuditDescriptions.Verification(Order, "DiagnosticResultsVerified", RoleNames.DiagnosticTechnician, null));
    }

    [Fact]
    public void ReturnedAudit_KeepsExistingFormat()
    {
        Assert.Equal("Trưởng phòng xét nghiệm: Phiếu #DX-20261023-0001. Thiếu mẫu",
            DiagnosticAuditDescriptions.Verification(Order, "DiagnosticResultsReturned", RoleNames.LabSupervisor, "Thiếu mẫu"));
    }

    [Theory]
    [InlineData(RoleNames.Doctor)]
    [InlineData(RoleNames.LabSupervisor)]
    [InlineData(RoleNames.DiagnosticTechnician)]
    public void VerificationRole_ReadsNewSentence(string role)
    {
        var description = DiagnosticAuditDescriptions.Verification(Order, "DiagnosticResultsVerified", role, "Ghi chú (có ngoặc).");

        Assert.Equal(role, DiagnosticAuditDescriptions.VerificationRole(description));
    }

    [Theory]
    [InlineData("Bác sĩ: Phiếu #DX-1. Đã kiểm tra", RoleNames.Doctor)]
    [InlineData("VerifiedByRole=LabSupervisor; cũ", RoleNames.LabSupervisor)]
    [InlineData("Không rõ", null)]
    public void VerificationRole_StillReadsHistoricalRecords(string description, string? expected)
    {
        Assert.Equal(expected, DiagnosticAuditDescriptions.VerificationRole(description));
    }
}
