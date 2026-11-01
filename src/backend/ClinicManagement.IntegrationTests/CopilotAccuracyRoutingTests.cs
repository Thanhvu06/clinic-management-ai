using ClinicManagement.Application.AI;
using ClinicManagement.Application.AI.Conversation;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.AI.Planning;
using ClinicManagement.Application.AI.Tools;
using ClinicManagement.Infrastructure.AI.Planning;

namespace ClinicManagement.IntegrationTests;

public class CopilotAccuracyRoutingTests
{
    public static IEnumerable<object[]> ReadQuestions()
    {
        yield return [AiActorRole.DiagnosticTechnician, "phiếu nào đang chờ thực hiện", AiChatIntentTypes.DiagnosticLookup, "technician.get_worklist"];
        yield return [AiActorRole.DiagnosticTechnician, "xem các chỉ định chưa thực hiện", AiChatIntentTypes.DiagnosticLookup, "technician.get_worklist"];
        yield return [AiActorRole.DiagnosticTechnician, "cách lấy mẫu xét nghiệm glucose", AiChatIntentTypes.FacilityInquiry, "technician.get_service_guidance"];
        yield return [AiActorRole.Receptionist, "có yêu cầu đổi lịch nào đang chờ", AiChatIntentTypes.QueueLookup, "reception.get_pending_change_requests"];
        yield return [AiActorRole.Receptionist, "xem yêu cầu hủy lịch chưa xử lý", AiChatIntentTypes.QueueLookup, "reception.get_pending_change_requests"];
        yield return [AiActorRole.Receptionist, "hóa đơn nào chưa thanh toán", AiChatIntentTypes.QueueLookup, "reception.get_unpaid_invoices"];
        yield return [AiActorRole.Receptionist, "cho xem các hóa đơn còn nợ", AiChatIntentTypes.QueueLookup, "reception.get_unpaid_invoices"];
        yield return [AiActorRole.Pharmacist, "thuốc nào sắp hết hàng", AiChatIntentTypes.PharmacyInventory, "pharmacist.get_low_stock"];
        yield return [AiActorRole.Pharmacist, "paracetamol còn bao nhiêu viên", AiChatIntentTypes.PharmacyInventory, "pharmacist.get_medicine_stock"];
        yield return [AiActorRole.Pharmacist, "thuốc vitamin c còn bao nhiêu", AiChatIntentTypes.PharmacyInventory, "pharmacist.get_medicine_stock"];
        yield return [AiActorRole.Pharmacist, "lô thuốc nào sắp hết hạn", AiChatIntentTypes.PharmacyInventory, "pharmacist.get_medicine_batches"];
        yield return [AiActorRole.Pharmacist, "xem lô thuốc hết hạn", AiChatIntentTypes.PharmacyInventory, "pharmacist.get_medicine_batches"];
        yield return [AiActorRole.Doctor, "hôm nay tôi còn bao nhiêu bệnh nhân chờ khám", AiChatIntentTypes.QueueLookup, "doctor.get_my_queue"];
        yield return [AiActorRole.Doctor, "xem người bệnh đang chờ đến lượt", AiChatIntentTypes.QueueLookup, "doctor.get_my_queue"];
        yield return [AiActorRole.Admin, "doanh thu 7 ngày gần đây", AiChatIntentTypes.AdminMetrics, "admin.get_revenue_report"];
        yield return [AiActorRole.Admin, "doanh thu 30 ngày gần đây", AiChatIntentTypes.AdminMetrics, "admin.get_revenue_report"];
        yield return [AiActorRole.Admin, "có bao nhiêu bác sĩ đang hoạt động", AiChatIntentTypes.AdminMetrics, "admin.get_active_doctors"];
        yield return [AiActorRole.Admin, "đơn nghỉ phép nào đang chờ duyệt", AiChatIntentTypes.AdminMetrics, "admin.get_pending_leave_requests"];
    }

    [Theory]
    [MemberData(nameof(ReadQuestions))]
    public void ExactQuestionsAndParaphrases_SelectDeterministicReadTool(AiActorRole role, string message, string intent, string tool)
    {
        var decision = new AiDeterministicPlanner().Plan(new AiCopilotPlanningContext
        {
            Role = role, NormalizedMessage = message,
            Analysis = new AiConversationAnalysis { Intent = new() { Intent = AiChatIntentTypes.UnclearOrOutOfScope } }
        });
        Assert.False(decision.RequiresProvider);
        Assert.Equal(AiPlannerModes.Deterministic, decision.PlannerMode);
        Assert.Equal(intent, decision.Intent);
        Assert.Equal(tool, Assert.Single(decision.ToolCalls).Name);
    }

    [Theory]
    [InlineData(AiActorRole.Doctor, "lịch làm việc tuần này", "/doctor/schedule")]
    [InlineData(AiActorRole.Doctor, "xin nghỉ phép thứ 4", "/doctor/leave-requests")]
    [InlineData(AiActorRole.Admin, "xóa tài khoản bác sĩ Khải", "/admin/users")]
    [InlineData(AiActorRole.Admin, "khóa tài khoản nhân viên", "/admin/users")]
    [InlineData(AiActorRole.Admin, "sửa thông tin cơ sở", "/admin/facilities")]
    public void NavigationAndWriteRefusals_DoNotCreateToolCalls(AiActorRole role, string message, string route)
    {
        var decision = new AiDeterministicPlanner().Plan(new AiCopilotPlanningContext
        {
            Role = role, NormalizedMessage = message, Analysis = new AiConversationAnalysis { Intent = new() }
        });
        Assert.False(decision.RequiresProvider);
        Assert.Empty(decision.ToolCalls);
        Assert.Equal(route, decision.NavigationRoute);
        Assert.NotEqual(AiChatIntentTypes.FacilityInquiry, decision.Intent);
        if (role == AiActorRole.Admin)
        {
            Assert.Equal("WRITE_NOT_SUPPORTED", decision.ErrorCode);
            Assert.Contains("không thực hiện", decision.Message);
        }
    }
}
