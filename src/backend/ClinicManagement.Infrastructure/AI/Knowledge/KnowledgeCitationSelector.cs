using System.Text.RegularExpressions;
using ClinicManagement.Application.AI.Knowledge;

namespace ClinicManagement.Infrastructure.AI.Knowledge;

internal static class KnowledgeCitationSelector
{
    private const double MinimumClaimScore = 0.25;
    private const double RelativeClaimScore = 0.70;
    private static readonly Regex ClaimBoundary = new(
        @"(?<=[.!?])\s+|\r?\n", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    internal static KnowledgeSource[] Select(string answer, IReadOnlyList<KnowledgeHit> promptHits)
    {
        if (promptHits.Count <= 1)
            return promptHits.Select(hit => hit.Chunk.Source).ToArray();

        var index = new Bm25KnowledgeIndex(promptHits.Select(hit => hit.Chunk).ToArray());
        var matches = new HashSet<KnowledgeSource>();
        foreach (var claim in ClaimBoundary.Split(answer))
        {
            if (KnowledgeText.ContentTerms(claim).Length < 3) continue;

            var ranked = index.Search(claim);
            if (ranked.Count == 0) continue;

            var threshold = Math.Max(MinimumClaimScore, ranked[0].Score * RelativeClaimScore);
            foreach (var hit in ranked.TakeWhile(hit => hit.Score >= threshold))
                matches.Add(hit.Chunk.Source);
        }

        // Short paraphrases can lack a qualifying clause while still matching a prompt passage.
        if (matches.Count == 0)
            return index.Search(answer).Take(1).Select(hit => hit.Chunk.Source).ToArray();

        return promptHits.Select(hit => hit.Chunk.Source).Where(matches.Contains).Distinct().ToArray();
    }
}
