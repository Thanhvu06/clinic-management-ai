using ClinicManagement.Application.AI.Tools;

namespace ClinicManagement.Infrastructure.AI;

/// <summary>Checks untrusted free text only, never backend-grounded clinical records.</summary>
public static class AiProviderOutputGuard
{
    public sealed record Block(string Message, string Code, bool Emergency);

    public static Block? Inspect(AiActorRole role, params string?[] texts)
    {
        var guard = new AiSafetyGuard();
        var safety = texts.Select(guard.Inspect).ToArray();
        if (safety.Any(x => x.IsEmergency))
            return new("Dấu hiệu có thể là tình huống cấp cứu. Hãy gọi 115 hoặc đến cơ sở cấp cứu gần nhất. Không chờ phản hồi qua trò chuyện.", "PROVIDER_OUTPUT_EMERGENCY", true);
        if (safety.Any(x => x.IsPromptInjection))
            return new("Nội dung trả lời không đáp ứng quy tắc an toàn. Vui lòng chọn thao tác có sẵn hoặc liên hệ cơ sở.", "PROVIDER_OUTPUT_UNSAFE", false);
        if (texts.Any(x => AiMedicalScopeGuard.IsUnsafeProviderAdvice(role, x)))
            return new("Tôi không thể kê đơn hoặc hướng dẫn liều dùng qua cuộc trò chuyện. Vui lòng trao đổi với bác sĩ hoặc xem toa thuốc đã được cơ sở xác nhận.", "MEDICAL_PRESCRIPTION_OUT_OF_SCOPE", false);
        return null;
    }
}
