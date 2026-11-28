using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClinicManagement.Infrastructure.AI;
using ClinicManagement.Infrastructure.AI.Knowledge;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;

namespace ClinicManagement.IntegrationTests;

public sealed class PatientEmergencySemanticGateTests(ITestOutputHelper output)
{
    internal const string SemanticOnly = "Bên môi bà xệ hẳn xuống, câu nói tự nhiên chỉ còn tiếng ú ớ.";

    [Fact]
    public async Task Embeddings_are_a_bounded_fallback_after_both_deterministic_layers_and_self_harm()
    {
        using var handler = new SafetyEmbeddingHandler();
        using var client = new HttpClient(handler);
        using var gate = Gate(client, "http://127.0.0.1");
        var guard = new AiSafetyGuard(gate);
        Assert.False((await guard.InspectPatientAsync(SemanticOnly)).IsEmergency);

        Assert.True(await gate.BuildIndexAsync(CancellationToken.None));
        Assert.Equal(PatientEmergencyExamples.Emergency.Length + PatientEmergencyExamples.Routine.Length, handler.Inputs.Count);
        handler.Inputs.Clear();

        var danger = await guard.InspectPatientAsync("Sau bữa tối ông lơ mơ và gọi không dậy.");
        var ingestion = await guard.InspectPatientAsync("Bé nhà mình hít phải viên bi nhựa.");
        var combined = await guard.InspectPatientAsync("Bé nuốt đồng xu rồi bắt đầu ho sặc, môi tím.");
        var distress = await guard.InspectPatientAsync("Tôi bế tắc, không muốn sống.");
        Assert.Equal("dấu hiệu nguy hiểm", danger.MatchedCategory);
        Assert.Equal("phơi nhiễm chất độc hoặc dị vật nguy hiểm", ingestion.MatchedCategory);
        Assert.True(combined.IsEmergency);
        Assert.True(distress.IsSelfHarm);
        Assert.Empty(handler.Inputs);

        var semanticOnly = await guard.InspectPatientAsync(SemanticOnly + " Mail: test@example.invalid");
        Assert.True(semanticOnly.IsEmergency);
        Assert.Equal("dấu hiệu nguy hiểm theo ngữ nghĩa", semanticOnly.MatchedCategory);
        Assert.DoesNotContain("test@example.invalid", Assert.Single(handler.Inputs));

        Assert.False((await guard.InspectPatientAsync("Xin hỏi lịch tái khám định kỳ.")).IsEmergency);
        handler.Fail = true;
        Assert.False((await guard.InspectPatientAsync(SemanticOnly)).IsEmergency);
        Assert.True((await guard.InspectPatientAsync("Con tôi thở rít.")).IsEmergency);
    }

    [Theory]
    [InlineData(0.62, 0.57, true)]
    [InlineData(0.61, 0.40, false)]
    [InlineData(0.79, 0.78, false)]
    [InlineData(0.79, 0.81, false)]
    public void Cosine_requires_absolute_emergency_evidence_and_a_margin_over_routine(double urgent, double routine, bool expected) =>
        Assert.Equal(expected, PatientEmergencySemanticGate.IsEmergency((urgent, routine)));

    [Fact]
    public async Task Evaluation_corpora_are_disjoint_and_measured_with_the_configured_local_model_when_available()
    {
        Assert.True(PatientEmergencyExamples.Emergency.Length >= 150);
        Assert.True(PatientEmergencyExamples.Routine.Length >= 100);
        Assert.True(PatientEmergencyEvaluationCases.TestEmergency.Length >= 40);
        Assert.True(PatientEmergencyEvaluationCases.TestRoutine.Length >= 25);
        var all = PatientEmergencyExamples.Emergency.Concat(PatientEmergencyExamples.Routine)
            .Concat(PatientEmergencyEvaluationCases.DevEmergency).Concat(PatientEmergencyEvaluationCases.DevRoutine)
            .Concat(PatientEmergencyEvaluationCases.TestEmergency).Concat(PatientEmergencyEvaluationCases.TestRoutine)
            .Select(sentence => sentence.Normalize().Trim().ToLowerInvariant()).ToArray();
        Assert.All(all, sentence => Assert.NotEmpty(sentence));
        Assert.Equal(all.Length, all.Distinct(StringComparer.Ordinal).Count());

        var localEndpoint = Environment.GetEnvironmentVariable("PATIENT_EMERGENCY_EVAL_OLLAMA_URL");
        if (string.IsNullOrWhiteSpace(localEndpoint)) return;

        using var client = new HttpClient(new SocketsHttpHandler { UseProxy = false });
        using var gate = Gate(client, localEndpoint);
        Assert.True(await gate.BuildIndexAsync(CancellationToken.None), "Could not build the bge-m3 evaluation index.");
        foreach (var (name, positive, negative) in new[]
        {
            ("dev", PatientEmergencyEvaluationCases.DevEmergency, PatientEmergencyEvaluationCases.DevRoutine),
            ("test", PatientEmergencyEvaluationCases.TestEmergency, PatientEmergencyEvaluationCases.TestRoutine)
        })
        {
            var guard = new AiSafetyGuard(gate);
            var semantic = 0;
            var semanticFalse = 0;
            var combined = 0;
            var combinedFalse = 0;
            foreach (var (message, expected) in positive.Select(message => (message, true)).Concat(negative.Select(message => (message, false))))
            {
                var embedding = await gate.IsEmergencyAsync(message, CancellationToken.None);
                var result = (await guard.InspectPatientAsync(message)).IsEmergency;
                if (embedding) { if (expected) semantic++; else semanticFalse++; }
                if (result) { if (expected) combined++; else combinedFalse++; }
                if (expected != result) output.WriteLine($"{name} combined mismatch: {message}");
            }
            output.WriteLine($"{name}: embedding TP={semantic} FP={semanticFalse} P={positive.Length} N={negative.Length}; combined TP={combined} FP={combinedFalse}");
            Assert.True(combined >= semantic);
            Assert.InRange(combinedFalse, 0, name == "dev" ? 2 : 0);
            Assert.True(semantic >= positive.Length * 0.85);
            Assert.InRange(semanticFalse, 0, 1);
        }
    }

    internal static PatientEmergencySemanticGate Gate(HttpClient client, string url)
    {
        var options = Options.Create(new OllamaOptions { IsEnabled = true, BaseUrl = url });
        return new(new OllamaKnowledgeClient(client, options), options, NullLogger<PatientEmergencySemanticGate>.Instance);
    }

    internal sealed class SafetyEmbeddingHandler : HttpMessageHandler
    {
        private static readonly HashSet<string> EmergencyExamples = new(PatientEmergencyExamples.Emergency, StringComparer.Ordinal);

        internal List<string> Inputs { get; } = [];
        internal bool Fail { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal("/api/embed", request.RequestUri?.AbsolutePath);
            using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            Assert.Equal("bge-m3", document.RootElement.GetProperty("model").GetString());
            var input = document.RootElement.GetProperty("input").EnumerateArray().Select(item => item.GetString()!).ToArray();
            Inputs.AddRange(input);
            if (Fail) return new(HttpStatusCode.ServiceUnavailable);
            float[] Vector(string text) => EmergencyExamples.Contains(text) || text.Contains(SemanticOnly, StringComparison.Ordinal)
                ? [1, 0] : [0, 1];
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { embeddings = input.Select(Vector).ToArray() }) };
        }
    }
}
