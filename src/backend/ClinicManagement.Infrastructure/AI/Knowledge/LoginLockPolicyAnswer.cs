using System.Text.RegularExpressions;
using ClinicManagement.Application.AI.Knowledge;
using Microsoft.AspNetCore.Identity;

namespace ClinicManagement.Infrastructure.AI.Knowledge;

/// <summary>
/// Answers "sai mật khẩu mấy lần thì bị khóa" from the sign-in lockout the app actually configures
/// (Identity <see cref="LockoutOptions"/>), not from the online-booking no-show lock.
/// </summary>
public static class LoginLockPolicyAnswer
{
    public static readonly KnowledgeSource Source = new("accounts", "Tài khoản và đăng nhập website", "Quên mật khẩu hoặc lỗi đăng nhập");

    private static readonly Regex LoginCue = new(@"\b(?:mat khau|dang nhap|password|mk)\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    // A sentence that also names the booking lock ("bị khóa đặt lịch online") stays a no-show question.
    private static readonly Regex BookingLockCue = new(@"\b(?:dat lich|online|vang mat|lo hen|bo hen|khong den)\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex LockWord = new(@"(?<![\p{L}])(?:khóa|khoá)(?![\p{L}])", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex WrongAttempts = new(@"\bsai (?:mat khau|mk|nhieu lan|may lan|\d+ lan)\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool IsLoginContext(string plain) => LoginCue.IsMatch(plain) && !BookingLockCue.IsMatch(plain);

    public static bool IsQuestion(string message)
    {
        var plain = KnowledgeText.Normalize(message);
        return IsLoginContext(plain) && (LockWord.IsMatch(KnowledgeText.Normalize(message, false)) || WrongAttempts.IsMatch(plain));
    }

    public static string Compose(LockoutOptions? lockout)
    {
        var parts = new List<string>();
        if (lockout is { AllowedForNewUsers: true, MaxFailedAccessAttempts: > 0 } && lockout.DefaultLockoutTimeSpan > TimeSpan.Zero)
            parts.Add($"Theo cấu hình đăng nhập hiện tại của ClinicCare, nhập sai mật khẩu {lockout.MaxFailedAccessAttempts} lần liên tiếp thì tài khoản bị tạm khóa đăng nhập {Duration(lockout.DefaultLockoutTimeSpan)}; hết thời gian đó bạn có thể đăng nhập lại.");
        else
            parts.Add("Hiện ClinicCare chưa có thông tin về số lần nhập sai mật khẩu dẫn tới khóa đăng nhập.");
        parts.Add("Đây là khóa đăng nhập, khác với tạm khóa đặt lịch online do vắng mặt.");
        parts.Add("Nếu quên mật khẩu, tại trang Đăng nhập bấm Quên mật khẩu?, nhập email tài khoản rồi bấm Gửi mã đặt lại mật khẩu và làm theo hướng dẫn. Chat không đặt lại mật khẩu hay gửi mã khôi phục.");
        parts.Add("Không nhận được hướng dẫn hoặc vẫn không đăng nhập được, vui lòng liên hệ lễ tân.");
        return string.Join(" ", parts);
    }

    private static string Duration(TimeSpan span) =>
        span.TotalMinutes < 60 || span.TotalMinutes % 60 != 0 ? $"{(int)Math.Ceiling(span.TotalMinutes)} phút" : $"{(int)span.TotalHours} giờ";
}
