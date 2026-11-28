using System.Text;
using System.Text.RegularExpressions;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.AI.Interfaces;
using ClinicManagement.Infrastructure.Common;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.AI.Knowledge;

public static class KnowledgeQuestionPolicy
{
    private const string BookingAction = @"dat(?: (?:lich(?: hen| kham)?|hen|kham))?";
    private static readonly Regex BookingLeadTime = new(
        @"\b(?:(?:can|phai|bat buoc) " + BookingAction + @" truoc (?:khong|ko|k)|" +
        BookingAction + @" (?:duoc )?(?:truoc|xa nhat|som nhat|toi da)(?: (?:duoc|toi da|it nhat))? " +
        @"(?:bao lau|(?:bao nhieu|may) (?:ngay|gio|tieng|tuan|thang)))\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex PublicClinicTopic = new(
        @"\b(?:chuan bi (?:thong tin |ve )?di ung|xet nghiem xong\b.*\bquay lai gap bac si|" +
        @"ket qua xet nghiem\b.*\bbac si (?:da )?duyet|ket luan bac si\b.*\bnghia la gi|" +
        @"cho duyet (?:doi lich|huy)\b.*\bnghia la gi|nguoi giam ho\b.*\btrao doi\b.*\bvoi bac si|" +
        @"(?:nhap|gui|chia se) so dien thoai (?:vao|trong) (?:chat|cuoc tro chuyen)|" +
        @"rag\b.*\blich su chat|dang nhap bang (?:email|so dien thoai))\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool IsEarlyGuidance(string message, IntentClassificationResult classification)
    {
        var text = KnowledgeText.Normalize(message);
        return IsBookingLeadTimeQuestion(text) && !HasSymptoms(message) ||
            IsPublicClinicQuestion(message, text, classification) ||
            (Regex.IsMatch(text, @"\b(?:huong dan|cach|nhu the nao|the nao|quy trinh|thu tuc|phai lam gi)\b") || IsInterfaceNavigation(text)) &&
            !Regex.IsMatch(text, @"\b(?:cua (?:chinh )?(?:toi|em|con|minh|anh|chi|ban)|ma|so)\b") && !HasSelection(message, classification);
    }

    public static bool IsCandidate(string message, string intent, bool explicitIntent,
        IntentClassificationResult? classification = null)
    {
        if (explicitIntent || intent == AiChatIntentTypes.PricingInquiry)
            return false;
        var text = KnowledgeText.Normalize(message);
        if (HasSymptoms(message) || IsTreatmentDietQuestion(message)) return false;
        if (intent != AiChatIntentTypes.ConfirmBooking &&
            (IsBookingLeadTimeQuestion(text) || IsPublicClinicQuestion(message, text, classification))) return true;
        if (Regex.IsMatch(text, @"\b(?:gia kham|gia bao nhieu|chi phi|phi kham|gio lam|mo cua|dia chi|so dien thoai)\b")) return false;
        var question = message.Contains('?') || IsInterfaceNavigation(text) || Regex.IsMatch(text,
            @"\b(?:chuan bi|co can|can mang|giay to|quy trinh|thu tuc|huong dan|cach|the nao|bao lau|may gio|may tieng|may ngay|may phut|may lan|may lit|bao nhieu|khi nao|duoc khong|khong a|la gi|tai sao|khong|ko|k|chua|sao|ra sao|o dau|ai|gi|nao|bao gio|a|ha|phai khong|duoc ko|duoc k|co phai)\b");
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

    internal static bool IsBookingLeadTimeQuestion(string normalized) => BookingLeadTime.IsMatch(normalized);

    private static readonly Regex MissedAppointmentWording = new(@"(?<![\p{L}])(?:lỡ|bỏ|trễ) hẹn(?![\p{L}])|(?<![\p{L}])không đến khám(?![\p{L}])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    // Patients say "lỡ hẹn" where the policy says "vắng mặt"; without the policy word the
    // online-lock question misses the lexical coverage the retriever requires.
    internal static string WithPolicyWording(string message) =>
        Regex.IsMatch(KnowledgeText.Normalize(message, false), @"(?<![\p{L}])(?:khóa|khoá)(?![\p{L}])") ? MissedAppointmentWording.Replace(message, "vắng mặt") : message;

    internal static bool IsVisitPreparationQuestion(string text) => Regex.IsMatch(text,
        @"^(?:(?:can|nen) )?chuan bi(?: gi)? truoc khi (?:di )?kham(?: (?:can gi|nen lam gi|the nao|nhu the nao))?$");

    internal static bool IsTreatmentDietQuestion(string message) => Regex.IsMatch(KnowledgeText.Normalize(message),
        @"\b(?:mo mau|duong huyet|huyet ap) (?:cao|thap)\b.*\b(?:an uong|kieng an|che do an)\b");

    private static bool IsPublicClinicQuestion(string message, string text, IntentClassificationResult? classification)
    {
        if (HasSelection(message, classification, ignoreDoctorProse: true) ||
            Regex.IsMatch(text, @"\b(?:cua (?:chinh )?(?:toi|em|con|minh|anh|chi|ban)|ma (?:lich hen|ho so|benh nhan))\b|^(?:cho toi|giup toi|toi muon) (?:xem|tra cuu|huy|doi|dat)\b"))
            return false;

        return PublicClinicTopic.IsMatch(text);
    }

    private static bool HasSelection(string message, IntentClassificationResult? classification, bool ignoreDoctorProse = false) =>
        SymptomSpecialtyRules.HasNamedSpecialty(message) || HasDoctorSelection(classification?.ExtractedDoctorName, ignoreDoctorProse) ||
        !string.IsNullOrWhiteSpace(classification?.ExtractedDate) || !string.IsNullOrWhiteSpace(classification?.ExtractedTimePreference) ||
        classification?.ExtractedRelativeDoctorIndex is not null || classification?.ExtractedRelativeSlotIndex is not null;

    private static bool HasDoctorSelection(string? name, bool ignoreDoctorProse) =>
        !string.IsNullOrWhiteSpace(name) && (!ignoreDoctorProse || !Regex.IsMatch(KnowledgeText.Normalize(name),
            @"^(?:duoc (?:khong|ko|k)|khong|duyet(?: a)?|tren ket qua(?: nghia la gi)?)$"));

    internal static bool HasSymptoms(string message)
    {
        var accented = KnowledgeText.Normalize(message, false);
        if (Regex.IsMatch(accented, @"\b(?:đau|sốt|ngứa|nôn|chóng mặt|tê tay|mệt mỏi|khó thở)\b")) return true;
        return Regex.IsMatch(KnowledgeText.Normalize(message), @"\bngo doc thuc pham nhe\b") || Regex.IsMatch(KnowledgeText.Normalize(message),
            @"\b(?:bi ho|ho khan|ho nhieu|ho keo dai|ho \d+ ngay|dau dau|dau bung|dau nguc|bi dau|bi sot|buon non|bi non|chong mat|te tay|met moi|kho tho|o chua|day bung|hoi hop|danh trong nguc|tim dap nhanh|u tai|nghe kem|mo mat|khat nhieu|tieu nhieu|tre kinh|cham kinh|roi loan kinh|khi hu|ra huyet|noi man|man ngua|noi ban|phat ban|viem da|rung toc|nam da|me day|nghet mui|viem xoang|viem amidan|chay mau cam|sung khop|cung khop|te bi|run tay|mat ngu|do mat|nhin mo|kho mat|o nong|tieu chay|tao bon|trao nguoc|cam cum)\b");
    }
}

internal static class PatientChatPricing
{
    private const string PackageSelectionGuidance = "Bạn có thể liên hệ lễ tân hoặc bác sĩ để được tư vấn chọn gói phù hợp.";

    internal static bool MentionsHealthPackage(string normalized) =>
        Regex.IsMatch(normalized, @"\bgoi (?:kham|suc khoe|xet nghiem)\b", RegexOptions.CultureInvariant);

    internal static bool HasReportedSymptoms(string message, string normalized) =>
        KnowledgeQuestionPolicy.HasSymptoms(message) ||
        Regex.IsMatch(normalized, @"\b(?:bi|dang) (?:ho|met|sot|dau|ngua|non|nhuc|rat|te|phu)\b",
            RegexOptions.CultureInvariant);

    // Chat shorthand such as "bn tiền", "bao nhiu", "giá sao", "hết nhiêu"; the
    // clinic word keeps dose or duration questions ("uống bao nhiu viên") out.
    private static readonly Regex ColloquialPriceCue = new(
        @"\b(?:(?:mat|het|ton|tra|tinh) (?:bn|bao nhiu|bao nhieu|nhieu|nhiu)|(?:bn|bao nhiu) (?:tien|k|nghin|ngan)|" +
        @"tien (?:kham )?(?:bn|bao nhiu|nhieu|nhiu)|gia (?:sao|the nao|ra sao|bn|bao nhiu)|bao nhiu)\b" +
        @"(?! (?:ngay|gio|tieng|phut|tuan|thang|lan|vien|lieu|ml|thoi gian))",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ClinicServiceWord = new(
        @"\b(?:kham|khoa|chuyen khoa|bac si|bs|dich vu|" + SpecialtyNames + @")\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Specialty names typed without "khám" ("nhi bn tiền"). Bare "mắt" and "sản" are left out:
    // unaccented they are also "mất" and "sẵn". "Nhi" is matched on the accented text only,
    // since "nhỉ" and "nhì" fold to the same letters.
    private const string SpecialtyNames =
        "noi tong quat|san phu khoa|san khoa|phu khoa|da lieu|tai mui hong|tim mach|co xuong khop|than kinh|noi tiet|nhan khoa|tieu hoa";
    private static readonly Regex AccentedPediatrics = new(@"(?<![\p{L}])nhi(?![\p{L}\p{M}])", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex PriceOutsideClinic = new(
        @"\b(?:bang gia|chi phi|gia (?:bao nhieu|bao nhiu|sao|the nao|ra sao|ban)|(?:bao nhieu|bao nhiu|nhieu|nhiu|bn) (?:tien|dong|nghin|ngan)|(?:mat|het|ton) (?:bao nhieu|bao nhiu|nhieu|nhiu|bn))\b",
        RegexOptions.CultureInvariant);

    internal static bool HasOutsidePriceEvidence(string message)
    {
        var lower = message.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormC);
        var plain = Regex.Replace(KnowledgeText.Normalize(lower), @"\b(?:danh gia|khoa cua)\b", " ");
        if (ClinicServiceWord.IsMatch(plain) || AccentedPediatrics.IsMatch(lower) || MentionsHealthPackage(plain) ||
            Regex.IsMatch(plain, @"\b(?:sieu am|noi soi|xet nghiem|chup x quang|vien phi|hoa don|thuoc|duoc pham)\b"))
            return false;
        return PriceOutsideClinic.IsMatch(plain) || Regex.IsMatch(lower, @"(?<!đánh )\bgiá\b");
    }

    internal static bool IsColloquialPriceQuestion(string lower, string normalized) =>
        ColloquialPriceCue.IsMatch(normalized) && !MedicinePriceQuestion.HasMedicineContext(lower) &&
        (ClinicServiceWord.IsMatch(normalized) || AccentedPediatrics.IsMatch(lower.Normalize(System.Text.NormalizationForm.FormC)));

    internal static bool HasPricingEvidence(string lower, string normalized)
    {
        if (IsColloquialPriceQuestion(lower, normalized)) return true;
        if (lower.Contains("bảng giá") || lower.Contains("chi phí khám") || lower.Contains("giá khám") ||
            lower.Contains("bao nhiêu tiền") || lower.Contains("hết bao nhiêu tiền") || lower.Contains("tiền khám") ||
            lower.Contains("phí khám") || lower.Contains("giá dịch vụ") || lower.Contains("viện phí") ||
            lower.Contains("giá bao nhiêu"))
            return true;

        // "Đánh giá" alone is not a price cue.
        var priceText = Regex.Replace(normalized, @"\bdanh\s+gia\b", " ", RegexOptions.CultureInvariant);
        return Regex.IsMatch(priceText,
            @"\b(?:bac si|bs|chuyen khoa|khoa kham)\b[^.!?]{0,60}\b(?:dang |se )?thu\s+(?:nhu the nao|the nao|ra sao|bao nhieu|muc nao)\b",
            RegexOptions.CultureInvariant) || Regex.IsMatch(
            priceText,
            @"\b(?:gia|phi|chi\s+phi)\b[^.!?]{0,60}\b(?:kham|dich\s+vu|goi|thuoc|sieu\s+am|noi\s+soi|chup|xet\s+nghiem|bao\s+nhieu|tien|chi\s+phi)\b|\b(?:kham|dich\s+vu|goi|thuoc|sieu\s+am|noi\s+soi|chup|xet\s+nghiem|bao\s+nhieu|tien)\b[^.!?]{0,60}\b(?:gia|phi)\b",
            RegexOptions.CultureInvariant);
    }

    internal static async Task<string> GetPackageCatalogAsync(AppDbContext dbContext, CancellationToken cancellationToken)
    {
        var packages = await dbContext.HealthPackages.AsNoTracking()
            .Where(package => package.IsActive)
            .OrderBy(package => package.Id)
            .Select(package => new { package.Name, package.Description, package.Price })
            .ToListAsync(cancellationToken);

        if (packages.Count == 0)
            return $"Hiện chưa có gói khám đang hoạt động. {PackageSelectionGuidance}";

        var message = new StringBuilder();
        message.AppendLine("Các gói hiện có tại ClinicCare:");
        foreach (var package in packages)
        {
            message.AppendLine($"- **{package.Name}**: {BusinessDisplay.Money(package.Price)} — {package.Description}");
        }
        message.AppendLine().Append(PackageSelectionGuidance);
        return message.ToString();
    }

    internal static async Task<string> GetConsultationFeesAsync(
        AppDbContext dbContext, string message, long? pendingSpecialtyId, CancellationToken cancellationToken)
    {
        var specialtiesWithFee = await dbContext.Specialties.AsNoTracking()
            .Where(s => s.IsActive && s.ConsultationFee > 0)
            .OrderBy(s => s.Id)
            .ToListAsync(cancellationToken);

        var lower = message.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormC);
        var plain = KnowledgeText.Normalize(message);
        var targetSpec = specialtiesWithFee.FirstOrDefault(s => MatchesSpecialtyName(s.Name, s.SpecialtyCode, lower, plain));

        if (targetSpec == null && pendingSpecialtyId.HasValue &&
            !Regex.IsMatch(plain, @"\b(?:bang gia|tat ca|tung khoa|cac khoa|cac chuyen khoa|kham chung)\b"))
            targetSpec = specialtiesWithFee.FirstOrDefault(s => s.Id == pendingSpecialtyId.Value);

        if (targetSpec != null)
            return $"Chi phí khám chuyên khoa **{targetSpec.Name}** tại ClinicCare là **{BusinessDisplay.Money(targetSpec.ConsultationFee)}** / lượt khám (chưa bao gồm các dịch vụ cận lâm sàng hoặc thuốc nếu có chỉ định của bác sĩ).";

        var result = new StringBuilder();
        result.AppendLine("Bảng giá chi phí khám tham khảo tại Phòng khám ClinicCare theo từng chuyên khoa:");
        foreach (var s in specialtiesWithFee)
        {
            result.AppendLine($"- **Khoa {s.Name}**: {BusinessDisplay.Money(s.ConsultationFee)} / lượt khám");
        }
        result.AppendLine("\n*Lưu ý: Chi phí trên là phí khám ban đầu, chưa bao gồm các chỉ định cận lâm sàng phát sinh nếu có.*");
        return result.ToString();
    }

    private static bool MatchesSpecialtyName(string name, string? code, string lower, string plain)
    {
        if (!string.IsNullOrWhiteSpace(code) && Regex.IsMatch(plain, $@"\b{Regex.Escape(KnowledgeText.Normalize(code))}\b"))
            return true;
        var normalized = KnowledgeText.Normalize(name);
        if (normalized == "nhi") return AccentedPediatrics.IsMatch(lower);
        var core = Regex.Replace(normalized, @"\bkhoa\b", "").Trim();
        return normalized.Length > 0 && Regex.IsMatch(plain, $@"\b{Regex.Escape(normalized)}\b") ||
            core.Length > 0 && Regex.IsMatch(plain, $@"\b{Regex.Escape(core)}\b");
    }

    internal static async Task<bool> NamesPricedSpecialtyAsync(AppDbContext db, string message, CancellationToken ct)
    {
        var lower = message.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormC);
        var plain = KnowledgeText.Normalize(message);
        var names = await db.Specialties.AsNoTracking().Where(s => s.IsActive && s.ConsultationFee > 0)
            .Select(s => new { s.Name, s.SpecialtyCode }).ToListAsync(ct);
        return names.Any(s => MatchesSpecialtyName(s.Name, s.SpecialtyCode, lower, plain));
    }
}
