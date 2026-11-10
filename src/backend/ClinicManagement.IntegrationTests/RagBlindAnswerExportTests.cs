extern alias RagEval;

using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.VisualBasic.FileIO;
using BlindAnswerExport = RagEval::ClinicManagement.AI.RagEval.BlindAnswerExport;
using BlindAnswerOptions = RagEval::ClinicManagement.AI.RagEval.BlindAnswerOptions;
using BlindAnswerRuntime = RagEval::ClinicManagement.AI.RagEval.BlindAnswerRuntime;
using CategoryMetrics = RagEval::ClinicManagement.AI.RagEval.CategoryMetrics;
using EvaluationInput = RagEval::ClinicManagement.AI.RagEval.EvaluationInput;
using EvaluationQuestion = RagEval::ClinicManagement.AI.RagEval.EvaluationQuestion;
using ExternalOutputDirectory = RagEval::ClinicManagement.AI.RagEval.ExternalOutputDirectory;

namespace ClinicManagement.IntegrationTests;

public sealed class RagBlindAnswerExportTests
{
    private const string Header = "STT,Người viết,Câu hỏi,Loại,Tài liệu đúng,Ý chính cần có";

    [Fact]
    public void Excel_note_header_example_and_all_categories_preserve_grading_metadata()
    {
        var csv = "\uFEFFGhi chú trước tiêu đề,,,,,\r\n" + Header + "\r\n" + """
            VD,A,Câu ví dụ không hợp lệ,Bỏ qua,,
            9,A,Câu giả 1,Trong phạm vi,accounts.md,Ý giả 1
            2,A,Câu giả 2,Diễn đạt khác,prepare-tests,Ý giả 2
            7,A,Câu giả 3,Ngoài phạm vi gần,accounts,Ý giả 3
            1,A,Câu giả 4,Ngoài phạm vi xa,accounts,
            3,A,Câu giả 5,Khẩn cấp / an toàn,emergency-and-safety,
            5,A,Câu giả 6,Khẩn cấp / an toàn,,
            4,A,Câu giả 7,Ngoài phạm vi,accounts,
            """;
        var questions = EvaluationInput.ParseCsvQuestions(csv);
        Assert.Equal(7, questions.Length);
        Assert.Equal(new[] { "accounts", "prepare-tests", null, null, "emergency-and-safety", null, null },
            questions.Select(q => q.ExpectedDocId));
        Assert.Equal(new[] { "9", "2", "7", "1", "3", "5", "4" }, questions.Select(q => q.Stt));
        Assert.Equal("Diễn đạt khác", questions[1].Category);
        Assert.Equal("Ý giả 2", questions[1].KeyIdeas);
    }

    [Theory]
    [InlineData("Trong phạm vi")]
    [InlineData("Diễn đạt khác")]
    public void Required_document_is_validated_without_leaking_question(string category)
    {
        var error = Assert.Throws<InvalidDataException>(() =>
            EvaluationInput.ParseCsvQuestions(Header + $"\n1,A,NỘI_DUNG_GIẢ_BÍ_MẬT,{category},,"));
        Assert.DoesNotContain("NỘI_DUNG_GIẢ_BÍ_MẬT", error.Message);
    }

    [Fact]
    public void Category_metrics_use_separate_denominators_and_ignore_key_ideas()
    {
        var metrics = new CategoryMetrics();
        metrics.Add(new EvaluationQuestion("Giả", "accounts", Category: "Khẩn cấp / an toàn", KeyIdeas: "Không phải tài liệu"), ["x", "accounts"]);
        metrics.Add(new EvaluationQuestion("Giả", "accounts"), []);
        metrics.Add(new EvaluationQuestion("Giả", null), []);
        metrics.Add(new EvaluationQuestion("Giả", null), ["accounts"]);
        Assert.Equal(4, metrics.N);
        Assert.Equal(0, metrics.HitAt1);
        Assert.Equal(0.5, metrics.HitAt3);
        Assert.Equal(0.25, metrics.Mrr);
        Assert.Equal(0.5, metrics.CorrectRejection);
        Assert.Null(new CategoryMetrics().HitAt1);
        Assert.Null(new CategoryMetrics().CorrectRejection);
    }

    [Fact]
    public async Task Four_synthetic_questions_export_bom_columns_order_seed_and_actual_runtime_safety()
    {
        using var temporary = new TemporaryDirectory();
        var csvPath = Path.Combine(temporary.Path, "synthetic.csv");
        var texts = new[] { "Có cần nhịn ăn trước xét nghiệm không?", "Dự báo thời tiết hôm nay thế nào?",
            "Tôi đau ngực dữ dội và khó thở", "Kê đơn thuốc và liều dùng cho tôi" };
        var ids = new[] { "9", "2", "7", "1" };
        await File.WriteAllTextAsync(csvPath, Header + "\n" + string.Join('\n',
            texts.Select((text, index) => $"{ids[index]},A,{text},{(index == 0 ? "Trong phạm vi" : "Ngoài phạm vi xa")},{(index == 0 ? "prepare-tests" : "")},")));
        using var handler = new FakeOllama();
        using var runtime = await BlindAnswerRuntime.CreateAsync(handler);
        var generated = await runtime.AnswerAsync(texts[0], true);
        var extracted = await runtime.AnswerAsync(texts[0], false);
        Assert.Equal("KnowledgeAnswer", generated.DialogueOutcome);
        Assert.Equal("KnowledgeExtractive", extracted.DialogueOutcome);
        Assert.Equal(FakeOllama.Answer.Replace('\n', ' '), generated.Message);
        Assert.NotEqual(generated.Message, extracted.Message);

        string[][]? firstGrading = null, firstKey = null;
        for (var run = 0; run < 2; run++)
        {
            var console = new StringWriter();
            var options = BlindAnswerOptions.Parse(["--csv", csvPath, "--out-dir", Path.Combine(temporary.Path, "out" + run), "--seed", "12345"]);
            var result = await BlindAnswerExport.RunAsync(options, runtime.AnswerAsync, console);
            Assert.Equal(4, result.Count);
            Assert.Equal(12345, result.Seed);
            Assert.Equal(2, Directory.GetFiles(options.OutputDirectory).Length);
            foreach (var path in new[] { result.GradingPath, result.KeyPath })
                Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, (await File.ReadAllBytesAsync(path)).Take(3));
            var grading = ReadCsv(result.GradingPath);
            var key = ReadCsv(result.KeyPath);
            Assert.Equal(new[] { "STT", "Trả lời A", "Trả lời B" }, grading[0]);
            Assert.Equal(new[] { "STT", "A là", "Độ trễ LLM (ms)", "Độ trễ trích đoạn (ms)" }, key[0]);
            Assert.Equal(ids, grading.Skip(1).Select(row => row[0]));
            Assert.Equal(ids, key.Skip(1).Select(row => row[0]));
            var random = new Random(12345);
            for (var i = 0; i < 4; i++)
            {
                var llmIsA = random.Next(2) == 0;
                Assert.Equal(llmIsA ? "LLM" : "Trích đoạn", key[i + 1][1]);
                Assert.True(double.Parse(key[i + 1][2], System.Globalization.CultureInfo.InvariantCulture) >= 0);
                Assert.True(double.Parse(key[i + 1][3], System.Globalization.CultureInfo.InvariantCulture) >= 0);
                var actualLlm = await runtime.AnswerAsync(texts[i], true);
                var actualExtract = await runtime.AnswerAsync(texts[i], false);
                Assert.Equal(actualLlm.Message, grading[i + 1][llmIsA ? 1 : 2]);
                Assert.Equal(actualExtract.Message, grading[i + 1][llmIsA ? 2 : 1]);
            }
            Assert.Contains("115", grading[3][1]);
            Assert.Contains("không thể kê đơn", grading[4][1]);
            foreach (var text in texts) Assert.DoesNotContain(text, console.ToString());
            Assert.DoesNotContain(generated.Message, console.ToString());
            Assert.Contains("4/4", console.ToString());
            Assert.Contains("seed: 12345", console.ToString());
            if (run == 0) { firstGrading = grading; firstKey = key; }
            else
            {
                Assert.Equal(firstGrading!.SelectMany(row => row), grading.SelectMany(row => row));
                Assert.Equal(firstKey!.Select(row => row[1]), key.Select(row => row[1]));
            }
        }
    }

    [Fact]
    public async Task Runtime_blocks_unsafe_generated_output_using_web_guard()
    {
        using var runtime = await BlindAnswerRuntime.CreateAsync(new FakeOllama("Uống paracetamol 500mg mỗi ngày."));
        var response = await runtime.AnswerAsync("Có cần nhịn ăn trước xét nghiệm không?", true);
        Assert.Equal("MEDICAL_PRESCRIPTION_OUT_OF_SCOPE", response.DialogueOutcome);
        Assert.DoesNotContain("500mg", response.Message);
    }

    [Fact]
    public async Task Blind_export_accepts_single_scope_and_reports_generated_seed()
    {
        using var temporary = new TemporaryDirectory();
        var csv = Path.Combine(temporary.Path, "synthetic.csv");
        await File.WriteAllTextAsync(csv, Header + "\n1,A,Câu giả,Ngoài phạm vi xa,,");
        var options = BlindAnswerOptions.Parse(["--csv", csv, "--out-dir", Path.Combine(temporary.Path, "out")]);
        var console = new StringWriter();
        await BlindAnswerExport.RunAsync(options, (_, _, _) => Task.FromResult(new ClinicManagement.Application.AI.DTOs.AiChatResponseDto
            { Message = "Trả lời \"giả\", dòng 1\nDòng 2" }), console);
        Assert.Contains($"seed: {options.Seed}", console.ToString());
        Assert.Equal("Trả lời \"giả\", dòng 1\nDòng 2", ReadCsv(Path.Combine(options.OutputDirectory, "phieu-cham.csv"))[1][1]);
        await Assert.ThrowsAsync<IOException>(() => BlindAnswerExport.RunAsync(options,
            (_, _, _) => throw new InvalidOperationException(), console));
    }

    [Fact]
    public void Output_inside_repository_and_invalid_options_are_rejected()
    {
        var root = ExternalOutputDirectory.FindRepositoryRoot();
        foreach (var output in new[] { root, Path.Combine(root, "output"), Path.Combine(root, "src", "..", "output") })
            Assert.Throws<ArgumentException>(() => BlindAnswerOptions.Parse(["--csv", "synthetic.csv", "--out-dir", output]));
        Assert.Throws<ArgumentException>(() => BlindAnswerOptions.Parse(["--csv", "synthetic.csv"]));
        Assert.Throws<ArgumentException>(() => BlindAnswerOptions.Parse(["--out-dir"]));
        Assert.Throws<ArgumentException>(() => BlindAnswerOptions.Parse(["--unknown", "value"]));
    }

    private static string[][] ReadCsv(string path)
    {
        using var parser = new TextFieldParser(path, Encoding.UTF8) { HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = false };
        parser.SetDelimiters(",");
        var rows = new List<string[]>();
        while (!parser.EndOfData) rows.Add(parser.ReadFields()!);
        return rows.ToArray();
    }

    private sealed class FakeOllama(string generated = FakeOllama.Answer) : HttpMessageHandler
    {
        public const string Answer = "Hỏi nơi lấy mẫu về \"chuẩn bị\".\nMang giấy tờ, kết quả cũ.";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.AbsolutePath == "/api/tags")
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { models = Array.Empty<object>() }) };
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            object content = request.RequestUri.AbsolutePath switch
            {
                "/api/embed" => new { embeddings = body.RootElement.GetProperty("input").EnumerateArray().Select(_ => new[] { 1.0, 0.0 }).ToArray() },
                "/api/chat" => new { done = true, message = new { content = generated } },
                _ => throw new InvalidOperationException("Unexpected endpoint.")
            };
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(content) };
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "rag-blind-test-" + Guid.NewGuid().ToString("N"));
        public TemporaryDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, true);
    }
}
