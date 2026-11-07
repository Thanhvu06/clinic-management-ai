namespace ClinicManagement.Application.AI.Knowledge;

public sealed record KnowledgeSource(string DocId, string Title, string Heading);
public sealed record KnowledgeChunk(string Id, KnowledgeSource Source, string Text);
public sealed record KnowledgeHit(KnowledgeChunk Chunk, double Score);
public sealed record KnowledgeRetrieval(IReadOnlyList<KnowledgeHit> Hits, bool EmbeddingWasCalled = false, bool HybridUsed = false);
public sealed record KnowledgeAnswer(string Text, IReadOnlyList<KnowledgeSource> Sources,
    bool Generated, bool ProviderWasCalled);

public interface IClinicKnowledgeRetriever
{
    Task<KnowledgeRetrieval> RetrieveAsync(string question, CancellationToken ct = default);
}

public interface IClinicKnowledgeAnswerer
{
    Task<KnowledgeAnswer?> AnswerAsync(string question, CancellationToken ct = default);
}
