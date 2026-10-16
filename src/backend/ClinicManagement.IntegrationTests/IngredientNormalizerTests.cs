using ClinicManagement.Application.Prescriptions;
using Xunit;

namespace ClinicManagement.IntegrationTests;

public class IngredientNormalizerTests
{
    [Theory]
    [InlineData("penicillin", "penicillin", true)]
    [InlineData("Penicillin G", "penicillin", true)]
    [InlineData("kháng sinh nhóm penicillin", "PENICILLIN", true)]
    [InlineData("dị ứng ibuprofen nặng", "ibuprofen", true)]
    [InlineData("DỊ ỨNG\tTHUỐC CHỐNG VIÊM\nKHÔNG STEROID", "thuoc chong viem khong steroid", true)]
    [InlineData("nhom thuoc chong viem khong steroid", "Thuốc chống viêm không steroid", true)]
    [InlineData("calci carbonat", "calci carbonat", true)]
    [InlineData("dị ứng calci carbonat nặng", "calci carbonat", true)]
    [InlineData("amoxicillin", "moxicillin", false)]
    [InlineData("penicillins", "penicillin", false)]
    [InlineData("xpenicillin penicillinx", "penicillin", false)]
    [InlineData("xpenicillin penicillin G", "penicillin", true)]
    [InlineData("calci carbonatx", "calci carbonat", false)]
    [InlineData("penicillin-G", "penicillin", false)]
    [InlineData("", "penicillin", false)]
    [InlineData("penicillin", "", false)]
    [InlineData("penicillin", "  ", false)]
    [InlineData(null, "penicillin", false)]
    [InlineData("penicillin", null, false)]
    public void ContainsKeyAsWordNormalizesTextAndRequiresWholePhraseBoundaries(string? text, string? key, bool expected) =>
        Assert.Equal(expected, IngredientNormalizer.ContainsKeyAsWord(text, key));

    [Theory]
    [InlineData("  ĐƯỜNG\tHUYẾT  ", "duong huyet")]
    [InlineData("Nhôm Phosphat", "nhom phosphat")]
    [InlineData(" KHÁNG  SINH\nPenicillin ", "khang sinh penicillin")]
    [InlineData("a\u0301", "a")]
    [InlineData(null, "")]
    [InlineData("", "")]
    public void NormalizeKeyRemovesVietnameseAccentsAndCollapsesWhitespace(string? text, string expected) => Assert.Equal(expected, IngredientNormalizer.NormalizeKey(text));

    [Fact]
    public void SplitIngredientsHandlesAllSeparatorsAndPreservesMultiwordIngredients()
    {
        Assert.Equal(new[] { "amoxicillin", "calci carbonat", "losartan kali", "nhom phosphat", "ibuprofen" },
            IngredientNormalizer.SplitIngredients(" Amoxicillin, Calci carbonat + Losartan kali / Nhôm phosphat VÀ Ibuprofen, amoxicillin "));
        Assert.Empty(IngredientNormalizer.SplitIngredients(null));
    }

    [Theory]
    [InlineData("losartan kali", "losartan", true)]
    [InlineData("calci carbonat", "calci", true)]
    [InlineData("ibuprofen", "ibuprofen", true)]
    [InlineData("ibuprofen", "ibu", false)]
    [InlineData("losartankali", "losartan", false)]
    [InlineData("ibuprofen", "", false)]
    public void MatchesKeyRequiresAnExactKeyOrWordBoundary(string ingredient, string key, bool expected) => Assert.Equal(expected, IngredientNormalizer.MatchesKey(ingredient, key));
}
