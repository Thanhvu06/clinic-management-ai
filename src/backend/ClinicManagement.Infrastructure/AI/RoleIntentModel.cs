using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using ClinicManagement.AI.Training;
using ClinicManagement.Application.AI.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ML;
using Microsoft.ML.Data;

namespace ClinicManagement.Infrastructure.AI;

public sealed class RoleIntentModelOptions
{
    public bool Enabled { get; set; } = true;
    public double? Threshold { get; set; }
    public string Directory { get; set; } = Path.Combine(AppContext.BaseDirectory, "models", "role-intent-v2");
    public string? LabelsPath { get; set; }
}

public sealed record RoleIntentModelPrediction(string Label, double Confidence, IReadOnlyDictionary<string, double> Probabilities);
public interface IRoleIntentModel
{
    bool IsAvailable { get; }
    double Threshold { get; }
    bool IsLabelAllowed(string label, AiActorRole role);
    RoleIntentModelPrediction? Predict(string text, AiActorRole role);
}

/// <summary>Loads the frozen v2 once. No training, provider calls or write actions.</summary>
public sealed class RoleIntentModel : IRoleIntentModel, IDisposable
{
    public const string ExpectedSha256 = "cb62d5995617065a26c5e8facffb662fbc5ab319b8932582b623981da37074ba";
    private readonly object _gate = new();
    private readonly ILogger<RoleIntentModel> _logger;
    private PredictionEngine<ModelInput, ModelOutput>? _engine;
    private RoleIntentLabel[] _catalog = [];
    private string[] _scoreLabels = [];
    private volatile bool _available;
    public bool IsAvailable => _available;
    public double Threshold { get; private set; } = .75;
    public double LoadMilliseconds { get; }

    public RoleIntentModel(IOptions<RoleIntentModelOptions> options, ILogger<RoleIntentModel> logger)
    {
        _logger = logger;
        var timer = Stopwatch.StartNew();
        try
        {
            var settings = options.Value;
            if (!settings.Enabled) return;
            var zip = Path.Combine(settings.Directory, "role_intent_model_v2.zip");
            if (!File.Exists(zip) || Hash(zip) != ExpectedSha256) throw new InvalidDataException("Model missing or checksum mismatch.");
            using var metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(settings.Directory, "role_intent_model_meta_v2.json")));
            var meta = metadata.RootElement;
            if (meta.GetProperty("modelSha256").GetString() != ExpectedSha256 || meta.GetProperty("modelVersion").GetString() != "role-intent-mlnet-v2")
                throw new InvalidDataException("Metadata mismatch.");
            var json = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            _catalog = meta.GetProperty("labelCatalog").Deserialize<RoleIntentLabel[]>(json)!;
            var labelsPath = settings.LabelsPath ?? Path.Combine(settings.Directory, RoleIntentDatasetGenerator.LabelsFile);
            if (Hash(labelsPath) != meta.GetProperty("dataSha256").GetProperty(RoleIntentDatasetGenerator.LabelsFile).GetString())
                throw new InvalidDataException("Label catalog checksum mismatch.");
            var catalog = JsonSerializer.Deserialize<RoleIntentLabel[]>(File.ReadAllText(labelsPath), json)!;
            if (JsonSerializer.Serialize(catalog) != JsonSerializer.Serialize(_catalog)) throw new InvalidDataException("Catalog mismatch.");
            _scoreLabels = meta.GetProperty("scoreLabels").Deserialize<string[]>()!;
            Threshold = settings.Threshold ?? meta.GetProperty("thresholdPolicy").GetProperty("threshold").GetDouble();
            if (!double.IsFinite(Threshold) || Threshold < 0 || Threshold > 1 || _catalog.Length != 24 || _scoreLabels.Length != 24 ||
                !_scoreLabels.Order(StringComparer.Ordinal).SequenceEqual(_catalog.Select(x => x.Label).Order(StringComparer.Ordinal)))
                throw new InvalidDataException("Invalid threshold or labels.");
            var ml = new MLContext(20261002);
            var model = ml.Model.Load(zip, out _);
            var probe = model.Transform(ml.Data.LoadFromEnumerable(new[] { new ModelInput() }));
            VBuffer<ReadOnlyMemory<char>> slots = default;
            probe.Schema["Score"].Annotations.GetValue("SlotNames", ref slots);
            if (!slots.DenseValues().Select(x => x.ToString()).SequenceEqual(_scoreLabels, StringComparer.Ordinal))
                throw new InvalidDataException("Score label mismatch.");
            _engine = ml.Model.CreatePredictionEngine<ModelInput, ModelOutput>(model);
            _available = true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _engine?.Dispose();
            _engine = null;
            _logger.LogWarning("Role-intent v2 disabled at startup ({FailureType}); existing routing remains available.", exception.GetType().Name);
        }
        finally { LoadMilliseconds = timer.Elapsed.TotalMilliseconds; }
    }

    public bool IsLabelAllowed(string label, AiActorRole role) =>
        _catalog.Any(x => x.Label == label && x.Roles.Contains(role.ToString(), StringComparer.Ordinal));

    public RoleIntentModelPrediction? Predict(string text, AiActorRole role)
    {
        lock (_gate)
        {
            if (!_available || _engine is null || !Enum.IsDefined(role)) return null;
            try
            {
                var scores = _engine.Predict(new ModelInput { Text = RoleIntentDatasetGenerator.Normalize(text) }).Score;
                return Mask(scores, _scoreLabels, _catalog, role);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                _available = false;
                // Do not log exceptions/messages that might contain user input.
                _logger.LogWarning("Role-intent v2 disabled after prediction failure ({FailureType}).", exception.GetType().Name);
                return null;
            }
        }
    }

    public static RoleIntentModelPrediction? Mask(float[] scores, string[] scoreLabels, RoleIntentLabel[] catalog, AiActorRole role)
    {
        if (!Enum.IsDefined(role) || scores.Length != scoreLabels.Length || scores.Any(x => !float.IsFinite(x) || x < 0)) return null;
        var allowed = catalog.Where(x => x.Roles.Contains(role.ToString(), StringComparer.Ordinal)).Select(x => x.Label).ToHashSet(StringComparer.Ordinal);
        var probabilities = scoreLabels.Select((label, index) => (label, score: (double)scores[index]))
            .Where(x => allowed.Contains(x.label)).ToDictionary(x => x.label, x => x.score, StringComparer.Ordinal);
        var total = probabilities.Values.Sum();
        if (total <= 0) return null;
        foreach (var label in probabilities.Keys.ToArray()) probabilities[label] /= total;
        var top = probabilities.OrderByDescending(x => x.Value).ThenBy(x => x.Key, StringComparer.Ordinal).First();
        return new(top.Key, top.Value, probabilities);
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    public void Dispose() { lock (_gate) { _available = false; _engine?.Dispose(); _engine = null; } }
    public sealed class ModelInput { public string Text { get; set; } = ""; public string Label { get; set; } = ""; public float Weight { get; set; } = 1; }
    public sealed class ModelOutput { public float[] Score { get; set; } = []; }
}
