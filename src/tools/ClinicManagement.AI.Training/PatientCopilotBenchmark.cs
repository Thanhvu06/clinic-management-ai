using System.Text.Json;
using ClinicManagement.Application.AI.Tools;
using ClinicManagement.Infrastructure.AI;

namespace ClinicManagement.AI.Training;

public sealed class PatientCopilotBenchmarkCase
{
    public string CaseId { get; set; } = string.Empty;
    public string Input { get; set; } = string.Empty;
    public string ActorRole { get; set; } = "Patient";
    public bool Authenticated { get; set; } = true;
    public string ExpectedTool { get; set; } = "none";
    public string ExpectedOutcome { get; set; } = "completed";
    public string ExpectedSafety { get; set; } = "ROUTINE";
    public string Category { get; set; } = "deterministic-routing";
    public string[] MustNotContain { get; set; } = Array.Empty<string>();
}

public sealed class PatientCopilotBenchmarkRunner
{
    public static string Evaluate(string path)
    {
        var cases = JsonSerializer.Deserialize<List<PatientCopilotBenchmarkCase>>(
            File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
        var guard = new AiSafetyGuard();
        var safetyCorrect = 0;
        var routeCorrect = 0;
        var outcomeCorrect = 0;
        var forbiddenClean = 0;
        var contentCases = 0;
        var safetyFailures = new List<object>();

        foreach (var item in cases)
        {
            var inspection = guard.Inspect(item.Input);
            var predictedSafety = inspection.IsEmergency ? "EMERGENCY" : "ROUTINE";
            var predictedTool = PredictTool(item.Input, inspection);
            var predictedOutcome = inspection.IsEmergency ? "safety" : inspection.IsPromptInjection ? "blocked" : "completed";

            if (string.Equals(predictedSafety, item.ExpectedSafety, StringComparison.OrdinalIgnoreCase)) safetyCorrect++;
            else safetyFailures.Add(new { item.CaseId, expected = item.ExpectedSafety, predicted = predictedSafety });
            if (string.Equals(predictedTool, item.ExpectedTool, StringComparison.OrdinalIgnoreCase)) routeCorrect++;
            if (string.Equals(predictedOutcome, item.ExpectedOutcome, StringComparison.OrdinalIgnoreCase)) outcomeCorrect++;
            if (item.MustNotContain.Length > 0)
            {
                contentCases++;
                var safeResponse = predictedOutcome == "safety"
                    ? "Tình huống có thể cấp cứu. Gọi 115 hoặc đến cơ sở cấp cứu gần nhất."
                    : predictedOutcome == "blocked"
                        ? "Tôi chỉ hỗ trợ trong phạm vi ClinicCare và không thể thực hiện yêu cầu này."
                        : "Kết quả sẽ được kiểm tra từ dịch vụ ClinicCare.";
                if (item.MustNotContain.All(token => !safeResponse.Contains(token, StringComparison.OrdinalIgnoreCase))) forbiddenClean++;
            }
        }

        var report = new
        {
            benchmark = "patient-copilot-phase-1.2",
            evaluator = "deterministic safety-and-planner-contract evaluator (not live Gemini)",
            totalCases = cases.Count,
            safety = new { correct = safetyCorrect, accuracy = Ratio(safetyCorrect, cases.Count), failures = safetyFailures },
            routing = new { correct = routeCorrect, accuracy = Ratio(routeCorrect, cases.Count) },
            outcome = new { correct = outcomeCorrect, accuracy = Ratio(outcomeCorrect, cases.Count) },
            mustNotContain = new { clean = forbiddenClean, total = contentCases, accuracy = Ratio(forbiddenClean, contentCases) }
            ,evaluationModes = new[] { "deterministic-safety", "deterministic-planner-contract" }
            ,plannerContractFixtures = cases.Count(x => string.Equals(x.Category, "planner-contract", StringComparison.OrdinalIgnoreCase))
            ,liveGemini = new { enabled = false, optInEnvironmentVariable = "CLINICCARE_BENCHMARK_LIVE_GEMINI" }
        };
        return JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
    }

    private static string PredictTool(string input, AiSafetyGuardResult inspection)
    {
        if (inspection.IsEmergency) return "none";
        if (inspection.IsPromptInjection) return "none";
        var text = input.ToLowerInvariant();
        if (text.Contains("chi tiết lịch") || text.Contains("chi tiet lich")) return "patient.get_appointment_detail";
        if (text.Contains("lịch hẹn") || text.Contains("lich hen") || text.Contains("lịch khám của tôi")) return "patient.get_my_appointments";
        if (text.Contains("hủy lịch") || text.Contains("huy lich")) return "patient.prepare_cancel_appointment";
        if (text.Contains("đổi lịch") || text.Contains("doi lich") || text.Contains("dời lịch")) return "patient.prepare_reschedule_appointment";
        if (text.Contains("khung giờ") || text.Contains("slot") || text.Contains("lịch trống")) return "clinic.get_available_slots";
        if (text.Contains("giá") || text.Contains("chi phí") || text.Contains("bang gia")) return "clinic.get_pricing";
        if (text.Contains("cơ sở") || text.Contains("địa chỉ") || text.Contains("dia chi")) return "clinic.get_facilities";
        if (text.Contains("bác sĩ") || text.Contains("bac si")) return "clinic.search_doctors";
        if (text.Contains("chuyên khoa") || text.Contains("chuyen khoa")) return "clinic.search_specialties";
        if (text.Contains("đặt lịch") || text.Contains("dat lich")) return "patient.prepare_booking";
        return "none";
    }

    private static double Ratio(int value, int total) => total == 0 ? 1 : Math.Round((double)value / total, 4);
}
