using System.Diagnostics;
using ClinicManagement.Infrastructure.AI;
using Xunit;

namespace ClinicManagement.IntegrationTests;

public sealed class AiPatientInputScreeningTests
{
    [Fact]
    public void Long_plain_text_finishes_promptly_with_no_PII()
    {
        var text = new string('a', 200_000);
        var timer = Stopwatch.StartNew();
        Assert.False(AiPatientInputScreening.ContainsPii(text));
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(5), "PII screening exceeded the generous time budget.");
    }

    [Theory]
    [InlineData("Khám tổng quát định kỳ", false)]
    [InlineData("synthetic@example.com", true)]
    [InlineData("Liên hệ 0912345678", true)]
    [InlineData("Liên hệ +84912345678", true)]
    [InlineData("123456789", true)]
    [InlineData("123456789012", true)]
    [InlineData("12345678 hoặc 1234567890", false)]
    public void Ordinary_inputs_keep_existing_PII_semantics(string text, bool expected)
        => Assert.Equal(expected, AiPatientInputScreening.ContainsPii(text));
}
