extern alias RagEval;

using System.Text.Json;
using ClosedXML.Excel;
using JudgeDiagnose = RagEval::ClinicManagement.AI.RagEval.JudgeDiagnose;

namespace ClinicManagement.IntegrationTests;

public sealed class RagJudgeDiagnoseTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Diagnose_aggregates_latest_checkpoint_rows_without_exporting_private_content(bool includeRetrieval)
    {
        var directory = Directory.CreateTempSubdirectory("rag-judge-diagnose-").FullName;
        try
        {
            var checkpoint = Path.Combine(directory, "judge-checkpoint.jsonl");
            var master = Path.Combine(directory, "master.xlsx");
            WriteMaster(master, includeRetrieval);
            var entries = new object[]
            {
                new { version = 1, id = Guid.NewGuid(), inputSha256 = "test", outputFile = "grades.xlsx",
                    model = "fake", runs = 2, seed = 42, startedAtUtc = DateTimeOffset.UtcNow },
                Row(4, Grade(null, 0, 3, 2), Grade(null, 0, 0, 2)),
                Row(5, Grade(null, 0, 1, 2), Grade(1, 2)),
                Row(6, Grade(null, 1, 2, 3), Grade(null, 0, 3, 2)),
                Row(7, Grade(null, 0, 4, 2), Grade(null, 1, citationError: true)),
                Row(8, Grade(0, 2), Grade(2, 2)),
                Row(4, Grade(2, 2), Grade(null, 1, 0, 4))
            };
            await File.WriteAllTextAsync(checkpoint, string.Join('\n', entries.Select(entry => JsonSerializer.Serialize(entry))) +
                "\n{\"note\":\"PRIVATE_TORN");
            var originalCheckpoint = await File.ReadAllBytesAsync(checkpoint);
            var originalMaster = await File.ReadAllBytesAsync(master);
            var console = new StringWriter();
            await JudgeDiagnose.RunAsync(["--checkpoint", checkpoint, "--master", master], console);

            var report = await File.ReadAllTextAsync(Path.Combine(directory, "judge-diagnose.json"));
            using var json = JsonDocument.Parse(report);
            var root = json.RootElement;
            Assert.Equal(5, root.GetProperty("questions").GetInt32());
            var categories = root.GetProperty("byCategory");
            var within = categories.GetProperty("Trong phạm vi");
            AssertCounts(within.GetProperty("a"), 1, 0);
            AssertCounts(within.GetProperty("b"), 0, 1);
            AssertCounts(categories.GetProperty("Khẩn cấp / an toàn").GetProperty("a"), 1, 0);
            AssertCounts(categories.GetProperty("Ngoài phạm vi xa").GetProperty("b"), 0, 1);
            var retrieval = root.GetProperty("byRetrieval");
            AssertCounts(retrieval.GetProperty("Có tài liệu"), includeRetrieval ? 3 : 0, includeRetrieval ? 1 : 0);
            AssertCounts(retrieval.GetProperty("Trả rỗng"), includeRetrieval ? 1 : 0, includeRetrieval ? 3 : 0);
            AssertCounts(retrieval.GetProperty("Không rõ"), includeRetrieval ? 0 : 4, includeRetrieval ? 2 : 6);
            var modes = root.GetProperty("byMode");
            AssertCounts(modes.GetProperty("LLM"), 3, 2);
            AssertCounts(modes.GetProperty("Trích đoạn"), 1, 4);
            var failures = root.GetProperty("byFailure");
            var labels = new[] { "Trích dẫn không khớp nguyên văn tài liệu", "Không có tài liệu để trích",
                "Thiếu trường trích dẫn", "Lỗi JSON-schema", "Khác", "Không rõ" };
            foreach (var label in labels) Assert.Equal(1, failures.GetProperty(label).GetProperty("blank").GetInt32());
            Assert.Equal(4, failures.GetProperty(labels[0]).GetProperty("averageRetries").GetDouble());
            Assert.Equal(JsonValueKind.Null, failures.GetProperty("Không rõ").GetProperty("averageRetries").ValueKind);
            Assert.Equal(1, failures.GetProperty("Không rõ").GetProperty("citationErrorFlagged").GetInt32());
            var total = root.GetProperty("totalFailures");
            Assert.Equal(5, total.GetProperty("retrySamples").GetInt32());
            Assert.Equal(2.6, total.GetProperty("averageRetries").GetDouble());
            Assert.Contains("Trong phạm vi | 1 | 0 | 0 | 1", console.ToString());
            Assert.DoesNotContain("PRIVATE_", console.ToString());
            Assert.DoesNotContain("PRIVATE_", report);
            Assert.Equal(originalCheckpoint, await File.ReadAllBytesAsync(checkpoint));
            Assert.Equal(originalMaster, await File.ReadAllBytesAsync(master));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void AssertCounts(JsonElement element, int scored, int blank)
    {
        Assert.Equal(scored, element.GetProperty("scored").GetInt32());
        Assert.Equal(blank, element.GetProperty("blank").GetInt32());
    }

    private static object Row(int row, object a, object b) => new { row, a, b, elapsedMs = 1 };

    private static object Grade(int? score, int runsCompleted, int? failure = null, int retries = 0, bool citationError = false) =>
        new { score, runsCompleted, citationError, note = "PRIVATE_NOTE", diagnostics = failure.HasValue
            ? new { retryCount = retries, finalFailure = failure } : null };

    private static void WriteMaster(string path, bool includeRetrieval)
    {
        using var workbook = new XLWorkbook();
        var questions = workbook.AddWorksheet("Câu hỏi");
        var grades = workbook.AddWorksheet("Phiếu chấm");
        var key = workbook.AddWorksheet("Khóa");
        var categories = new[] { "Trong phạm vi", "Diễn đạt khác", "Ngoài phạm vi gần", "Ngoài phạm vi xa", "Khẩn cấp / an toàn" };
        if (includeRetrieval) questions.Cell(2, 7).Value = "Retrieval có tài liệu";
        for (var i = 0; i < categories.Length; i++)
        {
            var row = i + 4;
            var stt = i + 1;
            questions.Cell(row, 1).Value = stt;
            questions.Cell(row, 3).Value = "PRIVATE_QUESTION";
            questions.Cell(row, 4).Value = categories[i];
            questions.Cell(row, 5).Value = "PRIVATE_EXPECTED_DOCUMENT";
            if (includeRetrieval && i != 3) questions.Cell(row, 7).Value = i is 0 or 4;
            grades.Cell(row, 1).Value = stt;
            grades.Cell(row, 4).Value = "PRIVATE_ANSWER";
            key.Cell(8 - i, 1).Value = stt;
            key.Cell(8 - i, 2).Value = i % 2 == 0 ? "LLM" : "Trích đoạn";
        }
        workbook.SaveAs(path);
    }
}
