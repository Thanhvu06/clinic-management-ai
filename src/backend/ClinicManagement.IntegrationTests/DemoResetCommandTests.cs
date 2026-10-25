using ClinicManagement.Api;
using ClinicManagement.Infrastructure.Common;

namespace ClinicManagement.IntegrationTests;

public class DemoResetCommandTests
{
    [Theory]
    [InlineData("Production", "ClinicCareDemoLocal")]
    [InlineData("Testing", "ClinicCareDemoLocal")]
    [InlineData("Development", "ClinicManagementDb")]
    [InlineData("Development", "OtherClinicCareDemo")]
    [InlineData("Development", "")]
    public void ResetRefusesUnsafeTargets(string environment, string database) =>
        Assert.Throws<InvalidOperationException>(() => DemoResetCommand.Validate(environment, $"Server=localhost;Database={database};Integrated Security=true"));

    [Fact]
    public void ResetAcceptsOnlyDevelopmentDemoTarget()
    {
        var target = DemoResetCommand.Validate("Development", "Server=localhost;Database=ClinicCareDemoBrand_20261024c;Integrated Security=true");
        Assert.Equal("localhost", target.DataSource);
        Assert.Equal("ClinicCareDemoBrand_20261024c", target.InitialCatalog);
    }

    [Theory]
    [InlineData("APT", 8)]
    [InlineData("PAY", 8)]
    [InlineData("INV", 7)]
    [InlineData("REG", 7)]
    public void RandomDocumentCodesHaveFullSuffix(string prefix, int length) =>
        Assert.Matches($@"^{prefix}-261025-[0-9A-F]{{{length}}}$", BusinessDisplay.RandomCode(prefix, new DateOnly(2026, 10, 25), length));

    [Fact]
    public void MoneyUsesVietnameseSeparatorsIndependentOfServerCulture()
    {
        using var culture = new CultureScope("en-US");
        Assert.Equal("570.000 ₫", BusinessDisplay.Money(570000m));
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly System.Globalization.CultureInfo previous = System.Globalization.CultureInfo.CurrentCulture;
        public CultureScope(string name) => System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo(name);
        public void Dispose() => System.Globalization.CultureInfo.CurrentCulture = previous;
    }
}
