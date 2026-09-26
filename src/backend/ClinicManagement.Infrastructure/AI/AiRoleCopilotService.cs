using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClinicManagement.Application.AI;
using ClinicManagement.Application.AI.Conversation;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.AI.Interfaces;
using ClinicManagement.Application.AI.Tools;
using Microsoft.AspNetCore.Http;

namespace ClinicManagement.Infrastructure.AI;

public sealed class AiRoleCopilotService : IAiRoleCopilotService
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IAiToolExecutor _executor;
    private readonly IAiConversationPipeline _pipeline;

    public AiRoleCopilotService(IHttpContextAccessor httpContextAccessor, IAiToolExecutor executor, IAiConversationPipeline pipeline)
    {
        _httpContextAccessor = httpContextAccessor;
        _executor = executor;
        _pipeline = pipeline;
    }

    public IReadOnlyList<AiToolDefinition> GetToolsForCurrentRole()
    {
        var role = ResolveRole();
        return AiRoleToolCatalog.Definitions.Where(x => x.AllowedRoles.Contains(role)).ToArray();
    }

    public async Task<AiCopilotResponseDto> ChatAsync(AiCopilotRequestDto request, CancellationToken cancellationToken = default)
    {
        var role = ResolveRole();
        var message = request.Message ?? string.Empty;
        var analysis = _pipeline.Analyze(message);
        var tools = GetToolsForCurrentRole();

        if (analysis.Safety.IsEmergency)
        {
            return new AiCopilotResponseDto
            {
                Role = role.ToString(), Intent = AiChatIntentTypes.EmergencyEscalation,
                ProviderStatus = AiProviderStatusContract.SafetyBlocked,
                Message = "Dấu hiệu có thể là tình huống cấp cứu. Hãy gọi 115 hoặc kích hoạt quy trình cấp cứu theo quy định của cơ sở.",
                SafetyNotice = "Safety guard đã chặn mọi tool call trước khi truy cập dữ liệu.",
                AvailableTools = tools
            };
        }

        if (analysis.Safety.IsPromptInjection)
        {
            return new AiCopilotResponseDto
            {
                Role = role.ToString(), Intent = AiChatIntentTypes.PromptInjection,
                ProviderStatus = AiProviderStatusContract.SafetyBlocked,
                Message = "Yêu cầu điều khiển tool hoặc truy cập vượt quyền đã bị từ chối.",
                SafetyNotice = "Quyền, user và phạm vi cơ sở luôn do backend quyết định.",
                AvailableTools = tools
            };
        }

        var (toolName, arguments, prompt, route) = ResolveTool(role, analysis.NormalizedText);
        if (toolName is null)
        {
            return new AiCopilotResponseDto
            {
                Role = role.ToString(), Intent = analysis.Intent.Intent,
                ProviderStatus = AiProviderStatusContract.NotCalled,
                Message = prompt ?? "Tôi có thể tra cứu dữ liệu trong workspace hiện tại theo quyền của bạn.",
                SuggestedPrompts = SuggestedPrompts(role), NavigationRoute = route,
                AvailableTools = tools
            };
        }

        var result = await _executor.ExecuteAsync(new AiToolInvocation
        {
            ToolName = toolName,
            ToolVersion = "1.0",
            ArgumentsJson = arguments
        }, cancellationToken);

        var card = new AiCopilotDataCardDto
        {
            Type = result.ResultType ?? "workspace_result",
            Title = result.Status == "completed" ? "Dữ liệu đã kiểm chứng" : "Không thể truy cập dữ liệu",
            Description = result.DisplayText ?? result.Error?.Message,
            Data = result.Data,
            Sources = result.DataSources
        };
        return new AiCopilotResponseDto
        {
            Role = role.ToString(), Intent = analysis.Intent.Intent,
            ProviderStatus = AiProviderStatusContract.NotCalled,
            Message = result.DisplayText ?? result.Error?.Message ?? "Đã xử lý yêu cầu trong phạm vi được phân quyền.",
            NavigationRoute = route,
            SuggestedPrompts = SuggestedPrompts(role),
            Cards = new[] { card },
            AvailableTools = tools
        };
    }

    private (string? Tool, string Arguments, string? Prompt, string? Route) ResolveTool(AiActorRole role, string message)
    {
        var lower = AiTextNormalizer.NormalizeForComparison(message);
        if (role == AiActorRole.Receptionist && (lower.Contains("tra cứu") || lower.Contains("ma lich") || lower.Contains("mã lịch")))
        {
            var appointmentCode = ExtractAppointmentCode(message);
            return string.IsNullOrWhiteSpace(appointmentCode)
                ? (null, "{}", "Vui lòng cung cấp mã lịch hẹn để tôi tra cứu đúng hồ sơ trong phạm vi cơ sở của bạn.", "/reception/appointments")
                : ("reception.lookup_appointment", JsonSerializer.Serialize(new { appointmentCode }), null, "/reception/appointments");
        }

        return role switch
        {
            AiActorRole.Receptionist when lower.Contains("hang doi") || lower.Contains("hàng đợi") || lower.Contains("cho tiep")
                => ("reception.get_queue", "{}", null, "/reception"),
            AiActorRole.Receptionist => ("reception.get_today_appointments", "{}", null, "/reception/appointments"),
            AiActorRole.Doctor when lower.Contains("tom tat") || lower.Contains("tóm tắt")
                => (null, "{}", "Hãy mở một ca khám được phân công để tôi tóm tắt đúng patient scope.", "/doctor/appointments"),
            AiActorRole.Doctor when lower.Contains("can lam sang") || lower.Contains("cận lâm sàng") || lower.Contains("chi dinh")
                => ("doctor.get_diagnostic_orders", "{}", null, "/doctor/appointments"),
            AiActorRole.Doctor => ("doctor.get_my_queue", "{}", null, "/doctor/queue"),
            AiActorRole.DiagnosticTechnician => ("technician.get_worklist", "{}", null, "/diagnostics"),
            AiActorRole.Pharmacist when lower.Contains("ton kho") || lower.Contains("tồn kho")
                => ("pharmacist.get_inventory_status", "{}", null, "/pharmacy/inventory"),
            AiActorRole.Pharmacist => ("pharmacist.get_prescription_queue", "{}", null, "/pharmacy/prescriptions"),
            AiActorRole.Admin when lower.Contains("ai") || lower.Contains("provider")
                => ("admin.get_ai_health", "{}", null, "/admin/audit-logs"),
            AiActorRole.Admin => ("admin.get_dashboard_metrics", "{}", null, "/admin"),
            _ => (null, "{}", null, null)
        };
    }

    private AiActorRole ResolveRole()
    {
        var claims = _httpContextAccessor.HttpContext?.User?.FindAll(ClaimTypes.Role).Select(x => x.Value) ?? Enumerable.Empty<string>();
        foreach (var claim in claims)
        {
            if (Enum.TryParse<AiActorRole>(claim, true, out var role)) return role;
            if (claim.Equals("Diagnostic Technician", StringComparison.OrdinalIgnoreCase)) return AiActorRole.DiagnosticTechnician;
        }
        return AiActorRole.Patient;
    }

    private static string ExtractAppointmentCode(string message) =>
        Regex.Match(message ?? string.Empty, @"\b[A-Za-z]{2,}-?\d{3,}\b").Value;

    private static IReadOnlyList<string> SuggestedPrompts(AiActorRole role) => role switch
    {
        AiActorRole.Receptionist => new[] { "Xem lịch hẹn hôm nay", "Xem hàng đợi tiếp nhận", "Tra cứu mã lịch hẹn" },
        AiActorRole.Doctor => new[] { "Xem hàng đợi của tôi", "Xem chỉ định cận lâm sàng" },
        AiActorRole.DiagnosticTechnician => new[] { "Xem danh sách chỉ định đang chờ" },
        AiActorRole.Pharmacist => new[] { "Xem đơn thuốc chờ cấp", "Xem tồn kho" },
        AiActorRole.Admin => new[] { "Xem thống kê hôm nay", "Xem tình trạng AI" },
        _ => Array.Empty<string>()
    };
}
