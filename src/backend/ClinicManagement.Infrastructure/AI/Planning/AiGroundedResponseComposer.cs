using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.AI.Planning;
using ClinicManagement.Application.AI.Tools;

namespace ClinicManagement.Infrastructure.AI.Planning;

/// <summary>Composes only from validated local decisions and actual tool results.</summary>
public sealed class AiGroundedResponseComposer : IAiGroundedResponseComposer
{
    public AiGroundedResponse Compose(AiPlannerDecision decision, IReadOnlyList<AiToolExecutionResult> results)
    {
        if (results.Count == 0)
            return new AiGroundedResponse
            {
                Message = decision.Message ?? decision.Clarification ?? "Vui lòng mô tả rõ dữ liệu cần tra cứu.",
                NavigationRoute = decision.NavigationRoute
            };

        var cards = results.Select(result => new AiCopilotDataCardDto
        {
            Type = result.ResultType ?? "workspace_result",
            Title = result.Status == "completed" ? "Dữ liệu đã kiểm chứng" : "Không thể truy cập dữ liệu",
            Description = result.DisplayText ?? result.Error?.Message,
            Data = result.Status == "completed" ? result.Data : null,
            Sources = result.DataSources
        }).ToArray();
        var failed = results.FirstOrDefault(x => x.Status != "completed");
        return new AiGroundedResponse
        {
            Message = failed?.Error?.Message ?? string.Join(" ", results.Select(x => x.DisplayText).Where(x => !string.IsNullOrWhiteSpace(x))),
            NavigationRoute = decision.NavigationRoute,
            Cards = cards,
            Sources = results.SelectMany(x => x.DataSources).Distinct().ToArray()
        };
    }
}
