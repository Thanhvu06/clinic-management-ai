using Microsoft.AspNetCore.Identity;

namespace ClinicManagement.Infrastructure.Identity;

internal static class IdentityErrorMessages
{
    public static string ToVietnamese(IdentityError error) => error.Code switch
    {
        "PasswordRequiresNonAlphanumeric" => "Mật khẩu phải chứa ít nhất một ký tự đặc biệt.",
        "PasswordRequiresDigit" => "Mật khẩu phải chứa ít nhất một chữ số.",
        "PasswordRequiresLower" => "Mật khẩu phải chứa ít nhất một chữ cái thường.",
        "PasswordRequiresUpper" => "Mật khẩu phải chứa ít nhất một chữ cái hoa.",
        "PasswordTooShort" => "Mật khẩu quá ngắn.",
        _ => error.Description
    };
}
