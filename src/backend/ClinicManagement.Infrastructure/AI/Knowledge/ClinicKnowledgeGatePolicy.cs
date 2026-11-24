using System.Text.RegularExpressions;

namespace ClinicManagement.Infrastructure.AI.Knowledge;

public enum ClinicInformationTopic { None, OpeningHours, ConsultationFees }

public static class ClinicKnowledgeGatePolicy
{
    private static readonly Regex FarOutsideClinic = new(
        @"\b(?:nau an|nau mon|lam mon|ap chao|nuong|cong thuc nau|che bien|giai (?:toan|bai|phuong trinh)|bai toan|phuong trinh|tinh nghiem|lap trinh|python|javascript|bai hat|sinh nhat|bong da|co phieu|chung khoan|dau tu)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool IsOutsideClinic(string message) => FarOutsideClinic.IsMatch(KnowledgeText.Normalize(message));

    public static bool IsInformationQuestion(string message) => Regex.IsMatch(KnowledgeText.Normalize(message),
        @"\b(?:huong dan|quy trinh|giay to|lan dau|ho so cu|mat khau|ket qua|thanh toan|chuan bi|gom (?:nhung |cac )?buoc)\b");

    public static bool IsProcedureQuestion(string message) => Regex.IsMatch(KnowledgeText.Normalize(message),
        @"\b(?:huong dan|quy trinh|cach|gom (?:nhung |cac )?buoc)\b");

    public static bool IsInformationInquiry(string message) => message.Contains('?') || IsInformationQuestion(message) ||
        Regex.IsMatch(KnowledgeText.Normalize(message), @"\b(?:cho xem|khong|chua|nao|gi|bao lau|bao nhieu|sao|the nao)\b");

    public static bool CanUseSemanticFallback(string message) =>
        IsInformationInquiry(message) && !IsOutsideClinic(message) && !KnowledgeQuestionPolicy.HasSymptoms(message) &&
        !KnowledgeQuestionPolicy.IsTreatmentDietQuestion(message) &&
        !new AiSafetyGuard().Inspect(message).IsEmergency &&
        !new AiSafetyGuard().ContainsPromptInjection(message) && !AiMedicalScopeGuard.IsPrescriptionRequest(message);

    public static ClinicInformationTopic InformationTopic(string message)
    {
        var text = KnowledgeText.Normalize(message);
        if (!Regex.IsMatch(text, @"\b(?:cliniccare|phong kham|co so|kham|chuyen khoa)\b")) return ClinicInformationTopic.None;
        if (Regex.IsMatch(text, @"\b(?:bang gia|phi kham|gia kham|chi phi kham)\b") && !Regex.IsMatch(text, @"\b(?:thuoc|goi)\b"))
            return ClinicInformationTopic.ConsultationFees;
        if (Regex.IsMatch(text, @"\b(?:lich lam viec|gio lam viec|gio mo cua|mo cua)\b") ||
            Regex.IsMatch(text, @"\b(?:toi|dem|thu hai|thu ba|thu tu|thu nam|thu sau|thu bay|chu nhat|\d+ gio)\b") &&
            Regex.IsMatch(text, @"\b(?:mo|tiep nhan|tiep don|don tiep)\b"))
            return ClinicInformationTopic.OpeningHours;
        return ClinicInformationTopic.None;
    }
}
