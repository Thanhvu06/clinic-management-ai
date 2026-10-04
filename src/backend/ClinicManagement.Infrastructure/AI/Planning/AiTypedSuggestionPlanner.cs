using System.Text.Json;
using ClinicManagement.Application.AI;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.AI.Planning;
using ClinicManagement.Application.AI.Suggestions;
using ClinicManagement.Application.AI.Tools;

namespace ClinicManagement.Infrastructure.AI.Planning;

/// <summary>
/// Plans the server-owned typed suggestion codes. Each code maps to exactly one
/// read tool; arguments come only from the catalog definition. Authorization,
/// facility scope and argument validation stay in the normal binding/executor
/// pipeline, exactly as for chip clicks.
/// </summary>
public static class AiTypedSuggestionPlanner
{
    private static readonly IReadOnlyDictionary<string, (string Intent, string SubIntent, string Route)> Routes =
        new Dictionary<string, (string Intent, string SubIntent, string Route)>(StringComparer.OrdinalIgnoreCase)
        {
            ["reception.get_upcoming_appointments"] = (AiChatIntentTypes.ViewAppointments, "UpcomingAppointments", "/reception/appointments"),
            ["reception.get_pending_payments"] = (AiChatIntentTypes.QueueLookup, "PendingPayments", "/reception/billing"),
            ["doctor.get_my_appointments_today"] = (AiChatIntentTypes.ViewAppointments, "DoctorAppointmentsToday", "/doctor/appointments"),
            ["technician.get_completed_today"] = (AiChatIntentTypes.DiagnosticLookup, "TechnicianCompletedToday", "/diagnostics"),
            ["pharmacist.get_low_stock"] = (AiChatIntentTypes.PharmacyInventory, "LowStock", "/pharmacy/inventory"),
            ["admin.get_revenue_summary"] = (AiChatIntentTypes.AdminMetrics, "RevenueSummary", "/admin")
        };

    public static AiPlannerDecision Plan(AiSuggestionDefinition suggestion)
    {
        if (!AiSuggestionCatalog.IsTyped(suggestion) || !Routes.TryGetValue(suggestion.ToolName, out var route))
            return new AiPlannerDecision
            {
                PlannerMode = AiPlannerModes.Deterministic,
                Intent = AiChatIntentTypes.ClarificationRequired,
                SubIntent = AiPlannerErrorCodes.ToolNotAllowed,
                ErrorCode = AiPlannerErrorCodes.ToolNotAllowed,
                Confidence = 1m,
                Clarification = "Gợi ý này không có sẵn cho tài khoản của bạn. Hãy chọn một gợi ý khác hoặc mô tả rõ dữ liệu cần tra cứu.",
                Message = "Gợi ý này không có sẵn cho tài khoản của bạn. Hãy chọn một gợi ý khác hoặc mô tả rõ dữ liệu cần tra cứu."
            };

        var arguments = suggestion.FixedArguments ?? new Dictionary<string, string>();
        return new AiPlannerDecision
        {
            PlannerMode = AiPlannerModes.Deterministic,
            Intent = route.Intent,
            SubIntent = route.SubIntent,
            Confidence = .99m,
            NavigationRoute = route.Route,
            ToolCalls = new[]
            {
                new AiPlannerToolCall { Name = suggestion.ToolName, Version = "1.0", Arguments = JsonSerializer.SerializeToElement(arguments) }
            }
        };
    }
}
