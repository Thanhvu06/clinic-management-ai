using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ClinicManagement.Application.AI.DTOs;

namespace ClinicManagement.Application.AI.Interfaces;

public interface IAiSpecialtyService
{
    Task<AiSuggestionResponseDto> GetSuggestionsAsync(AiSuggestionRequestDto request, CancellationToken cancellationToken = default);
    Task<AiChatResponseDto> ChatAsync(AiChatRequestDto request, CancellationToken cancellationToken = default);
}

public interface IAiSpecialtySuggestionProvider
{
    Task<List<AiProviderSuggestionResult>> GetSuggestionsFromAiAsync(string symptomDescription, List<WhitelistItemDto> whitelist, CancellationToken cancellationToken = default);
    Task<AiChatProviderResult> ChatWithAiAsync(string message, List<ChatMessageDto> context, List<WhitelistItemDto> whitelist, CancellationToken cancellationToken = default);
}
