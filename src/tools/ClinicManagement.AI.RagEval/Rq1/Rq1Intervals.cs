namespace ClinicManagement.AI.RagEval;

public sealed record Rq1Interval(double Lower, double Upper);

public static class Rq1Intervals
{
    private const double Z = 1.959963984540054;
    public const int BootstrapSamples = 10_000;
    public const int BootstrapSeed = 20261114;

    public static Rq1Interval Wilson(int count, int n)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfLessThan(n, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, n);
        var p = (double)count / n;
        var z2 = Z * Z;
        var center = (p + z2 / (2 * n)) / (1 + z2 / n);
        var radius = Z * Math.Sqrt(p * (1 - p) / n + z2 / (4.0 * n * n)) / (1 + z2 / n);
        return new(count == 0 ? 0 : center - radius, count == n ? 1 : center + radius);
    }

    // Newcombe (1998), paired method 10, Table I.
    public static Rq1Interval Newcombe(int both, int firstOnly, int secondOnly, int neither)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(both);
        ArgumentOutOfRangeException.ThrowIfNegative(firstOnly);
        ArgumentOutOfRangeException.ThrowIfNegative(secondOnly);
        ArgumentOutOfRangeException.ThrowIfNegative(neither);
        var n = checked(both + firstOnly + secondOnly + neither);
        var first = both + firstOnly;
        var second = both + secondOnly;
        var a = Wilson(first, n);
        var b = Wilson(second, n);
        var p = (double)first / n;
        var q = (double)second / n;
        var determinant = (double)both * neither - (double)firstOnly * secondOnly;
        var correlationNumerator = determinant > 0 ? Math.Max(0, determinant - n / 2.0) : determinant;
        var denominator = Math.Sqrt((double)first * (n - first) * second * (n - second));
        var phi = denominator == 0 ? 0 : correlationNumerator / denominator;
        double Distance(double x, double y) => Math.Sqrt(Math.Max(0, x * x + y * y - 2 * phi * x * y));
        return new(Math.Max(-1, p - q - Distance(p - a.Lower, b.Upper - q)),
            Math.Min(1, p - q + Distance(a.Upper - p, q - b.Lower)));
    }

    public static Rq1Interval BootstrapMean(IReadOnlyList<double> differences, int seed = BootstrapSeed)
    {
        if (differences.Count == 0) throw new ArgumentException("Bootstrap requires paired differences.");
        var random = new Random(seed);
        var means = new double[BootstrapSamples];
        for (var b = 0; b < means.Length; b++)
        {
            var sum = 0.0;
            for (var i = 0; i < differences.Count; i++) sum += differences[random.Next(differences.Count)];
            means[b] = sum / differences.Count;
        }
        Array.Sort(means);
        return new(Percentile(0.025), Percentile(0.975));

        double Percentile(double probability)
        {
            var index = probability * (means.Length - 1);
            var lower = (int)index;
            return means[lower] + (index - lower) * (means[lower + 1] - means[lower]);
        }
    }
}
