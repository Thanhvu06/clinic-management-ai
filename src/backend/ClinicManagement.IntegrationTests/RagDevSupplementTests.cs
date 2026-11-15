extern alias RagEval;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using RagEval::ClinicManagement.AI.RagEval;

namespace ClinicManagement.IntegrationTests;

public sealed class RagDevSupplementTests : IDisposable
{
    private const string Author = "Claude (chat), chủ nhiệm duyệt";
    private const string Header = "STT,Người viết,Câu hỏi,Loại,Tài liệu đúng,Ý chính cần có\n";
    private const string HiddenMarker = "synthetic-hidden-marker";
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("rag-dev-supplement-");

    [Fact]
    public async Task CandidateOverlapReportsGroupTotalsAndWritesOnlyRemovedCandidateCodes()
    {
        var candidates = await WriteCsvAsync(Row("TEST-A", "a test question", DevQuestions.Near) +
            Row("TEST-B", "five different test words here", DevQuestions.Emergency, "emergency"));
        var removed = Path.Combine(_directory.FullName, "removed.json");
        var hidden = CreateSyntheticWorkbook();
        using var output = new StringWriter();
        using var errors = new StringWriter();

        var exit = await DevCalibrationCommands.RunAsync("dev-overlap",
            ["--candidates", candidates, "--hidden-eval", hidden, "--removed", removed], output, errors,
            texts => Task.FromResult(texts.Select(text => text is "a test question" or HiddenMarker
                ? new float[] { 1, 0 } : new float[] { 0, 1 }).ToArray()));

        Assert.Equal(0, exit);
        Assert.Empty(errors.ToString());
        Assert.Equal(new[] { "TEST-A" }, JsonSerializer.Deserialize<string[]>(await File.ReadAllTextAsync(removed)));
        Assert.Contains($"{DevQuestions.Near}: 1 câu, 1 vượt ngưỡng", output.ToString());
        Assert.Contains($"{DevQuestions.Emergency}: 1 câu, 0 vượt ngưỡng", output.ToString());
        Assert.Equal(5, output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.DoesNotContain(HiddenMarker, output.ToString());
        Assert.DoesNotContain("a test question", output.ToString());
        Assert.DoesNotContain("TEST-A", output.ToString());
    }

    [Fact]
    public async Task LegacyOverlapUsesFrozenQuestionsAndWritesRetainedCodes()
    {
        var retained = Path.Combine(_directory.FullName, "retained.json");
        using var output = new StringWriter();
        using var errors = new StringWriter();

        var exit = await DevCalibrationCommands.RunAsync("dev-overlap",
            ["--hidden-eval", CreateSyntheticWorkbook(), "--retained", retained], output, errors,
            texts => Task.FromResult(texts.Select(text => text == HiddenMarker
                ? new float[] { 1, 0 } : new float[] { 0, 1 }).ToArray()));

        Assert.Equal(0, exit);
        Assert.Empty(errors.ToString());
        var frozen = await DevQuestions.LoadRq2Async();
        Assert.Equal(frozen.Select(q => q.Stt), JsonSerializer.Deserialize<string[]>(await File.ReadAllTextAsync(retained)));
        Assert.Contains("Histogram cosine", output.ToString());
        Assert.DoesNotContain(HiddenMarker, output.ToString());
    }

    [Fact]
    public async Task FrozenQuestionDoesNotRequireTheSupplement()
    {
        var frozen = (await DevQuestions.LoadRq2Async())[0];
        var path = await WriteCsvAsync(Row(frozen.Stt!, frozen.Text, frozen.Category!, frozen.ExpectedDocId));

        var result = await DevBlindProvenance.LoadDevCsvAsync(path,
            () => throw new InvalidOperationException("The supplement must not be loaded."));

        Assert.Equal(frozen.Stt, Assert.Single(result).Stt);
    }

    [Fact]
    public async Task DevCsvAcceptsTheVerifiedSupplement()
    {
        var row = Row("TEST-A", "Registered question", DevQuestions.Near);
        var path = await WriteCsvAsync(row);

        var result = await DevBlindProvenance.LoadDevCsvAsync(path,
            () => Task.FromResult(DevBlindProvenance.ParseSupplement(
                (Header + row).Replace("\n", "\r\n", StringComparison.Ordinal), Hash(Header + row))));

        Assert.Equal("TEST-A", Assert.Single(result).Stt);
    }

    [Fact]
    public async Task DevCsvRejectsSupplementWithWrongSha()
    {
        var row = Row("TEST-A", "Registered question", DevQuestions.Near);
        var path = await WriteCsvAsync(row);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => DevBlindProvenance.LoadDevCsvAsync(path,
            () => Task.FromResult(DevBlindProvenance.ParseSupplement(Header + row, Hash(Header)))));

        Assert.Contains("SHA-256", error.Message);
    }

    [Theory]
    [InlineData("TEST-B", "Unregistered question")]
    [InlineData("TEST-A", "Changed question")]
    public async Task DevCsvRejectsQuestionsOutsideBothSources(string code, string question)
    {
        var supplement = Header + Row("TEST-A", "Registered question", DevQuestions.Near);
        var path = await WriteCsvAsync(Row(code, question, DevQuestions.Near));

        await Assert.ThrowsAsync<InvalidDataException>(() => DevBlindProvenance.LoadDevCsvAsync(path,
            () => Task.FromResult(DevBlindProvenance.ParseSupplement(supplement, Hash(supplement)))));
    }

    [Fact]
    public void SupplementRejectsUnregisteredAuthorEvenWithMatchingSha()
    {
        var input = Header + Row("TEST-A", "Registered question", DevQuestions.Near)
            .Replace(Author, "Different author", StringComparison.Ordinal);

        Assert.Throws<InvalidDataException>(() => DevBlindProvenance.ParseSupplement(input, Hash(input)));
    }

    private string CreateSyntheticWorkbook()
    {
        var path = Path.Combine(_directory.FullName, "synthetic.xlsx");
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Câu hỏi");
        sheet.Cell(4, 1).Value = "SYNTHETIC";
        sheet.Cell(4, 3).Value = HiddenMarker;
        workbook.SaveAs(path);
        return path;
    }

    private async Task<string> WriteCsvAsync(string rows)
    {
        var path = Path.Combine(_directory.FullName, "dev.csv");
        await File.WriteAllTextAsync(path, Header + rows);
        return path;
    }

    private static string Row(string code, string text, string category, string? doc = null) =>
        string.Join(',', new[] { code, Author, text, category, doc ?? "", "" }.Select(BlindAnswerExport.Cell)) + "\n";

    private static string Hash(string content) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

    public void Dispose()
    {
        foreach (var file in _directory.EnumerateFiles())
            file.Delete();
        _directory.Delete();
    }
}
