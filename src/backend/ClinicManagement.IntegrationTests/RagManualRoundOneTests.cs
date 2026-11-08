using ClinicManagement.Application.AI.Tools;
using ClinicManagement.Infrastructure.AI;
using ClinicManagement.Infrastructure.AI.Knowledge;

namespace ClinicManagement.IntegrationTests;

public sealed class RagManualRoundOneTests
{
    [Theory]
    [InlineData("di kham lan dau can mang gi", "prepare-visit", "Lần đầu và tái khám", "CCCD")]
    [InlineData("trễ hẹn 20p có sao k", "appointments", "Đến trễ và vắng mặt", "dưới 30 phút")]
    public void Manual_question_retrieves_specific_guidance_safe_for_extractive_fallback(
        string question, string docId, string heading, string requiredText)
    {
        var catalog = new MarkdownKnowledgeCatalog();
        var index = new Bm25KnowledgeIndex(catalog.Chunks);
        var hit = index.Search(question).First();

        Assert.Equal(docId, hit.Chunk.Source.DocId);
        Assert.Equal(heading, hit.Chunk.Source.Heading);
        Assert.Contains(requiredText, hit.Chunk.Text);
        Assert.True(index.MeetsCoverage(question, hit.Chunk, 0.5, 1.25));
        Assert.Null(AiProviderOutputGuard.Inspect(AiActorRole.Patient, hit.Chunk.Text));
    }
}
