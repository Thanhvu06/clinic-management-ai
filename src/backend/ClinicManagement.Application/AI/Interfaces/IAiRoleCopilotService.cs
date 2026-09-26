using ClinicManagement.Application.AI.DTOs;

namespace ClinicManagement.Application.AI.Interfaces;

public interface IAiRoleCopilotService
{
    Task<AiCopilotResponseDto> ChatAsync(AiCopilotRequestDto request, CancellationToken cancellationToken = default);
    IReadOnlyList<ClinicManagement.Application.AI.Tools.AiToolDefinition> GetToolsForCurrentRole();
}
