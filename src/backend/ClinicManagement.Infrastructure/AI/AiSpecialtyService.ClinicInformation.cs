using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.AI.Knowledge;
using ClinicManagement.Application.AI.Tools;
using ClinicManagement.Infrastructure.AI.Knowledge;

namespace ClinicManagement.Infrastructure.AI;

public partial class AiSpecialtyService
{
    private async Task<AiChatResponseDto> ResolveRecoveredClinicInformationAsync(AiChatRequestDto request,
        string message, ClinicInformationTopic topic, int version, CancellationToken ct)
    {
        var response = topic == ClinicInformationTopic.OpeningHours
            ? await HandleFacilityInquiryAsync(request, message, version, ct)
            : await HandlePricingInquiryAsync(request, message, version, ct);
        response.Sources.Add(topic == ClinicInformationTopic.OpeningHours
            ? new KnowledgeSource("database-clinic-locations", "Cơ sở khám — dữ liệu hệ thống (DB)", "Giờ hoạt động hiện hành")
            : new KnowledgeSource("database-specialties", "Chuyên khoa — dữ liệu hệ thống (DB)", "Phí khám hiện hành"));
        response.ToolResults.Add(new AiToolExecutionResult { ResultType = "knowledge_sources", Data = new { sources = response.Sources } });
        return response;
    }
}
