using System.Text.RegularExpressions;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.AI.Interfaces;

namespace ClinicManagement.Infrastructure.AI.Knowledge;

public static class KnowledgeQuestionPolicy
{
    public static bool IsEarlyGuidance(string message, IntentClassificationResult classification)
    {
        var text = KnowledgeText.Normalize(message);
        return (Regex.IsMatch(text, @"\b(?:huong dan|cach|nhu the nao|the nao|quy trinh|thu tuc|phai lam gi)\b") || IsInterfaceNavigation(text)) &&
            !Regex.IsMatch(text, @"\b(?:cua (?:chinh )?(?:toi|em|con|minh|anh|chi|ban)|ma|so)\b") && !HasSelection(message, classification);
    }

    public static bool IsCandidate(string message, string intent, bool explicitIntent,
        IntentClassificationResult? classification = null)
    {
        if (explicitIntent || intent == AiChatIntentTypes.PricingInquiry)
            return false;
        var text = KnowledgeText.Normalize(message);
        if (Regex.IsMatch(text, @"\b(?:gia kham|gia bao nhieu|chi phi|phi kham|gio lam|mo cua|dia chi|so dien thoai)\b")) return false;
        if (HasSymptoms(message)) return false;
        var question = message.Contains('?') || IsInterfaceNavigation(text) || Regex.IsMatch(text,
            @"\b(?:chuan bi|co can|can mang|giay to|quy trinh|thu tuc|huong dan|cach|the nao|bao lau|khi nao|duoc khong|khong a|la gi|tai sao|khong|ko|k|chua|sao|ra sao|o dau|ai|gi|nao|bao gio|a|ha|phai khong|duoc ko|duoc k|co phai)\b");
        if (!question) return false;
        if (intent == AiChatIntentTypes.FacilityInquiry)
            return Regex.IsMatch(text, @"\bo dau\b") && !HasSelection(message, classification);
        if (intent == AiChatIntentTypes.UnclearOrOutOfScope) return true;
        var instructions = IsInterfaceNavigation(text) || Regex.IsMatch(text, @"\b(?:nhu the nao|the nao|nhu nao|cach|quy trinh|thu tuc|huong dan|phai lam gi|lam sao)\b");
        if (!instructions && Regex.IsMatch(text, @"^(?:cho toi|giup toi|toi muon|toi can)\b.*\b(?:dat|huy|doi|xem)\b"))
            return false;
        var processQuestion = Regex.IsMatch(text,
            @"\b(?:chuan bi|can mang|mang theo|mang.*cu|co nen mang|co can.*(?:mang|quay|tiep nhan)|co tu|phai cho|dang cho|cho.*xu ly|het han|vang mat|khoa.*online|gio hen thi sao|thi.*kiem tra giay to)\b");
        var guidance = instructions || processQuestion;
        if (intent == AiChatIntentTypes.ConfirmBooking && !instructions && !Regex.IsMatch(text, @"\b(?:xac nhan|hoan tat)\b"))
            return false;
        var action = intent is AiChatIntentTypes.StartBooking or AiChatIntentTypes.DoctorSearch or
            AiChatIntentTypes.SelectDoctor or AiChatIntentTypes.SelectSlot or AiChatIntentTypes.ProvideReason or
            AiChatIntentTypes.ReviewDraft or AiChatIntentTypes.ConfirmBooking or AiChatIntentTypes.ModifyDraft or
            AiChatIntentTypes.CancelDraft or AiChatIntentTypes.ViewAppointments or AiChatIntentTypes.FindEarliestAvailableSlot or
            AiChatIntentTypes.DiagnosticLookup or AiChatIntentTypes.PrescriptionLookup or AiChatIntentTypes.PatientSummary or
            AiChatIntentTypes.QueueLookup;
        if (action) return guidance && !HasSelection(message, classification);
        return classification?.IsClear == false;
    }

    private static bool IsInterfaceNavigation(string text) => Regex.IsMatch(text,
        @"\b(?:o muc nao|muc nao|o dau tren web|tren web|tren website|menu|trang nao|bam vao dau|vao dau de)\b");

    private static bool HasSelection(string message, IntentClassificationResult? classification) =>
        SymptomSpecialtyRules.HasNamedSpecialty(message) || !string.IsNullOrWhiteSpace(classification?.ExtractedDoctorName) ||
        !string.IsNullOrWhiteSpace(classification?.ExtractedDate) || !string.IsNullOrWhiteSpace(classification?.ExtractedTimePreference) ||
        classification?.ExtractedRelativeDoctorIndex is not null || classification?.ExtractedRelativeSlotIndex is not null;

    private static bool HasSymptoms(string message)
    {
        var accented = KnowledgeText.Normalize(message, false);
        if (Regex.IsMatch(accented, @"\b(?:đau|sốt|ngứa|nôn|chóng mặt|tê tay|mệt mỏi|khó thở)\b")) return true;
        return Regex.IsMatch(KnowledgeText.Normalize(message),
            @"\b(?:bi ho|ho khan|ho nhieu|ho keo dai|ho \d+ ngay|dau dau|dau bung|dau nguc|bi dau|bi sot|buon non|bi non|chong mat|te tay|met moi|kho tho|o chua|day bung|hoi hop|danh trong nguc|tim dap nhanh|u tai|nghe kem|mo mat|khat nhieu|tieu nhieu|tre kinh|cham kinh|roi loan kinh|khi hu|ra huyet|noi man|man ngua|noi ban|phat ban|viem da|rung toc|nam da|me day|nghet mui|viem xoang|viem amidan|chay mau cam|sung khop|cung khop|te bi|run tay|mat ngu|do mat|nhin mo|kho mat|o nong|tieu chay|tao bon|trao nguoc|cam cum)\b");
    }
}
