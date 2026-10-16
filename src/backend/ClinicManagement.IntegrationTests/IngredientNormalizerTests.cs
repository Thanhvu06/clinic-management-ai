using ClinicManagement.Application.Prescriptions;
using Xunit;

namespace ClinicManagement.IntegrationTests;

public class IngredientNormalizerTests
{
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
