using System.Text.RegularExpressions;
using ClinicManagement.Application.AI.Conversation;
using ClinicManagement.Application.AI.DTOs;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.AI;

public partial class AiSpecialtyService
{
    private static readonly Regex SymptomLocationQuestion = new(
        @"\b(?:kham (?:o dau|(?:o )?cho nao|(?:o )?khoa (?:nao|gi)|(?:o )?chuyen khoa (?:nao|gi))|den dau (?:de )?kham)\b",
        RegexOptions.CultureInvariant);

    private async Task<AiChatResponseDto?> ResolveSymptomLocationAsync(
        AiChatRequestDto request, string message, int version, CancellationToken ct)
    {
        if (!SymptomLocationQuestion.IsMatch(AiTextNormalizer.NormalizeForComparison(message)) ||
            !SymptomSpecialtyRules.HasSymptom(message))
            return IsClinicParkingQuestion(message) ? await HandleFacilityInquiryAsync(request, message, version, ct) : null;

        var response = await SymptomSpecialtyRules.ApplyAsync(_dbContext, new AiChatResponseDto
        {
            Urgency = "ROUTINE",
            PromptVersion = GeminiAiProvider.CurrentPromptVersion,
            DialogueOutcome = "SymptomLocationResolved",
            PrimaryIntent = AiChatIntentTypes.SpecialtyRecommendation,
            AssistantStatus = "Online",
            ProviderStatus = "NotCalled",
            ProviderState = "NotCalled"
        }, message, ct);
        if (response.SpecialtySuggestions.Count == 0) return null;

        var specialtyIds = response.SpecialtySuggestions.Select(suggestion => suggestion.SpecialtyId).ToArray();
        var locations = await _dbContext.Departments.AsNoTracking()
            .Where(department => department.IsActive && department.Facility.IsActive &&
                department.SpecialtyId.HasValue && specialtyIds.Contains(department.SpecialtyId.Value) &&
                department.Facility.Address != null && department.Facility.Address != "")
            .Select(department => new
            {
                department.SpecialtyId, department.FacilityId, department.Facility.Name, department.Facility.Address, department.Facility.City
            })
            .Distinct().OrderBy(location => location.FacilityId).ToListAsync(ct);
        var contact = await CreateContactReceptionActionAsync(ct);
        foreach (var suggestion in response.SpecialtySuggestions)
        {
            var addresses = locations.Where(location => location.SpecialtyId == suggestion.SpecialtyId)
                .Select(location => $"{location.Name} — {location.Address?.Trim()}" +
                    (string.IsNullOrWhiteSpace(location.City) ? "" : $", {location.City.Trim()}")).ToArray();
            response.Message += addresses.Length > 0
                ? $"\n- {suggestion.SpecialtyName}: {string.Join(" · ", addresses)}"
                : $"\n- {suggestion.SpecialtyName}: Bạn có thể liên hệ lễ tân để xác nhận cơ sở khám.";
        }
        response.Reply = response.Message;
        response.Actions.Add(contact);
        await PreserveExistingDraftAsync(request, response, ct);
        return WithDraftVersionSync(response, version);
    }
}
