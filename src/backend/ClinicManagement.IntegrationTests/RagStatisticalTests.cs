extern alias RagEval;

using StatisticalTests = RagEval::ClinicManagement.AI.RagEval.StatisticalTests;

namespace ClinicManagement.IntegrationTests;

public sealed class RagStatisticalTests
{
    [Theory]
    [InlineData(0, 0, 1.0)]
    [InlineData(3, 0, 0.25)]
    [InlineData(10, 2, 0.03857421875)]
    [InlineData(24, 0, 0.00000011920928955078125)]
    public void McNemar_exact_matches_hand_binomial_sums(int b, int c, double expected)
    {
        var result = StatisticalTests.McNemar(b, c);
        Assert.Equal("exact-binomial", result.Method);
        Assert.Equal(expected, result.PValue, 14);
        Assert.Equal(result.PValue, StatisticalTests.McNemar(c, b).PValue);
    }

    [Fact]
    public void McNemar_switches_at_25_and_matches_corrected_chi_square_reference()
    {
        var result = StatisticalTests.McNemar(20, 5);
        Assert.Equal("chi-square-continuity-corrected", result.Method);
        Assert.Equal(0.005110260660855874, result.PValue, 14);
        Assert.Equal(1, StatisticalTests.McNemar(12, 13).PValue);
    }

    [Fact]
    public void Wilcoxon_exact_matches_standard_n10_critical_value_and_small_sign_enumeration()
    {
        double[] differences = [-1, -2, 3, 4, -5, 6, 7, 8, 9, 10];
        var result = StatisticalTests.Wilcoxon(differences, new double[10]);
        Assert.Equal(8, result.Statistic);
        Assert.Equal(0.048828125, result.PValue, 14);
        var small = StatisticalTests.Wilcoxon([1, 2, 3], [0, 0, 0]);
        Assert.Equal(0.25, small.PValue);
        Assert.Equal(6, small.WPlus);
        Assert.Equal(0, small.WMinus);
    }

    [Fact]
    public void Wilcoxon_uses_average_ranks_for_ties_and_removes_zero_pairs()
    {
        var tied = StatisticalTests.Wilcoxon([1, 1, -2, 3], [0, 0, 0, 0]);
        Assert.Equal(7, tied.WPlus);
        Assert.Equal(3, tied.WMinus);
        Assert.Equal(0.625, tied.PValue);
        Assert.Equal("exact-sign-permutation-midranks", tied.Method);
        var withZero = StatisticalTests.Wilcoxon([0, 1, -2, 3], [0, 0, 0, 0]);
        Assert.Equal(3, withZero.NonZeroN);
        Assert.Equal(0.75, withZero.PValue);
        var reversed = StatisticalTests.Wilcoxon([0, 0, 0, 0], [1, 1, -2, 3]);
        Assert.Equal(tied.PValue, reversed.PValue);
        Assert.Equal(tied.R, reversed.R);
    }

    [Fact]
    public void Wilcoxon_all_equal_has_p_one_and_zero_effect()
    {
        var result = StatisticalTests.Wilcoxon([0, 1, 2], [0, 1, 2]);
        Assert.Equal(0, result.NonZeroN);
        Assert.Equal(1, result.PValue);
        Assert.Equal(0, result.R);
        Assert.Equal(0, result.Z);
    }

    [Fact]
    public void Wilcoxon_exact_boundary_and_large_ties_match_independent_normal_reference()
    {
        var exact = StatisticalTests.Wilcoxon(Enumerable.Repeat(1.0, 25).ToArray(), new double[25]);
        Assert.Equal("exact-sign-permutation-midranks", exact.Method);
        Assert.Equal(2.0 / 33554432, exact.PValue, 14);
        var normal = StatisticalTests.Wilcoxon(Enumerable.Repeat(1.0, 26).ToArray(), new double[26]);
        Assert.Equal("normal-tie-corrected", normal.Method);
        Assert.Equal(3.4141735772975323e-7, normal.PValue, 14);
        Assert.Equal(Math.Sqrt(26), normal.Z, 12);
        Assert.Equal(1, normal.R, 12);
    }

    [Fact]
    public void Kappa_matches_known_three_category_contingency_table()
    {
        var result = StatisticalTests.Kappa([0, 0, 1, 1, 2, 2], [0, 1, 0, 1, 1, 2]);
        Assert.Equal(6, result.Ratings);
        Assert.Equal(0.25, result.Kappa!.Value, 12);
        Assert.Equal(0.4, result.LinearWeightedKappa!.Value, 12);
        var constant = StatisticalTests.Kappa([2, 2], [2, 2]);
        Assert.Null(constant.Kappa);
        Assert.Null(constant.LinearWeightedKappa);
        Assert.Null(StatisticalTests.Kappa([], []).Kappa);
    }

    [Fact]
    public void Invalid_statistical_inputs_are_rejected_and_quantiles_use_nearest_rank()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => StatisticalTests.McNemar(-1, 0));
        Assert.Throws<ArgumentException>(() => StatisticalTests.Wilcoxon([double.NaN], [0]));
        Assert.Throws<ArgumentException>(() => StatisticalTests.Wilcoxon([1], []));
        Assert.Throws<ArgumentException>(() => StatisticalTests.Kappa([3], [0]));
        Assert.Equal(2.5, StatisticalTests.Median([4, 1, 3, 2]));
        Assert.Equal(19, StatisticalTests.P95(Enumerable.Range(1, 20).Select(i => (double)i)));
        Assert.Null(StatisticalTests.Median([]));
    }
}
