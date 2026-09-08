using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.AI.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ML;
using Microsoft.ML.Data;

namespace ClinicManagement.Infrastructure.AI;

public class SymptomInferenceInput
{
    public string Text { get; set; } = string.Empty;
}

public class SymptomInferenceOutput
{
    [ColumnName("PredictedLabel")]
    public string PredictedLabel { get; set; } = string.Empty;

    public float[] Score { get; set; } = Array.Empty<float>();
}

public class MlNetSpecialtyClassifier : IAiSpecialtyClassifier
{
    private readonly AiClassifierOptions _options;
    private readonly ILogger<MlNetSpecialtyClassifier> _logger;
    private readonly MLContext _mlContext;
    private readonly object _lock = new();
    private PredictionEngine<SymptomInferenceInput, SymptomInferenceOutput>? _predictionEngine;
    private bool _initialized;
    private bool _isLoaded;
    private bool _isClinicallyValidated;

    public MlNetSpecialtyClassifier(IOptions<AiClassifierOptions> options, ILogger<MlNetSpecialtyClassifier> logger)
    {
        _options = options.Value;
        _logger = logger;
        _mlContext = new MLContext(seed: 42);
    }

    private void EnsureInitialized()
    {
        if (_initialized) return;

        lock (_lock)
        {
            if (_initialized) return;
            _initialized = true;

            if (!_options.Enabled)
            {
                _logger.LogInformation("ML.NET Specialty Classifier is disabled via configuration.");
                return;
            }

            if (string.IsNullOrWhiteSpace(_options.ModelPath) || !File.Exists(_options.ModelPath))
            {
                _logger.LogWarning("ML.NET Specialty Classifier model file not found at '{ModelPath}'. Classifier disabled.", _options.ModelPath);
                return;
            }

            // Check metadata file for clinical validation
            var metadataPath = Path.Combine(Path.GetDirectoryName(_options.ModelPath) ?? "", "model_metadata.json");
            if (File.Exists(metadataPath))
            {
                try
                {
                    var metaJson = File.ReadAllText(metadataPath);
                    using var doc = JsonDocument.Parse(metaJson);
                    if (doc.RootElement.TryGetProperty("clinicallyValidated", out var validatedProp))
                    {
                        _isClinicallyValidated = validatedProp.GetBoolean();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to read model metadata from '{MetadataPath}'.", metadataPath);
                }
            }

            if (!_isClinicallyValidated && _options.RequireClinicallyValidated)
            {
                _logger.LogWarning(
                    "ML.NET Classifier model is NOT clinically validated (ClinicallyValidated = false). " +
                    "Production safety guard active (RequireClinicallyValidated = true). Rejecting demo model load.");
                return;
            }

            try
            {
                var model = _mlContext.Model.Load(_options.ModelPath, out _);
                _predictionEngine = _mlContext.Model.CreatePredictionEngine<SymptomInferenceInput, SymptomInferenceOutput>(model);
                _isLoaded = true;
                _logger.LogInformation("ML.NET Specialty Classifier loaded successfully from '{ModelPath}'. Validated: {IsValidated}", _options.ModelPath, _isClinicallyValidated);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load ML.NET model from '{ModelPath}'.", _options.ModelPath);
            }
        }
    }

    public SpecialtyClassificationResult? ClassifySymptom(string symptomDescription)
    {
        EnsureInitialized();

        if (!_isLoaded || _predictionEngine == null || string.IsNullOrWhiteSpace(symptomDescription))
        {
            return null;
        }

        try
        {
            var input = new SymptomInferenceInput { Text = symptomDescription.Trim() };
            SymptomInferenceOutput prediction;
            lock (_lock)
            {
                prediction = _predictionEngine.Predict(input);
            }

            if (string.IsNullOrWhiteSpace(prediction.PredictedLabel))
            {
                return null;
            }

            float maxScore = prediction.Score != null && prediction.Score.Length > 0 ? prediction.Score.Max() : 1.0f;

            if (maxScore < _options.MinimumConfidence)
            {
                _logger.LogInformation("ML.NET prediction '{Label}' confidence {Score:F2} below minimum threshold {Threshold:F2}.",
                    prediction.PredictedLabel, maxScore, _options.MinimumConfidence);
                return null;
            }

            return new SpecialtyClassificationResult
            {
                SpecialtyCode = prediction.PredictedLabel,
                Confidence = maxScore,
                IsClinicallyValidated = _isClinicallyValidated
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred during ML.NET symptom classification inference.");
            return null;
        }
    }
}
