using System.Text.RegularExpressions;
using ClinicManagement.Application.AI.Knowledge;
using ClinicManagement.Application.Common.Expiry;

namespace ClinicManagement.Infrastructure.AI.Knowledge;

/// <summary>
/// Answers questions about the online-booking lock with the figures the lock rule actually uses
/// (<see cref="ClinicManagement.Infrastructure.Expiry.NoShowRestrictionRule"/>), citing the policy document.
/// A generated answer may restate those figures wrongly ("lỡ hẹn hai lần … khóa"), so it is never used here.
/// </summary>
public static class NoShowPolicyAnswer
{
    public static readonly KnowledgeSource Source = new("appointments", "Đặt, đổi, hủy lịch và vắng mặt", "Hạn xử lý và hạn chế online");

    private static readonly string[] CountWords = ["không", "một", "hai", "ba", "bốn", "năm", "sáu", "bảy", "tám", "chín", "mười"];
    private static readonly string[] PlainCountWords = ["khong", "mot", "hai", "ba", "bon", "nam", "sau", "bay", "tam", "chin", "muoi"];

    // Cancelling is not a missed visit: "hủy lịch" alone never counts toward the lock, so it is not listed here.
    private static readonly Regex MissedVisit = new(@"\b(?:lo hen|lo lich|bo hen|tre hen|vang mat|khong den(?: kham)?)\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex VisitCount = new(@"\b(?:may lan|bao nhieu lan|\d+ lan|(?:mot|hai|ba|bon|nam) lan)\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ConsequenceCue = new(
        @"\b(?:mo lai|mo khoa|bi gi|co sao|co bi|thi sao|sao khong|may lan|bao nhieu lan|\d+ lan|(?:mot|hai|ba|bon|nam) lan|bi phat|anh huong|bi chan)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex LockWord = new(@"(?<![\p{L}])(?:khóa|khoá)(?![\p{L}])", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex BookingLock = new(@"\b(?:dat lich|online|tai khoan)\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex LockContext = new(@"(?<![\p{L}])(?:đặt lịch|online|vắng mặt)(?![\p{L}])", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Same cues as the patient composer's isBookingPolicyQuestion, so a typed rule question
    // that also says "đặt lịch" is answered rather than treated as a booking or a symptom.
    private static readonly Regex BookingPolicyCue = new(
        @"\b(?:may lan|kieu gi|lam sao|tai sao|vi sao|bi khoa|khoa dat lich|quy dinh|chinh sach|mo lai|lo hen|vang mat|bao lau|co bi|thi sao)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool IsBookingPolicyQuestion(string message)
    {
        var plain = KnowledgeText.Normalize(message);
        return Regex.IsMatch(plain, @"\bdat (?:lich|hen|kham)\b") && BookingPolicyCue.IsMatch(plain);
    }

    public static bool IsLockQuestion(string message)
    {
        var plain = KnowledgeText.Normalize(message);
        // "Sai mật khẩu … khóa tài khoản" is the sign-in lockout, answered by LoginLockPolicyAnswer.
        if (LoginLockPolicyAnswer.IsLoginContext(plain)) return false;
        var mentionsLock = LockWord.IsMatch(KnowledgeText.Normalize(message, false));
        // Arriving late once ("trễ hẹn 20p có sao") is a lateness question for the documents, not a lock question.
        var lateOnly = Regex.IsMatch(plain, @"\btre hen\b") && !MissedVisit.IsMatch(Regex.Replace(plain, @"\btre hen\b", " "));
        var lockConsequence = mentionsLock || (lateOnly ? VisitCount.IsMatch(plain) : ConsequenceCue.IsMatch(plain));
        return MissedVisit.IsMatch(plain) && lockConsequence || mentionsLock && BookingLock.IsMatch(plain);
    }

    public static string Compose(string message, ExpiryRulesOptions rule)
    {
        var plain = KnowledgeText.Normalize(message);
        var threshold = rule.NoShowThreshold;
        var parts = new List<string>();
        if (Count(plain) is { } count)
        {
            parts.Add(count < threshold
                ? $"Vắng mặt {count} lần thì chưa bị khóa đặt lịch online. Ngưỡng khóa là {threshold} lần Vắng mặt trong {rule.NoShowWindowDays} ngày."
                : $"Nếu đủ {threshold} lần bị ghi nhận Vắng mặt trong vòng {rule.NoShowWindowDays} ngày thì tài khoản bị tạm khóa đặt lịch online {rule.OnlineBookingBlockDays} ngày.");
        }
        else
        {
            parts.Add($"Theo quy định của ClinicCare, {threshold} lần Vắng mặt trong {rule.NoShowWindowDays} ngày sẽ dẫn tới tạm khóa đặt lịch online {rule.OnlineBookingBlockDays} ngày.");
        }
        if (Regex.IsMatch(plain, @"\btre\b"))
            parts.Add($"Đến trễ dưới {rule.AppointmentGraceMinutes} phút vẫn được tiếp nhận bình thường; quá {rule.AppointmentGraceMinutes} phút mà chưa được tiếp nhận thì lịch có thể chuyển Vắng mặt.");
        if (Regex.IsMatch(plain, @"\bhuy\b"))
            parts.Add("Số lần khóa chỉ tính các lịch bị ghi nhận Vắng mặt (không đến khám); nếu biết sẽ không đến được, hãy hủy hoặc báo lễ tân sớm.");
        parts.Add($"Khóa tự hết sau {rule.OnlineBookingBlockDays} ngày; trong thời gian bị khóa, bạn vẫn có thể liên hệ lễ tân để đặt lịch. Trạng thái lịch hẹn xem trong tài khoản của bạn.");
        return string.Join(" ", parts);
    }

    /// <summary>
    /// True when a text about the online lock states a count, window or grace time the rule does not use.
    /// For a no-show lock question the whole answer is checked; otherwise only sentences that tie "khóa" to
    /// online booking or absences, so an answer about another lock ("khóa đăng nhập 15 phút") is kept.
    /// </summary>
    public static bool Contradicts(string text, ExpiryRulesOptions rule, string? question = null)
    {
        var whole = question is not null && IsLockQuestion(question);
        string[] sentences = whole ? [text] : Regex.Split(text, @"(?<=[.!?;])\s+|[\r\n]+");
        foreach (var sentence in sentences)
        {
            var accented = KnowledgeText.Normalize(sentence, false);
            if (!LockWord.IsMatch(accented) || !whole && !LockContext.IsMatch(accented)) continue;
            if (StatesOtherFigure(accented, rule)) return true;
        }
        return false;
    }

    private static bool StatesOtherFigure(string accented, ExpiryRulesOptions rule)
    {
        foreach (Match match in Regex.Matches(accented, @"(?<![\p{L}\d])(\d+|" + string.Join('|', CountWords) + @") (lần|ngày|phút)(?![\p{L}])"))
        {
            var value = int.TryParse(match.Groups[1].Value, out var number) ? number : Array.IndexOf(CountWords, match.Groups[1].Value);
            var allowed = match.Groups[2].Value switch
            {
                "lần" => value == rule.NoShowThreshold,
                "ngày" => value == rule.NoShowWindowDays || value == rule.OnlineBookingBlockDays,
                _ => value == rule.AppointmentGraceMinutes
            };
            if (!allowed) return true;
        }
        return false;
    }

    private static int? Count(string plain)
    {
        var match = Regex.Match(plain, @"\b(\d+|" + string.Join('|', PlainCountWords.Skip(1)) + @") lan\b");
        if (!match.Success) return null;
        return int.TryParse(match.Groups[1].Value, out var number) ? number : Array.IndexOf(PlainCountWords, match.Groups[1].Value);
    }
}
