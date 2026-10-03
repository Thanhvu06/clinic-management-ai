using ClinicManagement.Application.AI.Conversation;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.AI.Planning;
using ClinicManagement.Application.AI.Suggestions;
using ClinicManagement.Application.AI.Tools;
using ClinicManagement.Infrastructure.AI.Planning;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Infrastructure.AI;

public sealed record HybridIntentRoute(string Source, string Label, double Score, string Role);

/// <summary>Preserves existing rules/fallback; model can only select existing read/suggestion routes.</summary>
public sealed class HybridIntentRouter(AiDeterministicPlanner rules, IRoleIntentModel model, ILogger<HybridIntentRouter> logger) : IAiDeterministicPlanner
{
    public HybridIntentRoute? LastRoute { get; private set; }
    public void Reset() => LastRoute = null;
    public static IReadOnlyDictionary<string, string> SuggestionCodes { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["StartBooking"] = "patient.start_booking", ["MyAppointments"] = "patient.my_appointments", ["MyVisits"] = "patient.my_visits",
        ["MyDiagnosticResults"] = "patient.my_diagnostic_results", ["MyPrescriptions"] = "patient.my_prescriptions", ["MyBills"] = "patient.my_bills",
        ["TodayAppointments"] = "receptionist.today_appointments", ["ReceptionQueue"] = "receptionist.queue",
        ["DoctorQueue"] = "doctor.my_queue", ["PatientSummary"] = "doctor.patient_summary", ["DiagnosticOrders"] = "doctor.diagnostic_orders",
        ["PrescriptionStatus"] = "doctor.prescription_status", ["TechnicianWorklist"] = "technician.worklist",
        ["PrescriptionQueue"] = "pharmacist.prescription_queue", ["InventoryStatus"] = "pharmacist.inventory",
        ["PrescriptionPayment"] = "pharmacist.prescription_payment", ["DashboardMetrics"] = "admin.dashboard_metrics", ["AiHealth"] = "admin.ai_health"
    };

    public AiPlannerDecision Plan(AiCopilotPlanningContext context)
    {
        var original = rules.Plan(context);
        // Safety and deterministic clarification/resource/permission decisions always win.
        if (!original.RequiresProvider || context.Analysis.Safety.IsEmergency || context.Analysis.Safety.IsPromptInjection ||
            context.Analysis.Intent.IsClear && context.Analysis.Intent.Intent != AiChatIntentTypes.UnclearOrOutOfScope ||
            context.Analysis.Intent.Method.EndsWith("Guard", StringComparison.Ordinal))
            return Record("rule", RuleLabel(original, context.Analysis.Intent.Intent), (double)original.Confidence, context.Role, original);
        if (!model.IsAvailable) return Record("fallback", original.Intent, (double)original.Confidence, context.Role, original);
        var prediction = model.Predict(context.NormalizedMessage, context.Role);
        if (prediction is null) return Record("fallback", original.Intent, 0, context.Role, original);
        if (!model.IsLabelAllowed(prediction.Label, context.Role) || !double.IsFinite(prediction.Confidence) || prediction.Confidence > 1 || prediction.Confidence < model.Threshold)
            return Record("fallback", prediction.Label, prediction.Confidence, context.Role, original);
        var selected = PlanLabel(prediction.Label, context);
        if (selected is null || selected.RequiresProvider) return Record("fallback", prediction.Label, prediction.Confidence, context.Role, original);
        // All handlers originate in the existing deterministic planner; writes cannot enter through model labels.
        if (selected.ToolCalls.Any(x => x.Name.Contains("prepare", StringComparison.OrdinalIgnoreCase) || x.Name.Contains("execute", StringComparison.OrdinalIgnoreCase)))
            return Record("fallback", prediction.Label, prediction.Confidence, context.Role, original);
        return Record("model", prediction.Label, prediction.Confidence, context.Role, new AiPlannerDecision
        {
            PlannerMode = selected.PlannerMode, Intent = selected.Intent, SubIntent = selected.SubIntent, Confidence = (decimal)prediction.Confidence,
            Message = selected.Message, Clarification = selected.Clarification, NavigationRoute = selected.NavigationRoute,
            ErrorCode = selected.ErrorCode, ToolCalls = selected.ToolCalls
        });
    }

    private AiPlannerDecision? PlanLabel(string label, AiCopilotPlanningContext context)
    {
        if (SuggestionCodes.TryGetValue(label, out var code))
        {
            var suggestion = AiSuggestionCatalog.Find(code, context.Role);
            return suggestion is null ? null : rules.PlanSuggestion(suggestion, context.CurrentTurnResource);
        }
        var text = label switch
        {
            "Greeting" => "xin chào", "Help" => "hướng dẫn", "ClinicKnowledge" => "thông tin phòng khám " + context.NormalizedMessage,
            "LookupAppointment" when context.Role == AiActorRole.Receptionist => "tra cứu mã lịch hẹn " + context.NormalizedMessage,
            _ => null // ActionRequest/OutOfScope have no general dedicated handler: retain fallback.
        };
        if (text is null) return null;
        return rules.Plan(new AiCopilotPlanningContext
        {
            Role = context.Role, NormalizedMessage = text, Resource = context.Resource,
            CurrentTurnResource = context.CurrentTurnResource, Memory = context.Memory,
            Analysis = new AiConversationAnalysis { Intent = new() { IsClear = false }, NormalizedText = text }
        });
    }

    public AiPlannerDecision PlanSuggestion(AiSuggestionDefinition? suggestion, AiResolvedResourceContext resource) =>
        Record("rule", SuggestionCodes.FirstOrDefault(x => x.Value == suggestion?.Code).Key ?? "InvalidSuggestion", 1, suggestion?.Role, rules.PlanSuggestion(suggestion, resource));

    private static string RuleLabel(AiPlannerDecision decision, string classifiedIntent)
    {
        var tool = decision.ToolCalls.FirstOrDefault()?.Name;
        if (tool == "clinic.search_knowledge") return "ClinicKnowledge";
        if (tool == "reception.lookup_appointment") return "LookupAppointment";
        var code = AiSuggestionCatalog.Definitions.FirstOrDefault(x => x.ToolName == tool)?.Code;
        var label = SuggestionCodes.FirstOrDefault(x => x.Value == code).Key;
        if (label is not null) return label;
        return decision.SubIntent switch
        {
            "Greeting" => "Greeting", "RoleHelp" => "Help", "BookingWizard" => "StartBooking",
            "WriteRequiresExplicitActionConfirmation" or "MixedReadWritePlan" => "ActionRequest", _ => classifiedIntent
        };
    }

    private AiPlannerDecision Record(string source, string label, double score, AiActorRole? role, AiPlannerDecision decision)
    {
        LastRoute = new(source, label, score, role?.ToString() ?? "Unknown");
        logger.LogInformation("Role-intent decision Source={Source} Label={Label} Score={Score} Role={Role}", source, label, score, LastRoute.Role);
        return decision;
    }
}
