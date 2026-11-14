extern alias RagEval;

using System.Net.Http.Json;
using System.Text.Json;
using ClosedXML.Excel;
using RagEval::ClinicManagement.AI.RagEval;

namespace ClinicManagement.IntegrationTests;

public class RagDevPlantTests
{
    private const string PassageText = "Nên chuẩn bị hồ sơ. Phiếu hẹn có thời gian 8 giờ. Mang giấy tờ bản gốc.";
    private const string PassageId = "test:0";
    private const int Seed = 20261113;

    [Fact]
    public async Task PlantsUseCopiesAndDoNotReuseOrMutateOriginalAnswers()
    {
        var (questions, answers) = Examples();
        var originals = answers.Select(a => a.Text).ToArray();
        var result = await RunAsync(questions, answers);

        Assert.Equal(originals, answers.Select(a => a.Text));
        Assert.Equal(originals, result.Originals.Select(a => a.Text));
        var plants = result.Plants.ToArray();
        Assert.NotEmpty(plants);
        Assert.Equal(plants.Length, plants.Select(v => (v.Answer.Code, v.Answer.Side)).Distinct().Count());
        Assert.All(plants, variant =>
        {
            Assert.Same(answers.Single(a => a.Code == variant.Answer.Code && a.Side == variant.Answer.Side), variant.Answer);
            Assert.NotEqual(variant.Answer.Text, variant.Edited);
        });
    }

    [Fact]
    public async Task PolicyResponsesNeverReceivePlantsEvenWithPassagesAndDifferentSides()
    {
        var (questions, answers) = Examples();
        var result = await RunAsync(questions, answers);
        Assert.Equal(2, result.Eligible);
        Assert.Contains(result.Originals, a => a.Outcome == "OutOfScopeMedicalRequest");
        Assert.NotEmpty(result.Plants);
        Assert.All(result.Plants, variant => Assert.Equal("q1", variant.Answer.Code));
    }

    [Fact]
    public async Task AutomaticControlsContainOnlySingleSentencesFromRecordedPassages()
    {
        var (questions, answers) = Examples();
        var result = await RunAsync(questions, answers);
        Assert.Equal(new[] { "Nên chuẩn bị hồ sơ.", "Phiếu hẹn có thời gian 8 giờ." }, result.Controls.Select(c => c.Text));
        Assert.All(result.Controls, c =>
        {
            Assert.Equal("B", c.Answer.Side);
            Assert.Equal(PassageId, c.SourceId);
            Assert.Contains(c.Text, Assert.Single(c.Answer.Passages!).Text, StringComparison.Ordinal);
        });

        var path = Path.Combine(Path.GetTempPath(), $"dev-plant-test-{Guid.NewGuid():N}.xlsx");
        try
        {
            DevPlantWorkbook.Save(path, result, "mock-digest");
            using var workbook = new XLWorkbook(path);
            var review = workbook.Worksheet("Duyet");
            Assert.Equal(result.Variants.Length + 1, review.LastRowUsed()!.RowNumber());
            Assert.All(review.RowsUsed().Where(row => row.RowNumber() > 1), row => Assert.True(row.Cell(11).IsEmpty()));
            Assert.Equal(answers.Length + 1, workbook.Worksheet("Goc").LastRowUsed()!.RowNumber());
            var automatic = workbook.Worksheet("TuDong");
            Assert.Equal(result.Controls.Select(c => c.Text),
                automatic.RowsUsed().Where(row => row.RowNumber() > 1).Select(row => row.Cell(4).GetString()));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task SameSeedAndMockOllamaProduceIdenticalResults()
    {
        var (questions, answers) = Examples();
        var first = await RunAsync(questions, answers);
        var second = await RunAsync(questions, answers);
        Assert.Equal(first.Variants, second.Variants);
        Assert.Equal(first.Controls, second.Controls);
        Assert.Contains(first.Variants, v => v.Kind == DevPlantRun.ParaphraseKind);
    }

    private static async Task<DevPlantResult> RunAsync(EvaluationQuestion[] questions, DevAnswer[] answers)
    {
        using var handler = new MockOllama();
        using var model = new DevPlantModel(handler, Seed);
        Assert.Equal("mock-digest", await model.GetDigestAsync());
        var run = new DevPlantRun(questions, answers, model, new Dictionary<string, string>(), Seed);
        return await run.GenerateAsync(TextWriter.Null);
    }

    private static (EvaluationQuestion[], DevAnswer[]) Examples()
    {
        EvaluationQuestion[] questions =
        [
            new("Trước khi khám cần gì?", "test", "q1", DevQuestions.InScope),
            new("Có dịch vụ này không?", null, "q2", DevQuestions.Near)
        ];
        DevAnswer[] answers =
        [
            Answer("q1", "A", "LLM", "Nên chuẩn bị hồ sơ. Phiếu hẹn có thời gian 8 giờ.", "KnowledgeAnswer"),
            Answer("q1", "B", "Trích đoạn", "Nên chuẩn bị hồ sơ. Phiếu hẹn có thời gian 8 giờ. Nên đổi giấy tờ.", "KnowledgeExtractive"),
            Answer("q2", "A", "LLM", "Không nên chờ 8 giờ.", "OutOfScopeMedicalRequest"),
            Answer("q2", "B", "Trích đoạn", "Nên chuẩn bị hồ sơ.", "OutOfScopeMedicalRequest")
        ];
        return (questions, answers);
    }

    private static DevAnswer Answer(string code, string side, string mode, string text, string outcome) =>
        new(code, side, mode, text, [PassageId], null)
        {
            Outcome = outcome,
            Passages = [new ReplayPassage(PassageId, PassageText, 1)]
        };

    private sealed class MockOllama : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath == "/api/tags")
                return new() { Content = JsonContent.Create(new { models = new[] { new { name = DevPlantModel.Name, digest = "mock-digest" } } }) };

            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var body = json.RootElement;
            Assert.Equal("qwen2.5:7b", body.GetProperty("model").GetString());
            var options = body.GetProperty("options");
            Assert.Equal(0, options.GetProperty("temperature").GetInt32());
            Assert.Equal(Seed, options.GetProperty("seed").GetInt32());
            var user = body.GetProperty("messages")[1].GetProperty("content").GetString()!;
            var reply = !user.Contains(DevPlantModel.Paraphrase, StringComparison.Ordinal) ? "{}" :
                user.Contains("Văn bản gốc cần xử lý:\nNên chuẩn bị hồ sơ.", StringComparison.Ordinal)
                    ? """{"text":"Bạn nên chuẩn bị hồ sơ trước."}"""
                    : """{"text":"Phiếu hẹn ghi mốc thời gian 8 giờ."}""";
            return new() { Content = JsonContent.Create(new { message = new { content = reply } }) };
        }
    }
}
