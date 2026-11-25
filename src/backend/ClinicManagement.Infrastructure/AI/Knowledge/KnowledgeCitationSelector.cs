using System.Text.RegularExpressions;
using ClinicManagement.Application.AI.Knowledge;

namespace ClinicManagement.Infrastructure.AI.Knowledge;

internal static class KnowledgeCitationSelector
{
    private const double MinimumClaimScore = 0.25;
    private const double RelativeClaimScore = 0.70;
    private const int MinimumMatchedTerms = 3;
    private const int DirectEvidenceTerms = 5;
    private static readonly Regex ClaimBoundary = new(
        @"(?<=[.!?])\s+|\r?\n", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Attribution = new(
        @"\b(?:theo|dựa trên|từ)\s+(?:các\s+)?tài liệu(?:\s+công khai)?\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    internal static KnowledgeSource[] Select(string answer, IReadOnlyList<KnowledgeHit> promptHits)
    {
        if (promptHits.Count <= 1)
            return promptHits.Select(hit => hit.Chunk.Source).ToArray();

        var index = new Bm25KnowledgeIndex(promptHits.Select(hit => hit.Chunk).ToArray());
        var directEvidence = promptHits
            .SelectMany(hit => ClaimBoundary.Split(hit.Chunk.Text).SelectMany(EvidencePhrases)
                .Distinct(StringComparer.Ordinal).Select(phrase => (Phrase: phrase, Source: hit.Chunk.Source)))
            .GroupBy(item => item.Phrase, StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.First().Source, StringComparer.Ordinal);
        var matches = new HashSet<KnowledgeSource>();
        foreach (var segment in ClaimBoundary.Split(answer))
        {
            var claim = Attribution.Replace(segment, " ");
            foreach (var phrase in EvidencePhrases(claim))
                if (directEvidence.TryGetValue(phrase, out var source))
                    matches.Add(source);

            var termCount = KnowledgeText.ContentTerms(claim).Length;
            if (termCount < MinimumMatchedTerms) continue;

            var minimumCoverage = (double)MinimumMatchedTerms / termCount;
            var ranked = index.Search(claim)
                .Where(hit => index.EvaluateCoverage(claim, hit.Chunk).Coverage >= minimumCoverage).ToArray();
            if (ranked.Length == 0) continue;

            var threshold = Math.Max(MinimumClaimScore, ranked[0].Score * RelativeClaimScore);
            foreach (var hit in ranked.TakeWhile(hit => hit.Score >= threshold))
                matches.Add(hit.Chunk.Source);
        }

        // A short paraphrase or generic reply cannot reliably override the question's ranking.
        if (matches.Count == 0)
            return [promptHits[0].Chunk.Source];

        return promptHits.Select(hit => hit.Chunk.Source).Where(matches.Contains).Distinct().ToArray();
    }

    private static IEnumerable<string> EvidencePhrases(string text)
    {
        var terms = KnowledgeText.OrderedContentTerms(text).ToArray();
        for (var start = 0; start <= terms.Length - DirectEvidenceTerms; start++)
        {
            var phrase = terms.AsSpan(start, DirectEvidenceTerms).ToArray();
            if (phrase.Distinct(StringComparer.Ordinal).Count() == DirectEvidenceTerms)
                yield return string.Join(' ', phrase);
        }
    }
}
