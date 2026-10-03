using ClinicManagement.Application.AI;
using ClinicManagement.Application.AI.Conversation;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.AI.Planning;
using ClinicManagement.Application.AI.Suggestions;
using ClinicManagement.Application.AI.Tools;
using ClinicManagement.Infrastructure.AI.Planning;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ClinicManagement.Infrastructure.AI;

public sealed record HybridIntentRoute(string Source, string Label, double Score, string Role);

/// <summary>Preserves existing rules/fallback; model can only select existing read/suggestion routes.</summary>
public sealed class HybridIntentRouter(AiDeterministicPlanner rules, IRoleIntentModel model, ILogger<HybridIntentRouter> logger,
    IOptions<RoleIntentModelOptions>? options = null) : IAiDeterministicPlanner
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
        {
            var ruleLabel = RuleLabel(original, context.Analysis.Intent.Intent);
            if (CanOverride(context, original) && model.IsAvailable)
            {
                var candidate = model.Predict(context.NormalizedMessage, context.Role);
                if (ValidPrediction(candidate, context.Role, options!.Value.OverrideThreshold!.Value) && candidate!.Label != ruleLabel)
                {
                    var replacement = PlanLabel(candidate.Label, context);
                    if (SafeSelection(replacement)) return Selected("override", candidate, context.Role, replacement!);
                }
            }
            return Record("rule", ruleLabel, (double)original.Confidence, context.Role, original);
        }
        if (!model.IsAvailable) return Record("fallback", original.Intent, (double)original.Confidence, context.Role, original);
        var prediction = model.Predict(context.NormalizedMessage, context.Role);
        if (prediction is null) return Record("fallback", original.Intent, 0, context.Role, original);
        if (!ValidPrediction(prediction, context.Role, model.Threshold))
            return Record("fallback", prediction.Label, prediction.Confidence, context.Role, original);
        var selected = PlanLabel(prediction.Label, context);
        if (!SafeSelection(selected))
            return Record("fallback", prediction.Label, prediction.Confidence, context.Role, original);
        return Selected("model", prediction, context.Role, selected!);
    }

    private AiPlannerDecision Selected(string source, RoleIntentModelPrediction prediction, AiActorRole role, AiPlannerDecision selected) =>
        Record(source, prediction.Label, prediction.Confidence, role, new AiPlannerDecision
        {
            PlannerMode = selected.PlannerMode, Intent = selected.Intent, SubIntent = selected.SubIntent, Confidence = (decimal)prediction.Confidence,
            Message = selected.Message, Clarification = selected.Clarification, NavigationRoute = selected.NavigationRoute,
            ErrorCode = selected.ErrorCode, ToolCalls = selected.ToolCalls
        });

    private bool ValidPrediction(RoleIntentModelPrediction? prediction, AiActorRole role, double threshold) =>
        prediction is not null && model.IsLabelAllowed(prediction.Label, role) && double.IsFinite(prediction.Confidence) &&
        prediction.Confidence <= 1 && prediction.Confidence >= threshold;

    private static bool SafeSelection(AiPlannerDecision? selected) => selected is not null && !selected.RequiresProvider &&
        // Existing handlers only. The action gateway/confirmation can never be selected by model inference.
        !selected.ToolCalls.Any(x => x.Name.Contains("prepare", StringComparison.OrdinalIgnoreCase) || x.Name.Contains("execute", StringComparison.OrdinalIgnoreCase));

    private bool CanOverride(AiCopilotPlanningContext context, AiPlannerDecision original)
    {
        var settings = options?.Value;
        if (settings?.OverrideEnabled != true || settings.OverrideThreshold is not {} threshold ||
            !double.IsFinite(threshold) || threshold < 0 || threshold > 1) return false;
        var intent = context.Analysis.Intent;
        if (context.Analysis.Safety.IsEmergency || context.Analysis.Safety.IsPromptInjection ||
            intent.Method.EndsWith("Guard", StringComparison.Ordinal) || original.PlannerMode == AiPlannerModes.Safety ||
            intent.Intent is AiChatIntentTypes.EmergencyEscalation or AiChatIntentTypes.PromptInjection ||
            original.SubIntent is "WriteRequiresExplicitActionConfirmation" or "MixedReadWritePlan" or
                "MissingAssignedCase" or "MissingPrescriptionForPayment" or "MissingAppointmentCode" ||
            original.ErrorCode is not null) return false;
        if (IsLegacyBooking(intent.Intent) || IsLegacyBooking(original.Intent) || original.SubIntent == "BookingWizard" ||
            intent.IsCorrection || intent.CorrectionTarget is not null || intent.ExtractedRelativeDoctorIndex.HasValue ||
            intent.ExtractedRelativeSlotIndex.HasValue) return false;
        var memory = context.Memory;
        return memory is null || (string.IsNullOrEmpty(memory.LastIntent) && string.IsNullOrEmpty(memory.LastSubIntent) &&
            string.IsNullOrEmpty(memory.PendingClarification) && memory.MissingFields.Count == 0 &&
            memory.ConfirmedEntities.Count == 0 && memory.CurrentResource is null && string.IsNullOrEmpty(memory.SanitizedSummary));
    }

    private static bool IsLegacyBooking(string intent) => intent is AiChatIntentTypes.StartBooking or AiChatIntentTypes.SelectDoctor or
        AiChatIntentTypes.SelectSlot or AiChatIntentTypes.ProvideReason or AiChatIntentTypes.ReviewDraft or AiChatIntentTypes.ConfirmBooking or
        AiChatIntentTypes.ModifyDraft or AiChatIntentTypes.CancelDraft or AiChatIntentTypes.FindEarliestAvailableSlot or
        AiChatIntentTypes.SpecialtyRecommendation or AiChatIntentTypes.FindDoctorForSymptom or AiChatIntentTypes.DoctorSearch;

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
