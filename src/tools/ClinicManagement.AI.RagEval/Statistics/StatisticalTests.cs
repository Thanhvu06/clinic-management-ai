using MathNet.Numerics;

namespace ClinicManagement.AI.RagEval;

public sealed record McNemarResult(int B, int C, string Method, double PValue);
public sealed record WilcoxonResult(int NonZeroN, double WPlus, double WMinus, double Statistic,
    string Method, double PValue, double Z, double R);
public sealed record KappaResult(int Ratings, double? Kappa, double? LinearWeightedKappa);

public static class StatisticalTests
{
    public static McNemarResult McNemar(int b, int c)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(b);
        ArgumentOutOfRangeException.ThrowIfNegative(c);
        var n = checked(b + c);
        if (n < 25)
        {
            long choose = 1, tail = 1;
            for (var k = 1; k <= Math.Min(b, c); k++)
            {
                choose = choose * (n - k + 1) / k;
                tail += choose;
            }
            return new(b, c, "exact-binomial", Math.Min(1, 2.0 * tail / Math.Pow(2, n)));
        }
        var corrected = Math.Max(0, Math.Abs(b - c) - 1.0);
        return new(b, c, "chi-square-continuity-corrected",
            SpecialFunctions.Erfc(corrected / Math.Sqrt(2.0 * n)));
    }

    public static WilcoxonResult Wilcoxon(IReadOnlyList<double> left, IReadOnlyList<double> right)
    {
        if (left.Count != right.Count || left.Concat(right).Any(value => !double.IsFinite(value)))
            throw new ArgumentException("Wilcoxon requires finite paired observations.");
        var differences = left.Zip(right, (a, b) => a - b).Where(d => d != 0)
            .OrderBy(Math.Abs).ToArray();
        if (differences.Any(value => !double.IsFinite(value))) throw new ArgumentException("Paired differences must be finite.");
        var n = differences.Length;
        if (n == 0) return new(0, 0, 0, 0, "all-equal", 1, 0, 0);
        var ranks = new double[n];
        for (var i = 0; i < n;)
        {
            var end = i + 1;
            while (end < n && Math.Abs(differences[end]) == Math.Abs(differences[i])) end++;
            var rank = (i + 1 + end) / 2.0;
            for (var j = i; j < end; j++) ranks[j] = rank;
            i = end;
        }
        var plus = ranks.Where((_, i) => differences[i] > 0).Sum();
        var minus = ranks.Sum() - plus;
        // Midrank squares give the tie-corrected variance directly, including tied absolute differences.
        var variance = ranks.Sum(rank => rank * rank) / 4;
        var z = (plus - ranks.Sum() / 2) / Math.Sqrt(variance);
        var p = SpecialFunctions.Erfc(Math.Abs(z) / Math.Sqrt(2));
        if (n <= 25)
        {
            var doubled = ranks.Select(rank => (int)(rank * 2)).ToArray();
            var ways = new long[doubled.Sum() + 1];
            ways[0] = 1;
            var reached = 0;
            foreach (var rank in doubled)
            {
                for (var sum = reached; sum >= 0; sum--) ways[sum + rank] += ways[sum];
                reached += rank;
            }
            var boundary = (int)(2 * Math.Min(plus, minus));
            p = Math.Min(1, 2.0 * ways.Take(boundary + 1).Sum() / Math.Pow(2, n));
        }
        return new(n, plus, minus, Math.Min(plus, minus), n <= 25 ? "exact-sign-permutation-midranks" : "normal-tie-corrected",
            p, z, Math.Min(1, Math.Abs(z) / Math.Sqrt(n)));
    }

    public static KappaResult Kappa(IReadOnlyList<int> rater1, IReadOnlyList<int> rater2)
    {
        if (rater1.Count != rater2.Count || rater1.Concat(rater2).Any(value => value is < 0 or > 2))
            throw new ArgumentException("Kappa requires paired ratings in 0/1/2.");
        var n = rater1.Count;
        if (n == 0) return new(0, null, null);
        double Agreement(bool weighted, bool expected)
        {
            double result = 0;
            for (var i = 0; i < 3; i++)
                for (var j = 0; j < 3; j++)
                {
                    var weight = weighted ? 1 - Math.Abs(i - j) / 2.0 : i == j ? 1 : 0;
                    var probability = expected
                        ? (double)rater1.Count(value => value == i) * rater2.Count(value => value == j) / ((double)n * n)
                        : (double)rater1.Zip(rater2).Count(pair => pair.First == i && pair.Second == j) / n;
                    result += weight * probability;
                }
            return result;
        }
        double? Calculate(bool weighted)
        {
            var expected = Agreement(weighted, true);
            return expected >= 1 ? null : (Agreement(weighted, false) - expected) / (1 - expected);
        }
        return new(n, Calculate(false), Calculate(true));
    }

    public static double? Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        return sorted.Length == 0 ? null : (sorted[(sorted.Length - 1) / 2] + sorted[sorted.Length / 2]) / 2;
    }

    public static double? P95(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        return sorted.Length == 0 ? null : sorted[(int)Math.Ceiling(sorted.Length * 0.95) - 1];
    }
}
