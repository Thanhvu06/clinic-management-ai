using System.ComponentModel.DataAnnotations;
using ClinicManagement.Application.AI.Tools;

namespace ClinicManagement.Application.AI.DTOs;

public sealed class AiCopilotRequestDto
{
    [Required]
    [MaxLength(500)]
    public string Message { get; set; } = string.Empty;
}

public sealed class AiCopilotDataCardDto
{
    public string Type { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public object? Data { get; init; }
    public IReadOnlyList<AiToolDataSource> Sources { get; init; } = Array.Empty<AiToolDataSource>();
}

public sealed class AiCopilotResponseDto
{
    public string Role { get; init; } = string.Empty;
    public string AssistantStatus { get; init; } = "Online";
    public string ProviderStatus { get; init; } = "NotCalled";
    public string Intent { get; init; } = AiChatIntentTypes.UnclearOrOutOfScope;
    public string Message { get; init; } = string.Empty;
    public string? SafetyNotice { get; init; }
    public string? NavigationRoute { get; init; }
    public IReadOnlyList<string> SuggestedPrompts { get; init; } = Array.Empty<string>();
    public IReadOnlyList<AiCopilotDataCardDto> Cards { get; init; } = Array.Empty<AiCopilotDataCardDto>();
    public IReadOnlyList<AiToolDefinition> AvailableTools { get; init; } = Array.Empty<AiToolDefinition>();
}
