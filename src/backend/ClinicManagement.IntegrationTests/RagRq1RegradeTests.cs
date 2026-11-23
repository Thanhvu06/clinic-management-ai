extern alias RagEval;

using System.Security.Cryptography;
using System.Text.Json;
using ClosedXML.Excel;
using RagEval::ClinicManagement.AI.RagEval;

namespace ClinicManagement.IntegrationTests;

public class RagRq1RegradeTests
{
    private const string HiddenText = "SYNTHETIC_PRIVATE_CONTENT_DO_NOT_EXPORT";
    private static readonly Rq1Checklist Full = new("Đúng", "Đủ", "Không", "Không", "Đạt");
    private static readonly Rq1Checklist Partial = Full with { C2 = "Thiếu một phần" };
    private static readonly Rq1Checklist Wrong = Full with { C1 = "Sai" };

    [Fact]
    public void Mapping_keeps_sides_and_reuses_PR184_hand_calculated_agreement()
    {
        using var first = new XLWorkbook();
        using var repeat = new XLWorkbook();
        var left = first.AddWorksheet("Phiếu chấm");
        var right = repeat.AddWorksheet("Phiếu chấm");
        WriteRow(left, 4, "141", Full, Full);
        WriteRow(left, 5, "2", Wrong, Partial);
        WriteRow(right, 4, "CL02", Partial, Full);
        WriteRow(right, 5, "CL01", Wrong, Full);
        var result = Compare(left, right, [new("CL01", "2"), new("CL02", "141")]);
        Assert.Equal(4, result.Agreement.Overall.N);
        Assert.Equal(2, result.MatchingScores);
        Assert.Equal(50, result.Agreement.Overall.PercentAgreement);
        Assert.Equal(0.2, result.Agreement.Overall.Kappa!.Value, 12);
        Assert.Equal(3.0 / 7, result.Agreement.Overall.LinearWeightedKappa!.Value, 12);
        Assert.Collection(result.Differences,
            difference => { Assert.Equal("CL01", difference.Cl); Assert.Equal("B", difference.Side); },
            difference => { Assert.Equal("CL02", difference.Cl); Assert.Equal("A", difference.Side); });
    }

    [Fact]
    public void C2_equivalence_depends_on_ideas_and_item_differences_need_not_change_score()
    {
        using var first = new XLWorkbook();
        using var repeat = new XLWorkbook();
        var left = first.AddWorksheet("Phiếu chấm");
        var right = repeat.AddWorksheet("Phiếu chấm");
        var notApplicable = Full with { C2 = "Không áp dụng" };
        WriteRow(left, 4, "1", Full, Full);
        WriteRow(left, 5, "2", Full, Full);
        WriteRow(right, 4, "CL01", notApplicable, notApplicable);
        WriteRow(right, 5, "CL02", notApplicable, notApplicable);
        left.Cell(4, 6).Clear();
        right.Cell(4, 6).Clear();
        var result = Compare(left, right, [new("CL01", "1"), new("CL02", "2")]);
        Assert.Equal(100, result.Agreement.Overall.PercentAgreement);
        Assert.Null(result.Agreement.Overall.LinearWeightedKappa);
        Assert.Equal(50, result.Agreement.Items["C2"].PercentAgreement);
        Assert.Equal(2, result.Differences.Count);
        Assert.All(result.Differences, difference =>
        {
            Assert.Equal("CL02", difference.Cl);
            Assert.Equal(new[] { "C2" }, difference.Items);
            Assert.Equal(difference.FirstScore, difference.RepeatScore);
        });
    }

    [Fact]
    public void Invalid_or_incomplete_side_is_reported_without_discarding_the_valid_other_side()
    {
        using var first = new XLWorkbook();
        using var repeat = new XLWorkbook();
        var left = first.AddWorksheet("Phiếu chấm");
        var right = repeat.AddWorksheet("Phiếu chấm");
        WriteRow(left, 4, "1", Full, Full);
        WriteRow(right, 4, "CL01", Full, Full);
        right.Cell(4, 10).Value = "invalid";
        right.Cell(4, 11).Clear();
        var result = Compare(left, right, [new("CL01", "1")]);
        Assert.Equal(2, result.RequestedRatings);
        Assert.Equal(1, result.Agreement.Overall.N);
        Assert.Equal(new[] { "CL01/A" }, result.UnavailableRatings);
        Assert.Null(Assert.Single(result.Differences).RepeatScore);
        var issues = Rq1MechanicalAudit.Check(right, 4, new("CL01", "1"));
        Assert.Contains(issues, issue => issue.Rule == "a" && issue.Item == "C2" && issue.Side == "A");
        Assert.Contains(issues, issue => issue.Rule == "b" && issue.Item == "C1" && issue.Side == "A");
    }

    [Theory]
    [InlineData("a")]
    [InlineData("b")]
    [InlineData("c")]
    [InlineData("d")]
    [InlineData("e")]
    [InlineData("f")]
    [InlineData("g")]
    [InlineData("h")]
    [InlineData("i")]
    [InlineData("j")]
    [InlineData("k")]
    public void Mechanical_a_through_k_flags_match_the_locked_review_protocol(string rule)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Phiếu chấm");
        WriteRow(sheet, 4, "CL01", Full, Full);
        Assert.Empty(Rq1MechanicalAudit.Check(sheet, 4, new("CL01", "141")));
        switch (rule)
        {
            case "a": sheet.Cell(4, 10).Clear(); break;
            case "b": sheet.Cell(4, 10).Value = "not-a-dropdown-option"; break;
            case "c": sheet.Cell(4, 17).Value = 99; break;
            case "d": sheet.Cell(4, 11).Value = "Không áp dụng"; break;
            case "e": sheet.Cell(4, 6).Clear(); break;
            case "f": sheet.Cell(4, 15).Value = "Có"; break;
            case "g": sheet.Cell(4, 15).Value = "Có"; sheet.Cell(4, 11).Value = "Thiếu một phần"; break;
            case "h": sheet.Cell(4, 5).Value = sheet.Cell(4, 4).Value; sheet.Cell(4, 21).Value = "Có"; break;
            case "i": sheet.Cell(4, 8).Clear(); break;
            case "j": sheet.Cell(4, 15).Value = "Có"; sheet.Cell(4, 10).Value = "Sai"; break;
            case "k": sheet.Cell(4, 2).Value = "Ngoài phạm vi xa"; break;
        }
        Assert.Contains(Rq1MechanicalAudit.Check(sheet, 4, new("CL01", "141")), issue => issue.Rule == rule && issue.Cl == "CL01");
    }

    [Fact]
    public void Mechanical_score_check_uses_saved_cache_without_recalculating_the_workbook()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Phiếu chấm");
        WriteRow(sheet, 4, "CL01", Full, Full);
        sheet.Cell(4, 17).Clear();
        sheet.Cell(4, 17).FormulaA1 = "1+1";
        Assert.True(sheet.Cell(4, 17).CachedValue.IsBlank);
        Assert.Contains(Rq1MechanicalAudit.Check(sheet, 4, new("CL01", "1")), issue => issue.Rule == "c");
        Assert.True(sheet.Cell(4, 17).CachedValue.IsBlank);
    }

    [Fact]
    public void Content_mismatch_stops_before_reading_codex_and_error_does_not_expose_content()
    {
        using var first = FullWorkbook(false);
        using var repeat = RepeatWorkbook();
        repeat.Worksheet(1).Cell(4, 8).Value = HiddenText + " changed";
        var called = false;
        var error = Assert.Throws<Rq1RegradeException>(() => Rq1Regrade.Analyze(first, repeat, Mapping(), () =>
        {
            called = true;
            return FullWorkbook(true);
        }));
        Assert.False(called);
        Assert.Contains("CL01, column 8", error.Message);
        Assert.DoesNotContain(HiddenText, error.Message);
    }

    [Fact]
    public void Verification_requires_exactly_299_matching_scores_out_of_300()
    {
        using var first = FullWorkbook(false);
        using var repeat = RepeatWorkbook();
        var report = Rq1Regrade.Analyze(first, repeat, Mapping(), () => FullWorkbook(true));
        Assert.Equal(30, report.MatchingContentRows);
        Assert.Equal(60, report.Stability.Agreement.Overall.N);
        Assert.Equal(59, report.Stability.MatchingScores);
        Assert.Equal(300, report.Verification.Agreement.Overall.N);
        Assert.Equal(299, report.Verification.MatchingScores);
        Assert.Empty(report.MechanicalIssues);
        Assert.Throws<Rq1RegradeException>(() => Rq1Regrade.Analyze(first, repeat, Mapping(), () => FullWorkbook(false)));
    }

    [Fact]
    public async Task Command_preserves_locked_files_and_exports_only_identifiers_and_statistics()
    {
        var directory = Directory.CreateTempSubdirectory("rq1-regrade-tests-");
        var inputs = new[] { "first.xlsx", "repeat.xlsx", "codex.xlsx", "mapping.csv" }.Select(name => Path.Combine(directory.FullName, name)).ToArray();
        try
        {
            using (var workbook = FullWorkbook(false)) workbook.SaveAs(inputs[0]);
            using (var workbook = RepeatWorkbook()) workbook.SaveAs(inputs[1]);
            using (var workbook = FullWorkbook(true)) workbook.SaveAs(inputs[2]);
            await File.WriteAllTextAsync(inputs[3], "CL,STT_goc\n" + string.Join('\n', Mapping().Select(item => $"{item.Cl},{item.Stt}")));
            foreach (var input in inputs) File.SetAttributes(input, File.GetAttributes(input) | FileAttributes.ReadOnly);
            var before = inputs.Select(Hash).ToArray();
            var output = Path.Combine(directory.FullName, "report.json");
            using var console = new StringWriter();
            await Rq1RegradeCommand.RunAsync(["--first", inputs[0], "--repeat", inputs[1], "--mapping", inputs[3], "--verify-codex", inputs[2], "--out", output], console);
            var json = await File.ReadAllTextAsync(output);
            Assert.DoesNotContain(HiddenText, json + console);
            Assert.DoesNotContain("LLM", json + console);
            Assert.Equal(before, inputs.Select(Hash));
            Assert.All(inputs, input => Assert.True(File.GetAttributes(input).HasFlag(FileAttributes.ReadOnly)));
            using var document = JsonDocument.Parse(json);
            Assert.Equal(299, document.RootElement.GetProperty("Report").GetProperty("Verification").GetProperty("MatchingScores").GetInt32());
        }
        finally
        {
            foreach (var input in inputs.Where(File.Exists)) File.SetAttributes(input, FileAttributes.Normal);
            directory.Delete(true);
        }
    }

    [Theory]
    [InlineData("CL,STT_goc\nCL02,1\n")]
    [InlineData("CL,STT_goc\nCL01,1\nCL02,1\n")]
    [InlineData("CL,STT_goc\nCL01,1\n")]
    public void Mapping_rejects_reordering_duplicates_or_missing_rows(string csv)
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, csv);
            Assert.Throws<Rq1RegradeException>(() => Rq1RegradeCommand.ReadMapping(path));
        }
        finally { File.Delete(path); }
    }

    private static Rq1RegradeComparison Compare(IXLWorksheet first, IXLWorksheet second, Rq1RegradeMapping[] mapping) =>
        Rq1Regrade.Compare(first, second, mapping, BlindAnswerWorkbook.SttRows(first), BlindAnswerWorkbook.SttRows(second));

    private static Rq1RegradeMapping[] Mapping() => Enumerable.Range(1, 30).Select(i => new Rq1RegradeMapping($"CL{i:00}", (31 - i).ToString())).ToArray();

    private static XLWorkbook FullWorkbook(bool codex)
    {
        var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Phiếu chấm");
        for (var i = 1; i <= 150; i++) WriteRow(sheet, i + 3, i.ToString(), Full, codex && i == 1 ? Partial : Full);
        return workbook;
    }

    private static XLWorkbook RepeatWorkbook()
    {
        var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Phiếu chấm");
        for (var i = 1; i <= 30; i++) WriteRow(sheet, i + 3, $"CL{i:00}", Full, i == 1 ? Partial : Full);
        return workbook;
    }

    private static void WriteRow(IXLWorksheet sheet, int row, string identifier, Rq1Checklist a, Rq1Checklist b)
    {
        sheet.Cell(row, 1).Value = identifier;
        sheet.Cell(row, 2).Value = "Trong phạm vi";
        for (var column = 3; column <= 9; column++) sheet.Cell(row, column).Value = HiddenText + column;
        WriteSide(10, a);
        WriteSide(19, b);

        void WriteSide(int start, Rq1Checklist checklist)
        {
            var values = new[] { checklist.C1, checklist.C2, checklist.C3, checklist.C4, checklist.C5, "Không" };
            for (var i = 0; i < values.Length; i++)
            {
                sheet.Cell(row, start + i).Value = values[i];
                Rq1Workbook.Dropdown(sheet.Range(row, start + i, row, start + i), i == 5 ? ["Có", "Không"] : Rq1Checklist.Options[i]);
            }
            sheet.Cell(row, start + 7).Value = checklist.Score;
        }
    }

    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
