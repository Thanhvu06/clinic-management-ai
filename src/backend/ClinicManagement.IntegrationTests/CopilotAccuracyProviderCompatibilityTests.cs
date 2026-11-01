using ClinicManagement.Application.AI.Interfaces;
using ClinicManagement.Application.AI.Planning;
using ClinicManagement.Application.AI.Tools;
using ClinicManagement.Infrastructure.AI.Planning;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ClinicManagement.IntegrationTests;

public class CopilotAccuracyProviderCompatibilityTests
{
    [Theory]
    [InlineData(AiActorRole.Receptionist)]
    [InlineData(AiActorRole.DiagnosticTechnician)]
    [InlineData(AiActorRole.Pharmacist)]
    [InlineData(AiActorRole.Admin)]
    public async Task AddedReadTools_DoNotChangeExistingProviderToolContracts(AiActorRole role)
    {
        var original = AiRoleToolCatalog.Definitions.Where(tool => tool.AllowedRoles.Contains(role)).ToArray();
        var added = AiRoleToolCatalog.DeterministicReadDefinitions.Where(tool => tool.AllowedRoles.Contains(role)).ToArray();
        Assert.NotEmpty(added);
        AiRolePlannerProviderRequest? captured = null;
        var provider = new Mock<IAiSpecialtySuggestionProvider>();
        provider.Setup(p => p.PlanRoleCopilotAsync(It.IsAny<AiRolePlannerProviderRequest>(), It.IsAny<CancellationToken>()))
            .Callback<AiRolePlannerProviderRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(new AiRolePlannerProviderResult { Status = "Disabled" });
        var health = new Mock<IAiProviderHealth>();
        health.Setup(h => h.CanAttempt()).Returns(true);
        var planner = new GeminiStructuredPlanner(provider.Object, health.Object, NullLogger<GeminiStructuredPlanner>.Instance);
        await planner.PlanAsync(new AiStructuredPlannerRequest
        {
            Role = role, Message = "Câu hỏi kiểm thử hợp đồng provider",
            AllowedTools = original.Concat(added).ToArray(), AllowedToolNames = original.Concat(added).Select(tool => tool.Name).ToArray()
        });
        Assert.NotNull(captured);
        Assert.Equal(original.Select(tool => tool.Name).Order(), captured.AllowedTools.Select(tool => tool.Name).Order());
        Assert.DoesNotContain(captured.AllowedTools, tool => added.Any(local => local.Name == tool.Name));
    }
}
