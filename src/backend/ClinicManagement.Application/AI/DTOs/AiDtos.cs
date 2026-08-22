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
