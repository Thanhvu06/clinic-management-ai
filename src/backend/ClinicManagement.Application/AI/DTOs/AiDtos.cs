using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ClinicManagement.Application.AI.DTOs;

public class AiSuggestionRequestDto
{
    [Required]
    [MinLength(10)]
    [MaxLength(1000)]
    public string SymptomDescription { get; set; } = string.Empty;
}

public class AiSuggestionResponseDto
{
    public string Outcome { get; set; } = string.Empty;
    public string Disclaimer { get; set; } = "Gợi ý chỉ mang tính tham khảo, không thay thế chẩn đoán của bác sĩ.";
    public List<AiSpecialtySuggestionDto> Suggestions { get; set; } = new();
}

public class AiSpecialtySuggestionDto
{
    public long SpecialtyId { get; set; }
    public string SpecialtyCode { get; set; } = string.Empty;
    public string SpecialtyName { get; set; } = string.Empty;
    public int Rank { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public class AiProviderSuggestionResult
{
    public string SpecialtyCode { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

public class WhitelistItemDto
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public class ChatMessageDto
{
    [Required]
    [RegularExpression("^(user|model)$", ErrorMessage = "Role chá»‰ Ä‘Æ°á»£c lĂ  user hoáº·c model")]
    public string Role { get; set; } = string.Empty; // "user" or "model"

    [Required]
    [MaxLength(500, ErrorMessage = "Ná»™i dung tin nháº¯n vÆ°á»£t quĂ¡ 500 kĂ½ tá»±.")]
    public string Content { get; set; } = string.Empty;
}

public class AiChatRequestDto
{
    [Required]
    [MaxLength(500, ErrorMessage = "Tin nháº¯n vÆ°á»£t quĂ¡ 500 kĂ½ tá»±.")]
    public string Message { get; set; } = string.Empty;

    [MaxLength(6, ErrorMessage = "Lá»‹ch sá»­ khĂ´ng Ä‘Æ°á»£c vÆ°á»£t quĂ¡ 6 tin nháº¯n.")]
    public List<ChatMessageDto> Context { get; set; } = new();
}

public class AiChatResponseDto
{
    public string Reply { get; set; } = string.Empty;
    public List<AiSpecialtySuggestionDto> SuggestedSpecialties { get; set; } = new();
    public string Urgency { get; set; } = "ROUTINE"; // ROUTINE, SOON, EMERGENCY
    public bool ManualSelectionRequired { get; set; } = false;
}

public class AiChatProviderResult
{
    public string Reply { get; set; } = string.Empty;
    public List<string> SuggestedSpecialtyCodes { get; set; } = new();
    public string Urgency { get; set; } = "ROUTINE";
}
