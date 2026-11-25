using System.Text.RegularExpressions;
using ClinicManagement.Application.AI.Conversation;
using ClinicManagement.Application.AI.Tools;

namespace ClinicManagement.Infrastructure.AI;

/// <summary>
/// Keeps patient requests to prescribe, dose, start, stop or change medication
/// outside the chat assistant without blocking staff planner/action workflows or
/// ordinary read questions about an already issued prescription.
/// </summary>
public static class AiMedicalScopeGuard
{
    internal const string PatientRefusal = "Tôi không thể kê đơn hoặc hướng dẫn liều dùng qua cuộc trò chuyện. Vui lòng liên hệ bác sĩ, dược sĩ hoặc phòng khám để được hướng dẫn.";
    internal const string PatientPickingRefusal = "Tôi không thể gợi ý hay chọn thuốc cho bạn và không thể kê đơn qua cuộc trò chuyện. Vui lòng liên hệ bác sĩ hoặc dược sĩ để được hướng dẫn.";

    // Output is not a user question: numeric dosing and authored prescriptions
    // must be rejected even without request words such as "bao nhiêu".
    private static readonly Regex ProviderDoseOrPrescriptionAdvice = new(
        @"\b\d+(?:[.,]\d+)?\s*(?:mg|mcg|ml|viên|vien)\b|\b(?:toi|minh)\s+ke\s+(?:don|thuoc)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ProviderMedicationChangeAdvice = new(
        @"\b(?:hãy|nên)\s+(?:ngừng|ngưng|dừng|đổi|bỏ)\s+thuốc\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static bool IsUnsafeProviderAdvice(AiActorRole role, string? text) =>
        role == AiActorRole.Patient && !string.IsNullOrWhiteSpace(text) &&
        (IsPrescriptionRequest(role, text) ||
         ProviderDoseOrPrescriptionAdvice.IsMatch(AiTextNormalizer.NormalizeForComparison(text)) ||
         ProviderMedicationChangeAdvice.IsMatch(AiTextNormalizer.Normalize(text)));

    private static readonly Regex PreservedPrescriptionRead = new(
        @"\bđã\s+kê\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex FoldedPrescriptionRead = new(
        @"\b(?:bac\s+si\s+da\s+ke|thuoc\s+(?:bac\s+si\s+)?da\s+ke|(?:toa|don)\s+thuoc\s+cua\s+toi|thuoc\s+cua\s+toi)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex PrescriptionRequest = new(
        @"\bke\s+(?:thuoc|don)(?:\s+thuoc)?\s+(?:cho|giup)\b|\bcho\s+(?:toi|minh)\s+(?:toa|don)\b.{0,40}\b(?:tri|chua)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex MedicationChange = new(
        @"\b(?:ngừng|ngưng|đổi|bỏ|tăng|giảm|dừng)\s+(?:(?:liều|lượng)\s+)?thuốc\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex RequestContext = new(
        @"\b(?:có nên|được không|tôi muốn|cho tôi|giúp tôi|hãy|nên)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex UnaccentedChange = new(
        @"\b(?:(?:ngung|dung|doi|bo) thuoc|(?:tang|giam) lieu)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex UnaccentedDoseRequest = new(
        @"\blieu\s+(?:dung|luong)\b|\buong\b.{0,50}\bbao\s+nhieu\s+(?:vien|mg)\b|\bbao\s+nhieu\s+(?:vien|mg)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex AccentedDoseRequest = new(
        @"\bliều\b.{0,60}(?:bao\s+nhiêu|viên|mg|thuốc|paracetamol)|\buống\b.{0,60}\b(?:bao\s+nhiêu|mấy)\s+viên\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex PrescriptionAddition = new(
        @"\bke\s+them\b|\bke\s+(?:thuoc|don)\s+moi\b|\bthem\s+(?:thuoc|don\s+thuoc)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private const string QuantityQuestion = @"(?:bao\s+(?:nhiêu|nhieu)|mấy|may)";
    private const string ExplicitDoseAmount = @"(?:liều|lieu\s+(?:dung|luong)|gấp\s+đôi|gap\s+doi)";
    private const string DoseAmount = $@"\b(?:{QuantityQuestion}|{ExplicitDoseAmount})\b";
    private const string Water = @"(?:nước|nuoc)";
    private const string WaterVolumeUnit = @"(?:ml|lít|lit|cốc|coc|ly|chai)";
    private const string MedicationUseAmount =
        $@"\b(?:(?<!\b{Water}\s){QuantityQuestion}(?!\s+(?:{WaterVolumeUnit}\s+)?{Water}\b)|{ExplicitDoseAmount})\b";
    private const string MedicationUseVerb =
        @"\b(?:(?:uống|uong|dùng|dung|nhỏ|nho|bôi|boi|tiêm|tiem|ngậm|ngam|dán|dan|xịt|xit)\b|" +
        @"(?:đặt|dat)\b(?!\s+(?:lịch|lich|hẹn|hen)\b))";

    private static readonly Regex DosageSuggestion = new(
        $@"\b(?:nên|nen)\b.{{0,40}}{DoseAmount}|{DoseAmount}.{{0,40}}\b(?:nên|nen)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex MedicationUseWithDose = new(
        $@"{MedicationUseVerb}(?<between>.{{0,40}}?)(?<amount>{MedicationUseAmount})|" +
        $@"(?<amount>{MedicationUseAmount})(?<between>.{{0,40}}?){MedicationUseVerb}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex DoseAdjustment = new(
        @"\b(?:tăng|giảm)\s+liều\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex TimingQuestion = new(
        @"\b(?:(?:bao\s+nhieu|may)\s+(?:phut|gio|tieng)|(?:den|toi|co\s+mat)\s+som)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex MedicationDoseContext = new(
        @"\b(?:thuoc|vien|siro|giot|mieng\s+dan|khang\s+sinh|ha\s+sot|giam\s+dau|" +
        @"paracetamol|ibuprofen|amoxicillin|aspirin|oresol)\b|(?<![\p{L}_])(?:mg|mcg|ml(?!\s+nuoc\b))\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private const string DoseUnitAmount = $@"\b(?:\d+\s*|{QuantityQuestion}\s+)";
    private const string DoseUnitMedication = @"(?:thuốc|thuoc|oresol|tiêm|tiem)\b";
    private const string QualifiedDoseUnit = @"(?:ống|ong|liều|lieu)\b";
    private const string PacketUnit = @"(?:gói|goi)\b";
    private static readonly Regex MedicationDoseUnitContext = new(
        $@"{DoseUnitAmount}{QualifiedDoseUnit}|\b{QualifiedDoseUnit}\s+{DoseUnitMedication}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex MedicationPacketContext = new(
        $@"{DoseUnitAmount}{PacketUnit}|\b{PacketUnit}\s+{DoseUnitMedication}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private const string ServiceNoun = @"(?:dịch|dich)\s+(?:vụ|vu)";
    private const string ServicePackageName =
        $@"(?:gói|goi)\s+(?:khám|kham|{ServiceNoun}|(?:xét|xet)\s+(?:nghiệm|nghiem))";
    private static readonly Regex ServicePackage = new(
        $@"\b{ServicePackageName}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private const string AccentedServiceOrPaymentNoun =
        @"\b(?:gói\s+(?:khám|dịch\s+vụ|xét\s+nghiệm)|dịch\s+vụ|bảo\s+hiểm|cọc|tiền|giá|phí|lịch|hẹn|thẻ)\b";
    private const string UnaccentedServiceOrPaymentNoun =
        @"\b(?:goi\s+(?:kham|dich\s+vu|xet\s+nghiem)|dich\s+vu|bao\s+hiem|coc|tien|gia|phi|lich)\b";
    private static readonly (Regex Object, Regex AmountSuffix) AccentedServiceOrPayment = (
        new(AccentedServiceOrPaymentNoun, RegexOptions.Compiled | RegexOptions.CultureInvariant),
        new($@"^\s+{AccentedServiceOrPaymentNoun}", RegexOptions.Compiled | RegexOptions.CultureInvariant));
    private static readonly (Regex Object, Regex AmountSuffix) UnaccentedServiceOrPayment = (
        new(UnaccentedServiceOrPaymentNoun, RegexOptions.Compiled | RegexOptions.CultureInvariant),
        new($@"^\s+{UnaccentedServiceOrPaymentNoun}", RegexOptions.Compiled | RegexOptions.CultureInvariant));

    private static readonly Regex MedicationRecommendation = new(
        @"\b(?:(?:thi co (?:can|nen) (?:cho )?uong thuoc|co (?:can|nen) (?:cho )?uong khang sinh) (?:hay )?khong|" +
        @"co (?:nen )?uong (?:thuoc|khang sinh)(?: (?!sau\b|truoc\b|luc\b)\p{L}+){0,3} duoc (?:khong|ko|k)|" +
        @"(?:nen|can) (?:cho )?uong thuoc (?:gi|nao)|" +
        @"uong (?:thuoc|khang sinh) (?:may|bao nhieu) ngay\b.{0,35}\b(?:ngung|dung))\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex MedicinePicking = new(
        @"\b(?:chon|lua chon|goi y|de xuat|gioi thieu)\s+(?:(?:giup|cho|toi|minh|em|mot|vai|loai|thu|ten)\s+){0,6}(?:thuoc|duoc pham)\b|" +
        @"\b(?:uong (?:thuoc |duoc pham )?(?:gi|loai nao)|uong (?:thuoc|duoc pham) nao|mua gi (?:de )?uong)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    internal static bool IsMedicinePickingRequest(string? message) =>
        !string.IsNullOrWhiteSpace(message) && SplitClauses(message).Any(clause =>
            !PreservedPrescriptionRead.IsMatch(clause.Preserved) && !FoldedPrescriptionRead.IsMatch(clause.Folded) &&
            MedicinePicking.IsMatch(clause.Folded));

    public static bool IsPrescriptionRequest(string? message) =>
        IsPrescriptionRequest(AiActorRole.Patient, message);

    public static bool IsPrescriptionRequest(AiActorRole role, string? message)
    {
        if (role != AiActorRole.Patient || string.IsNullOrWhiteSpace(message))
            return false;

        var preserved = AiTextNormalizer.Normalize(message).ToLowerInvariant();
        var folded = AiTextNormalizer.NormalizeForComparison(preserved);

        // "liều" is intentionally checked with accents preserved so it cannot
        // swallow the ordinary conjunction/question word "liệu". In fully
        // unaccented input only explicit dose forms such as "lieu dung" or
        // "bao nhieu vien" are accepted.
        if ((MedicationChange.IsMatch(preserved) && RequestContext.IsMatch(preserved)) ||
            (preserved == folded && UnaccentedChange.IsMatch(preserved)) ||
            PrescriptionAddition.IsMatch(folded))
            return true;

        foreach (var clause in SplitClauses(message))
        {
            var clausePreserved = clause.Preserved;
            var clauseFolded = clause.Folded;
            var isReadClause = PreservedPrescriptionRead.IsMatch(clausePreserved) ||
                               FoldedPrescriptionRead.IsMatch(clauseFolded);
            var hasDoseContext = MedicationDoseContext.IsMatch(clauseFolded) ||
                                 MedicationDoseUnitContext.IsMatch(clausePreserved) ||
                                 (MedicationPacketContext.IsMatch(clausePreserved) && !ServicePackage.IsMatch(clausePreserved)) ||
                                 DoseAdjustment.IsMatch(clausePreserved);
            var asksMedicationAmount = hasDoseContext
                ? MedicationUseWithDose.IsMatch(clausePreserved)
                : HasMedicationAmountWithoutService(clausePreserved, clausePreserved == clauseFolded);
            if ((asksMedicationAmount && hasDoseContext && !isReadClause) ||
                (DosageSuggestion.IsMatch(clausePreserved) &&
                 (hasDoseContext || (asksMedicationAmount && !TimingQuestion.IsMatch(clauseFolded)))))
                return true;

            var isSoftRequest = AccentedDoseRequest.IsMatch(clausePreserved) ||
                                PrescriptionRequest.IsMatch(clauseFolded) ||
                                UnaccentedDoseRequest.IsMatch(clauseFolded) ||
                                MedicationRecommendation.IsMatch(clauseFolded);
            if (isSoftRequest && !isReadClause)
                return true;
        }
        return IsMedicinePickingRequest(message);
    }

    private static bool HasMedicationAmountWithoutService(string clause, bool isUnaccented)
    {
        var serviceOrPayment = isUnaccented ? UnaccentedServiceOrPayment : AccentedServiceOrPayment;
        // A rejected pair can overlap a later medication verb in the same clause.
        for (var match = MedicationUseWithDose.Match(clause); match.Success;
             match = MedicationUseWithDose.Match(clause, match.Index + 1))
        {
            var amount = match.Groups["amount"];
            if (!serviceOrPayment.Object.IsMatch(match.Groups["between"].ValueSpan) &&
                !serviceOrPayment.AmountSuffix.IsMatch(clause.AsSpan(amount.Index + amount.Length)))
                return true;
        }

        return false;
    }

    private static IEnumerable<(string Preserved, string Folded)> SplitClauses(string message)
    {
        foreach (var rawPart in Regex.Split(message, @"[.!?;\r\n]+"))
        {
            var preserved = AiTextNormalizer.Normalize(rawPart).ToLowerInvariant();
            if (preserved.Length == 0) continue;
            var folded = AiTextNormalizer.NormalizeForComparison(preserved);
            var start = 0;
            foreach (Match boundary in Regex.Matches(folded, @"\b(?:bay\s+gio|gio|nhung|sau\s+do)\b", RegexOptions.CultureInvariant))
            {
                if (boundary.Value == "gio" && Regex.IsMatch(folded[..boundary.Index], @"\b(?:bao\s+nhieu|may)\s*$", RegexOptions.CultureInvariant))
                    continue;

                if (boundary.Index > start)
                    yield return (preserved[start..boundary.Index].Trim(), folded[start..boundary.Index].Trim());
                start = boundary.Index + boundary.Length;
            }

            if (start < preserved.Length)
                yield return (preserved[start..].Trim(), folded[start..].Trim());
        }
    }
}
