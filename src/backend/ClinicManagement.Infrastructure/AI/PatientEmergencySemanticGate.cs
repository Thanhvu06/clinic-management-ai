using ClinicManagement.Infrastructure.AI.Knowledge;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ClinicManagement.Infrastructure.AI;

public sealed class PatientEmergencySemanticGate(
    OllamaKnowledgeClient embeddings, IOptions<OllamaOptions> options, ILogger<PatientEmergencySemanticGate> logger) : BackgroundService
{
    private static readonly TimeSpan QueryBudget = TimeSpan.FromSeconds(1);
    private float[][]? _index;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.IsEnabled) return;
        while (!await BuildIndexAsync(stoppingToken))
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        logger.LogInformation("Patient emergency embedding index ready with {Model}.", options.Value.EmbeddingModel);
    }

    internal async Task<bool> BuildIndexAsync(CancellationToken ct)
    {
        var vectors = new List<float[]>();
        var examples = PatientEmergencyExamples.Emergency.Concat(PatientEmergencyExamples.Routine);
        foreach (var batch in examples.Chunk(16))
        {
            var response = await embeddings.EmbedAsync(batch, ct);
            var dimension = vectors.Count == 0 ? response.Value?.FirstOrDefault()?.Length ?? 0 : vectors[0].Length;
            if (!ValidVectors(response.Value, batch.Length, dimension)) return false;
            vectors.AddRange(response.Value!);
        }

        Volatile.Write(ref _index, vectors.ToArray());
        return true;
    }

    internal async Task<bool> IsEmergencyAsync(string? message, CancellationToken ct)
    {
        var index = Volatile.Read(ref _index);
        if (index is null) return false;
        var text = KnowledgePrivacyMask.Mask(message).Trim();
        if (text.Length == 0) return false;
        var response = await embeddings.EmbedAsync([text], ct, QueryBudget);
        return ValidVectors(response.Value, 1, index[0].Length) && IsEmergency(Score(index, response.Value![0]));
    }

    internal static (double Emergency, double Routine) Score(float[][] index, float[] query)
    {
        var boundary = PatientEmergencyExamples.Emergency.Length;
        var emergency = -1.0;
        var routine = -1.0;
        for (var i = 0; i < index.Length; i++)
        {
            var similarity = Bm25KnowledgeIndex.Cosine(query, index[i]);
            if (i < boundary) emergency = Math.Max(emergency, similarity);
            else routine = Math.Max(routine, similarity);
        }
        return (emergency, routine);
    }

    internal static bool IsEmergency((double Emergency, double Routine) score) =>
        score.Emergency >= 0.62 && score.Emergency - score.Routine >= 0.03;

    private static bool ValidVectors(float[][]? vectors, int count, int dimension) =>
        dimension > 0 && vectors?.Length == count && vectors.All(vector =>
            vector?.Length == dimension && vector.All(float.IsFinite) && vector.Any(value => value != 0));
}
