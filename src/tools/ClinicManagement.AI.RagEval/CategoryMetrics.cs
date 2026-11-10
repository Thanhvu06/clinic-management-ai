namespace ClinicManagement.AI.RagEval;

public sealed class CategoryMetrics
{
    public int N { get; private set; }
    public int InScope { get; private set; }
    public int Outside { get; private set; }
    private int _hit1, _hit3, _rejected;
    private double _reciprocal;
    public double? HitAt1 => InScope == 0 ? null : (double)_hit1 / InScope;
    public double? HitAt3 => InScope == 0 ? null : (double)_hit3 / InScope;
    public double? Mrr => InScope == 0 ? null : _reciprocal / InScope;
    public double? CorrectRejection => Outside == 0 ? null : (double)_rejected / Outside;

    public void Add(EvaluationQuestion question, string[] retrievedDocIds)
    {
        N++;
        if (question.ExpectedDocId is null)
        {
            Outside++;
            if (retrievedDocIds.Length == 0) _rejected++;
            return;
        }
        InScope++;
        var rank = Array.IndexOf(retrievedDocIds, question.ExpectedDocId);
        if (rank == 0) _hit1++;
        if (rank is >= 0 and < 3) _hit3++;
        if (rank >= 0) _reciprocal += 1.0 / (rank + 1);
    }
}
