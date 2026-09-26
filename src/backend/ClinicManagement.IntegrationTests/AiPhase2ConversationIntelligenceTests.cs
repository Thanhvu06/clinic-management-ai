using ClinicManagement.Application.AI;
using ClinicManagement.Application.AI.Conversation;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.AI.Interfaces;
using ClinicManagement.Application.AI.Tools;
using ClinicManagement.Infrastructure.AI;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicManagement.IntegrationTests;

[Collection(AiPhase12AcceptanceCollection.Name)]
public sealed class AiPhase2ConversationIntelligenceTests : IntegrationTestBase
{
    public AiPhase2ConversationIntelligenceTests(CustomWebApplicationFactory factory) : base(factory) { }

    [Fact]
    public void Doctor_interrogatives_are_not_names_and_reason_is_grounded()
    {
        var classifier = new VietnameseIntentClassifier(IntentClassificationMode.Off);

        var doctorQuestion = classifier.Classify("có bác sĩ nào khám bệnh ho không");
        Assert.Equal(AiChatIntentTypes.SpecialtyRecommendation, doctorQuestion.Intent);
        Assert.Null(doctorQuestion.ExtractedDoctorName);
        Assert.Equal("Ho", doctorQuestion.ExtractedReason);

        var symptomQuestion = classifier.Classify("tôi bị đau bụng thì nên chọn bác sĩ nào\\");
        Assert.Equal(AiChatIntentTypes.SpecialtyRecommendation, symptomQuestion.Intent);
        Assert.Null(symptomQuestion.ExtractedDoctorName);
        Assert.Equal("Đau bụng", symptomQuestion.ExtractedReason);

        var explicitDoctor = classifier.Classify("cho tôi xem bác sĩ Nguyễn Minh Khải");
        Assert.Equal(AiChatIntentTypes.DoctorSearch, explicitDoctor.Intent);
        Assert.Equal("Nguyễn Minh Khải", explicitDoctor.ExtractedDoctorName);
    }

    [Fact]
    public void Reason_guard_rejects_commands_and_gibberish_without_losing_clinical_additions()
    {
        Assert.False(VietnameseIntentClassifier.IsValidBookingReason("chốt luôn ngay bây giờ"));
        Assert.False(VietnameseIntentClassifier.IsValidBookingReason("kkkkkkkkkkkk"));
        Assert.False(VietnameseIntentClassifier.IsClinicalComplaint("cho tôi"));
        Assert.Equal("Đau bụng 2 ngày, tối qua nôn 3 lần", VietnameseIntentClassifier.MergeReasons("Đau bụng 2 ngày", "tối qua nôn 3 lần"));
        Assert.Equal("Đau bụng", AiTextNormalizer.NormalizeClinicalReason("tôi bị đau bụng thì nên chọn bác sĩ nào\\"));
    }

    [Fact]
    public void Pipeline_returns_typed_entities_and_blocks_safety_before_provider()
    {
        using var scope = Factory.Services.CreateScope();
        var pipeline = scope.ServiceProvider.GetRequiredService<IAiConversationPipeline>();

        var analysis = pipeline.Analyze("Tôi bị đau bụng thì nên chọn bác sĩ nào\\");
        Assert.Equal("Đau bụng", analysis.Entities.ClinicalReason);
        Assert.DoesNotContain(analysis.Entities.Entities, x => x.Type == "DoctorName" && x.IsValid);
        Assert.Equal(AiProviderStatusContract.NotCalled, analysis.ProviderStatus);

        var emergency = pipeline.Analyze("đau ngực dữ dội và khó thở");
        Assert.True(emergency.Safety.IsEmergency);
        Assert.Equal(AiProviderStatusContract.SafetyBlocked, emergency.ProviderStatus);
    }

    [Fact]
    public void Phase2_role_catalog_is_server_owned_and_read_only()
    {
        var definitions = AiRoleToolCatalog.Definitions;
        Assert.Equal(definitions.Count, definitions.Select(x => x.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(definitions, definition =>
        {
            Assert.Equal(AiToolRiskLevel.Low, definition.RiskLevel);
            Assert.Equal(AiToolConfirmationRequirement.None, definition.Confirmation);
            Assert.NotEmpty(definition.AllowedRoles);
        });
        Assert.DoesNotContain(definitions, x => x.Name.Contains("execute", StringComparison.OrdinalIgnoreCase) || x.Name.Contains("write", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Provider_contract_does_not_promote_local_fallback_to_online()
    {
        Assert.Equal(AiProviderStatusContract.NotCalled, AiProviderStatusContract.FromProviderResult("Success", called: false));
        Assert.Equal(AiProviderStatusContract.Online, AiProviderStatusContract.FromProviderResult("Success", called: true));
        Assert.Equal(AiProviderStatusContract.Degraded, AiProviderStatusContract.FromProviderResult("InvalidResponse", called: true));
        Assert.Equal(AiProviderStatusContract.Unavailable, AiProviderStatusContract.FromProviderResult("Disabled", called: true));
    }
}
