using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.AI.Interfaces;
using Microsoft.ML;
using Microsoft.ML.Data;

namespace ClinicManagement.Infrastructure.AI;

public class IntentInferenceInput
{
    public string Text { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
}

public class IntentInferenceOutput
{
    [ColumnName("PredictedLabel")]
    public string PredictedLabel { get; set; } = string.Empty;

    public float[] Score { get; set; } = Array.Empty<float>();
}

public class VietnameseIntentClassifier : IVietnameseIntentClassifier
{
    private readonly IntentClassificationMode _mode;
    private readonly string? _customModelPath;
    private readonly float _optimalThreshold;

    private static readonly object _initLock = new();
    private static bool _modelLoadAttempted;
    private static MLContext? _mlContext;
    private static ITransformer? _loadedModel;
    private static string? _loadedModelPath;
    private static float _metadataOptimalThreshold = 0.35f;
    private static readonly ConcurrentBag<PredictionEngine<IntentInferenceInput, IntentInferenceOutput>> _enginePool = new();

    private static readonly HashSet<string> ExactGreetings = new(StringComparer.OrdinalIgnoreCase)
    {
        "chào", "xin chào", "chào bạn", "chào bác sĩ", "chào bs", "alo", "hello", "hi", "hey",
        "cảm ơn", "cảm ơn bạn", "cảm ơn bác sĩ", "thanks", "thank you", "tạm biệt", "bye", "hẹn gặp lại",
        "dạ", "vâng", "được rồi", "tuyệt vời", "chào ad", "chào em"
    };

    private static readonly HashSet<string> ConfirmationWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "chốt", "chốt lịch", "chốt giúp tôi", "đồng ý", "xác nhận", "xác nhận đặt lịch",
        "ok chốt", "tôi đồng ý", "ok tôi đồng ý", "đồng ý đặt", "hoàn tất", "hoàn tất đặt lịch",
        "chốt nhé", "xác nhận nhé", "chốt nha", "đồng ý nha"
    };

    private static readonly HashSet<string> CancelWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "hủy", "hủy đặt lịch", "hủy lịch", "hủy bản nháp", "hủy thao tác", "không đặt nữa",
        "bỏ qua", "thôi không đặt nữa", "thôi khỏi", "hủy bỏ", "không khám nữa"
    };

    private static readonly HashSet<string> ReviewWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "xem lại", "xem lại lịch", "xem lại thông tin", "xem tóm tắt", "kiểm tra lịch",
        "kiểm tra thông tin", "xem lại bản nháp", "tóm tắt lịch khám", "kiểm tra lại"
    };

    private static readonly string[] ClinicalKeywordTerms = new[]
    {
        "đau", "sốt", "ho", "mệt", "khó thở", "chóng mặt", "buồn nôn", "nôn", "ngứa",
        "dị ứng", "viêm", "nhức", "tức ngực", "phù", "co giật", "rát", "chảy máu",
        "tiêu chảy", "táo bón", "mất ngủ", "sụt cân", "nổi mẩn", "mẩn ngứa", "hắt hơi",
        "sổ mũi", "viêm họng", "đau bụng", "đau lưng", "đau đầu", "đau dạ dày", "huyết áp",
        "tiểu đường", "tim đập nhanh", "khó nuốt", "rát họng", "chấn thương", "mỏi cơ",
        "khám tổng quát", "khám sức khỏe", "khám định kỳ", "tái khám", "kiểm tra sức khỏe",
        "tầm soát", "khám thai", "khám mắt", "khám răng", "khám tai mũi họng",
        "tim mạch", "tai mũi họng", "da liễu", "nhi khoa", "sản phụ khoa", "răng hàm mặt", "nội khoa", "ngoại khoa"
    };

    public VietnameseIntentClassifier(
        IntentClassificationMode mode = IntentClassificationMode.Shadow,
        string? customModelPath = null,
        float? overrideThreshold = null)
    {
        _mode = mode;
        _customModelPath = customModelPath;
        EnsureMetadataLoaded(_customModelPath);
        if (_mode != IntentClassificationMode.Off)
        {
            EnsureModelLoaded();
        }
        _optimalThreshold = overrideThreshold ?? _metadataOptimalThreshold;
    }

    public IntentClassificationMode Mode => _mode;
    public string? LoadedModelPath => _loadedModelPath;
    public float OptimalThreshold => _optimalThreshold;

    public IntentClassificationResult Classify(string? message, IntentClassificationContext? context = null)
    {
        var result = new IntentClassificationResult();

        if (string.IsNullOrWhiteSpace(message))
        {
            result.Intent = AiChatIntentTypes.UnclearOrOutOfScope;
            result.IsClear = false;
            result.ClarificationPrompt = "Vui lòng nhập câu hỏi hoặc thông tin bạn cần ClinicCare hỗ trợ nhé.";
            return result;
        }

        var trimmed = message.Trim();
        var lower = trimmed.ToLowerInvariant();
        var normalized = NormalizeText(trimmed);

        // In Shadow or Active mode: Run ML model to collect telemetry (and use for fallback in Active mode)
        if (_mode == IntentClassificationMode.Shadow || _mode == IntentClassificationMode.Active)
        {
            var engine = RentEngine();
            if (engine != null)
            {
                try
                {
                    var mlPred = engine.Predict(new IntentInferenceInput { Text = trimmed });
                    float confidence = (mlPred.Score != null && mlPred.Score.Length > 0) ? mlPred.Score.Max() : 0.0f;
                    if (!float.IsNaN(confidence) && !float.IsInfinity(confidence) && !string.IsNullOrWhiteSpace(mlPred.PredictedLabel))
                    {
                        result.ShadowIntent = mlPred.PredictedLabel;
                        result.ShadowConfidence = confidence;
                        result.Method = "RuleOnly (Shadow ML)";
                    }
                }
                catch
                {
                    // Clean fallback
                }
                finally
                {
                    ReturnEngine(engine);
                }
            }
        }

        // 1. Gibberish / Nonsense Filter
        if (IsGibberish(trimmed, lower, normalized))
        {
            result.Intent = AiChatIntentTypes.UnclearOrOutOfScope;
            result.IsClear = false;
            result.ClarificationPrompt = "ClinicCare chưa hiểu rõ yêu cầu của bạn. Bạn có thể mô tả cụ thể hơn về triệu chứng sức khỏe, nhu cầu đặt lịch hoặc thông tin phòng khám cần tìm hiểu không ạ?";
            return result;
        }

        // 2. Cancellation Intent ("hủy", "không đặt nữa")
        if (CancelWords.Any(w => lower == w || lower.StartsWith(w + " ") || lower.EndsWith(" " + w)))
        {
            result.Intent = AiChatIntentTypes.CancelDraft;
            return result;
        }

        // 3. Negations and Corrections ("không phải Khải, tôi muốn bác sĩ Hà", "đổi ngày mai")
        if (DetectCorrection(trimmed, lower, out var correctionResult))
        {
            return correctionResult;
        }

        // 4. Confirmation Intent ("chốt", "đồng ý", "xác nhận")
        // "Chưa chốt, giá bao nhiêu?" must NOT confirm!
        if (lower.Contains("chưa chốt") || lower.Contains("khoan đã") || lower.Contains("từ từ"))
        {
            // Do not confirm, let subsequent pricing or inquiry handlers evaluate
        }
        else if (ConfirmationWords.Any(w => lower == w || lower.StartsWith(w + " ") || lower.EndsWith(" " + w)))
        {
            result.Intent = AiChatIntentTypes.ConfirmBooking;
            return result;
        }

        // Context-dependent "ok" / "đồng ý"
        if (lower == "ok" || lower == "oke" || lower == "okay")
        {
            if (context != null && context.HasActiveDraft && context.HasSlot && context.HasReason)
            {
                result.Intent = AiChatIntentTypes.ConfirmBooking;
                return result;
            }
            result.Intent = AiChatIntentTypes.Greeting;
            return result;
        }

        // 5. Review Draft Intent ("xem lại", "tóm tắt")
        if (ReviewWords.Any(w => lower == w || lower.StartsWith(w + " ") || lower.EndsWith(" " + w)))
        {
            result.Intent = AiChatIntentTypes.ReviewDraft;
            return result;
        }

        // 6. Pricing Inquiry ("giá bao nhiêu", "chi phí khám")
        if (lower.Contains("bảng giá") || lower.Contains("chi phí khám") || lower.Contains("giá khám") ||
            lower.Contains("bao nhiêu tiền") || lower.Contains("hết bao nhiêu tiền") || lower.Contains("tiền khám") ||
            lower.Contains("phí khám") || lower.Contains("giá dịch vụ") || lower.Contains("viện phí") ||
            lower.Contains("giá bao nhiêu"))
        {
            result.Intent = AiChatIntentTypes.PricingInquiry;
            return result;
        }

        // 7. Facility / Reception Inquiry
        if (lower.Contains("lễ tân") || lower.Contains("tiếp đón") || lower.Contains("bàn tiếp đón") ||
            lower.Contains("hotline") || lower.Contains("số điện thoại") || lower.Contains("sđt") ||
            lower.Contains("địa chỉ") || lower.Contains("ở đâu") || lower.Contains("giờ mở cửa") ||
            lower.Contains("giờ làm việc") || lower.Contains("mấy giờ làm việc") || lower.Contains("liên hệ phòng khám") ||
            lower.Contains("chủ nhật có khám") || lower.Contains("có khám chủ nhật") || lower.Contains("phòng khám ở đâu"))
        {
            result.Intent = AiChatIntentTypes.FacilityInquiry;
            return result;
        }

        // 8. View Appointments
        if (lower.Contains("lịch hẹn của tôi") || lower.Contains("lịch đã đặt") || lower.Contains("xem lịch hẹn") ||
            lower.Contains("danh sách lịch hẹn") || lower.Contains("các lịch khám của tôi") || lower.Contains("tra cứu lịch hẹn") ||
            lower.Contains("lịch sử đặt khám"))
        {
            result.Intent = AiChatIntentTypes.ViewAppointments;
            return result;
        }

        // 9. Relative Doctor Selection ("người đầu", "người đầu tiên", "bác sĩ thứ nhất", "bác sĩ 1", "người thứ hai")
        if (lower.Contains("người đầu") || lower.Contains("người đầu tiên") || lower.Contains("bác sĩ đầu tiên") ||
            lower.Contains("bác sĩ thứ nhất") || lower.Contains("bác sĩ 1") || lower.Contains("người thứ nhất") ||
            lower.Contains("bác sĩ đầu"))
        {
            result.Intent = AiChatIntentTypes.SelectDoctor;
            result.ExtractedDoctorName = "@relative:1";
            result.ExtractedRelativeDoctorIndex = 0;
            return result;
        }
        if (lower.Contains("người thứ hai") || lower.Contains("người thứ 2") || lower.Contains("bác sĩ thứ hai") ||
            lower.Contains("bác sĩ 2") || lower.Contains("người thứ nhì"))
        {
            result.Intent = AiChatIntentTypes.SelectDoctor;
            result.ExtractedDoctorName = "@relative:2";
            result.ExtractedRelativeDoctorIndex = 1;
            return result;
        }

        // 10. Relative Slot Selection ("giờ đầu", "giờ đầu tiên", "ca đầu", "ca đầu tiên", "khung giờ đầu", "khung giờ thứ nhất")
        if (lower.Contains("giờ đầu") || lower.Contains("giờ đầu tiên") || lower.Contains("khung giờ đầu") ||
            lower.Contains("khung giờ thứ nhất") || lower.Contains("ca đầu") || lower.Contains("ca đầu tiên") ||
            lower.Contains("suất đầu") || lower.Contains("khung giờ 1"))
        {
            result.Intent = AiChatIntentTypes.SelectSlot;
            result.ExtractedRelativeSlotIndex = 0;
            return result;
        }
        if (lower.Contains("giờ thứ hai") || lower.Contains("khung giờ thứ hai") || lower.Contains("giờ thứ 2") ||
            lower.Contains("ca thứ hai") || lower.Contains("ca 2") || lower.Contains("khung giờ 2"))
        {
            result.Intent = AiChatIntentTypes.SelectSlot;
            result.ExtractedRelativeSlotIndex = 1;
            return result;
        }

        // 11. Doctor Selection / Search with explicit doctor mention
        var doctorMatch = Regex.Match(trimmed, @"(?:chọn\s+)?(?:bác sĩ|bac si|bs\.|bs|bác sỹ|bac sy)\s+([A-Za-z0-9À-ỹ\s]+)", RegexOptions.IgnoreCase);
        if (doctorMatch.Success)
        {
            var rawName = doctorMatch.Groups[1].Value;
            var delimiterMatch = Regex.Match(rawName, @"^(.*?)(?:\s+(?:vào|từ|lúc|ngày|khoa|chuyên khoa|sáng|chiều|tối)\b|[,\.\?!])", RegexOptions.IgnoreCase);
            var candidateName = (delimiterMatch.Success ? delimiterMatch.Groups[1].Value : rawName).Trim();

            if (!string.IsNullOrWhiteSpace(candidateName) && candidateName.Length >= 2)
            {
                result.ExtractedDoctorName = candidateName;
                if (lower.Contains("có khám không") || lower.Contains("thông tin") || lower.Contains("tìm bác sĩ") || lower.Contains("xem bác sĩ"))
                {
                    result.Intent = AiChatIntentTypes.DoctorSearch;
                }
                else
                {
                    result.Intent = AiChatIntentTypes.SelectDoctor;
                }
                return result;
            }
        }

        if (lower.Contains("danh sách bác sĩ") || lower.Contains("đội ngũ bác sĩ") || lower.Contains("tìm bác sĩ") || lower.Contains("xem bác sĩ"))
        {
            result.Intent = AiChatIntentTypes.DoctorSearch;
            return result;
        }

        // 12. Operational: Find Earliest Available Slot
        if (lower.Contains("sớm nhất") || lower.Contains("lúc nào sớm nhất") || lower.Contains("tìm lịch sớm nhất") || lower.Contains("giờ nào sớm nhất"))
        {
            result.Intent = AiChatIntentTypes.FindEarliestAvailableSlot;
            return result;
        }

        // 13. Slot / Date Selection ("mai", "sáng mai", "09:30", "chọn khung giờ", "chọn giờ")
        if (lower == "mai" || lower == "ngày mai" || lower == "sáng mai" || lower == "chiều mai" ||
            lower.StartsWith("chọn khung giờ") || lower.StartsWith("chọn giờ") || lower.StartsWith("chọn ngày") ||
            Regex.IsMatch(lower, @"^\d{1,2}:\d{2}$") || Regex.IsMatch(lower, @"^\d{4}-\d{2}-\d{2}$"))
        {
            result.Intent = AiChatIntentTypes.SelectSlot;
            if (lower.Contains("mai")) result.ExtractedDate = "mai";
            if (lower.Contains("sáng")) result.ExtractedTimePreference = "sáng";
            if (lower.Contains("chiều")) result.ExtractedTimePreference = "chiều";
            return result;
        }

        // 14. Modify Draft Intent ("đổi ngày", "đổi bác sĩ", "đổi giờ")
        if (lower.StartsWith("đổi ngày") || lower.StartsWith("đổi bác sĩ") || lower.StartsWith("đổi giờ") ||
            lower.StartsWith("đổi khung giờ") || lower.StartsWith("chọn lại"))
        {
            result.Intent = AiChatIntentTypes.ModifyDraft;
            result.IsCorrection = true;
            return result;
        }

        // 15. Greetings ("xin chào", "hello", "hi")
        // Mixed utterance: "chào bạn, tôi đau đầu hai ngày nay" -> ProvideReason!
        if (ExactGreetings.Contains(lower) || lower.StartsWith("chào ") || lower.StartsWith("xin chào"))
        {
            if (ContainsClinicalEvidence(lower, out var symptomPart))
            {
                result.Intent = AiChatIntentTypes.ProvideReason;
                result.ExtractedReason = symptomPart;
                return result;
            }
            result.Intent = AiChatIntentTypes.Greeting;
            return result;
        }

        // 16. Clinical Symptoms / Provide Reason (Strict word boundaries, negations separated)
        if (ContainsClinicalEvidence(lower, out var extractedSymptom))
        {
            result.Intent = AiChatIntentTypes.ProvideReason;
            result.ExtractedReason = extractedSymptom;
            return result;
        }

        // 17. Start Booking Intent
        if (lower.StartsWith("tôi muốn đặt") || lower.StartsWith("muốn đặt lịch") || lower.StartsWith("đặt lịch") ||
            lower.StartsWith("đặt khám") || lower.StartsWith("đăng ký khám") || lower.StartsWith("muốn khám") ||
            lower.StartsWith("tôi muốn khám") || lower.StartsWith("tôi muốn hẹn") || lower.StartsWith("hẹn khám") ||
            lower.Contains("tư vấn giúp tôi") || lower.Contains("tư vấn cho tôi") || lower == "tư vấn" ||
            lower.Contains("xem lịch khám") || lower.Contains("xem lịch") || lower.Contains("lịch khám"))
        {
            result.Intent = AiChatIntentTypes.StartBooking;
            return result;
        }

        // 18. ML.NET Model Execution (Active mode)
        if (_mode == IntentClassificationMode.Active)
        {
            var engine = RentEngine();
            if (engine != null)
            {
                try
                {
                    var mlPred = engine.Predict(new IntentInferenceInput { Text = trimmed });
                    float confidence = (mlPred.Score != null && mlPred.Score.Length > 0) ? mlPred.Score.Max() : 0.0f;

                    if (!float.IsNaN(confidence) && !float.IsInfinity(confidence) && !string.IsNullOrWhiteSpace(mlPred.PredictedLabel) && confidence >= _optimalThreshold)
                    {
                        result.Intent = mlPred.PredictedLabel;
                        result.Confidence = confidence;
                        result.Method = "ML.NET Model";
                        result.IsClear = mlPred.PredictedLabel != AiChatIntentTypes.UnclearOrOutOfScope;
                        if (!result.IsClear)
                        {
                            result.ClarificationPrompt = "ClinicCare chưa hiểu rõ yêu cầu của bạn. Bạn có thể mô tả cụ thể hơn về triệu chứng sức khỏe, nhu cầu đặt lịch hoặc thông tin phòng khám cần tìm hiểu không ạ?";
                        }
                        return result;
                    }
                }
                catch
                {
                    // Clean fallback to rule-based decision
                }
                finally
                {
                    ReturnEngine(engine);
                }
            }
        }

        // Fallback: Default to StartBooking for conversational non-gibberish Vietnamese sentences
        if (!IsGibberish(trimmed, lower, normalized) && trimmed.Length >= 2)
        {
            result.Intent = AiChatIntentTypes.StartBooking;
            result.IsClear = true;
            result.Method = "RuleFallback";
            return result;
        }

        result.Intent = AiChatIntentTypes.UnclearOrOutOfScope;
        result.IsClear = false;
        result.ClarificationPrompt = "ClinicCare chưa hiểu rõ yêu cầu của bạn. Bạn có thể mô tả cụ thể hơn về triệu chứng sức khỏe, nhu cầu đặt lịch hoặc thông tin phòng khám cần tìm hiểu không ạ?";
        return result;
    }

    private PredictionEngine<IntentInferenceInput, IntentInferenceOutput>? RentEngine()
    {
        if (_enginePool.TryTake(out var existing))
        {
            return existing;
        }

        EnsureModelLoaded();
        if (_loadedModel == null || _mlContext == null)
        {
            return null;
        }

        lock (_initLock)
        {
            return _mlContext.Model.CreatePredictionEngine<IntentInferenceInput, IntentInferenceOutput>(_loadedModel, ignoreMissingColumns: true);
        }
    }

    private static void ReturnEngine(PredictionEngine<IntentInferenceInput, IntentInferenceOutput> engine)
    {
        _enginePool.Add(engine);
    }

    private static IEnumerable<string> GetCandidateModelPaths(string? customModelPath)
    {
        if (!string.IsNullOrWhiteSpace(customModelPath))
        {
            yield return customModelPath;
        }

        yield return Path.Combine(AppContext.BaseDirectory, "models", "vietnamese_intent_classifier_v1.zip");
        yield return Path.Combine(AppContext.BaseDirectory, "vietnamese_intent_classifier_v1.zip");
        yield return Path.Combine(Directory.GetCurrentDirectory(), "models", "vietnamese_intent_classifier_v1.zip");
        yield return Path.Combine(Directory.GetCurrentDirectory(), "src", "backend", "ClinicManagement.Api", "models", "vietnamese_intent_classifier_v1.zip");
        yield return Path.Combine(Directory.GetCurrentDirectory(), "src", "tools", "ClinicManagement.AI.Training", "models", "vietnamese_intent_classifier_v1.zip");
        yield return Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "tools", "ClinicManagement.AI.Training", "models", "vietnamese_intent_classifier_v1.zip");
    }

    private static void EnsureMetadataLoaded(string? customModelPath)
    {
        lock (_initLock)
        {
            var resolvedModel = GetCandidateModelPaths(customModelPath).FirstOrDefault(File.Exists);
            var metaCandidates = new List<string>();
            if (resolvedModel != null)
            {
                var dir = Path.GetDirectoryName(resolvedModel);
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    metaCandidates.Add(Path.Combine(dir, "intent_model_metadata.json"));
                }
            }
            metaCandidates.Add(Path.Combine(AppContext.BaseDirectory, "models", "intent_model_metadata.json"));
            metaCandidates.Add(Path.Combine(Directory.GetCurrentDirectory(), "models", "intent_model_metadata.json"));
            metaCandidates.Add(Path.Combine(Directory.GetCurrentDirectory(), "src", "backend", "ClinicManagement.Infrastructure", "models", "intent_model_metadata.json"));

            var metaPath = metaCandidates.FirstOrDefault(File.Exists);
            if (metaPath != null)
            {
                try
                {
                    var metaJson = File.ReadAllText(metaPath);
                    using var doc = JsonDocument.Parse(metaJson);
                    if (doc.RootElement.TryGetProperty("optimalConfidenceThreshold", out var optProp) && optProp.TryGetSingle(out var val))
                    {
                        _metadataOptimalThreshold = val;
                    }
                    else if (doc.RootElement.TryGetProperty("Benchmark", out var bProp) &&
                             bProp.TryGetProperty("optimalThreshold", out var optB) && optB.TryGetSingle(out var valB))
                    {
                        _metadataOptimalThreshold = valB;
                    }
                }
                catch
                {
                    // Keep default fallback
                }
            }
        }
    }

    private void EnsureModelLoaded()
    {
        if (_modelLoadAttempted && (string.IsNullOrWhiteSpace(_customModelPath) || string.Equals(_loadedModelPath, _customModelPath, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        lock (_initLock)
        {
            if (_modelLoadAttempted && (string.IsNullOrWhiteSpace(_customModelPath) || string.Equals(_loadedModelPath, _customModelPath, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }
            _modelLoadAttempted = true;

            try
            {
                var resolved = GetCandidateModelPaths(_customModelPath).FirstOrDefault(File.Exists);
                if (resolved != null)
                {
                    _mlContext = new MLContext(seed: 42);
                    _loadedModel = _mlContext.Model.Load(resolved, out _);
                    _loadedModelPath = resolved;
                    EnsureMetadataLoaded(resolved);
                }
            }
            catch
            {
                _loadedModel = null;
                _loadedModelPath = null;
            }
        }
    }

    public static bool IsDisallowedReason(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return true;
        var trimmed = text.Trim();
        var lower = trimmed.ToLowerInvariant();

        if (ExactGreetings.Contains(lower) || ConfirmationWords.Contains(lower) || CancelWords.Contains(lower))
        {
            return true;
        }

        if (lower.StartsWith("tôi chọn") || lower.StartsWith("chọn ") || lower.StartsWith("hủy ") ||
            lower.StartsWith("xem ") || lower.StartsWith("liên hệ") || lower.StartsWith("bảng giá") ||
            lower.StartsWith("chốt ") || lower.StartsWith("ok chốt") || lower.StartsWith("đồng ý ") ||
            lower.StartsWith("xác nhận ") ||
            lower == "ok" || lower == "oke" || lower == "chốt" || lower == "người đầu" || lower == "giờ đầu")
        {
            return true;
        }

        return IsGibberish(trimmed, lower, NormalizeText(trimmed));
    }

    /// <summary>
    /// Checks if a string contains actual clinical evidence (symptoms or consultation purpose)
    /// without relying on string length.
    /// </summary>
    public static bool IsClinicalComplaint(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || IsDisallowedReason(text)) return false;
        return ContainsClinicalEvidence(text.ToLowerInvariant(), out _);
    }

    public static bool IsPlausibleClinicalReason(string? text) => IsClinicalComplaint(text);

    /// <summary>
    /// Business rule validation for the confirmation step (strictly 10 to 500 characters).
    /// </summary>
    public static bool IsValidBookingReason(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || IsDisallowedReason(text)) return false;
        var trimmed = text.Trim();
        return trimmed.Length >= 10 && trimmed.Length <= 500 && IsClinicalComplaint(trimmed);
    }

    public static string SanitizeClinicalReason(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var trimmed = text.Trim();

        // Strip negated symptom clause such as "tôi không sốt, chỉ đau đầu" -> "Đau đầu"
        var negationMatch = Regex.Match(
            trimmed,
            @"^(?:tôi\s+|em\s+|mình\s+|bệnh\s+nhân\s+)?(?:không|chẳng|chưa)\s+(?:bị\s+|có\s+)?(?:sốt|ho|đau|mệt|khó thở|buồn nôn|nôn)[,\.\s]+(?:chỉ|mà\s+chỉ|mà|nhưng\s+chỉ|nhưng)\s+(?:bị\s+|có\s+)?(.+)$",
            RegexOptions.IgnoreCase);
        if (negationMatch.Success)
        {
            var positivePart = negationMatch.Groups[1].Value.Trim();
            if (!string.IsNullOrWhiteSpace(positivePart))
            {
                return positivePart;
            }
        }

        return trimmed;
    }

    public static bool ContainsClinicalEvidence(string lower, out string extractedSymptom)
    {
        extractedSymptom = lower;

        // Check for negation patterns: "tôi không sốt, chỉ đau đầu" -> excludes "sốt", keeps "đau đầu"
        var negationMatch = Regex.Match(lower, @"(?:không|chẳng|chưa)\s+(?:bị\s+|có\s+)?(sốt|ho|đau|mệt|khó thở|buồn nôn|nôn)[,\.\s]+(?:chỉ|mà\s+chỉ|mà|nhưng\s+chỉ|nhưng)\s+(?:bị\s+|có\s+)?(.*)$", RegexOptions.IgnoreCase);
        if (negationMatch.Success)
        {
            extractedSymptom = negationMatch.Groups[2].Value.Trim();
            if (!string.IsNullOrWhiteSpace(extractedSymptom))
            {
                return true;
            }
        }

        // Check each clinical keyword with word boundaries (\bkeyword\b)
        // This ensures "ho" does NOT match inside "cho tôi..."!
        foreach (var kw in ClinicalKeywordTerms)
        {
            var pattern = $@"\b{Regex.Escape(kw)}\b";
            if (Regex.IsMatch(lower, pattern, RegexOptions.IgnoreCase))
            {
                // If it's a mixed greeting ("chào bạn, tôi đau đầu hai ngày nay"), strip greeting
                var clean = Regex.Replace(lower, @"^(?:chào\s+(?:bạn|bác sĩ|bs|phòng khám|em)|xin\s+chào|alo|hello|hi)[,\.\!\?]?\s*", "", RegexOptions.IgnoreCase).Trim();
                extractedSymptom = SanitizeClinicalReason(!string.IsNullOrWhiteSpace(clean) ? clean : lower);
                return true;
            }
        }

        return false;
    }

    public static string MergeReasons(string? existingReason, string? newReason)
    {
        var sanitizedExisting = SanitizeClinicalReason(existingReason);
        var sanitizedNew = SanitizeClinicalReason(newReason);

        if (string.IsNullOrWhiteSpace(sanitizedExisting)) return sanitizedNew;
        if (string.IsNullOrWhiteSpace(sanitizedNew)) return sanitizedExisting;

        if (sanitizedExisting.Equals(sanitizedNew, StringComparison.OrdinalIgnoreCase))
        {
            return sanitizedExisting;
        }

        if (sanitizedNew.Contains(sanitizedExisting, StringComparison.OrdinalIgnoreCase))
        {
            return sanitizedNew;
        }

        if (sanitizedExisting.Contains(sanitizedNew, StringComparison.OrdinalIgnoreCase))
        {
            return sanitizedExisting;
        }

        return $"{sanitizedExisting}, {sanitizedNew}";
    }

    private static bool IsGibberish(string trimmed, string lower, string normalized)
    {
        if (trimmed.Length < 2) return true;

        if (Regex.IsMatch(normalized, @"[bcdfghjklmnpqrstvwxyz]{5,}", RegexOptions.IgnoreCase))
        {
            return true;
        }

        if (Regex.IsMatch(lower, @"(.)\1{4,}"))
        {
            return true;
        }

        var words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length > 0 && words.All(w => w.Length > 3 && !Regex.IsMatch(w, @"[aeiouy]")))
        {
            return true;
        }

        return false;
    }

    public static string? ExtractDateTokenFromText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var lower = text.Trim().ToLowerInvariant();

        var isoMatch = Regex.Match(lower, @"\b(\d{4}-\d{2}-\d{2})\b");
        if (isoMatch.Success) return isoMatch.Groups[1].Value;

        var dmyMatch = Regex.Match(lower, @"\b(\d{1,2}[/-]\d{1,2}[/-]\d{4})\b");
        if (dmyMatch.Success) return dmyMatch.Groups[1].Value.Replace('-', '/');

        if (lower.Contains("ngày kia") || lower.Contains("ngày mốt") || Regex.IsMatch(lower, @"\bmốt\b"))
            return "ngày kia";
        if (lower.Contains("ngày mai") || lower.Contains("sáng mai") || lower.Contains("chiều mai") || Regex.IsMatch(lower, @"\bmai\b"))
            return "ngày mai";
        if (lower.Contains("hôm nay") || lower.Contains("sáng nay") || lower.Contains("chiều nay"))
            return "hôm nay";

        var days = new[] { "thứ hai", "thứ 2", "thứ ba", "thứ 3", "thứ tư", "thứ 4", "thứ năm", "thứ 5", "thứ sáu", "thứ 6", "thứ bảy", "thứ 7", "chủ nhật" };
        foreach (var d in days)
        {
            if (lower.Contains(d)) return d;
        }

        return null;
    }

    private static bool DetectCorrection(string trimmed, string lower, out IntentClassificationResult result)
    {
        result = new IntentClassificationResult();

        // Pattern 1: "không phải [Bác sĩ A], tôi muốn [Bác sĩ B]"
        var negationMatch = Regex.Match(trimmed, @"(?:không phải|chẳng phải|không muốn khám|đổi khỏi)\s+(?:bác sĩ|bs\.|bs)?\s*([A-Za-z0-9À-ỹ\s]+?)[,;\.]?\s*(?:tôi muốn|chuyển sang|đổi sang|khám với|muốn)?\s*(?:bác sĩ|bs\.|bs)?\s*([A-Za-z0-9À-ỹ\s]+)$", RegexOptions.IgnoreCase);
        if (negationMatch.Success)
        {
            var negated = negationMatch.Groups[1].Value.Trim();
            var desired = negationMatch.Groups[2].Value.Trim();

            if (!string.IsNullOrWhiteSpace(negated) && !string.IsNullOrWhiteSpace(desired))
            {
                result.Intent = AiChatIntentTypes.ModifyDraft;
                result.IsCorrection = true;
                result.CorrectionTarget = "Doctor";
                result.NegatedDoctorName = negated;
                result.ExtractedDoctorName = desired;
                return true;
            }
        }

        // Pattern 2: "đổi sang ngày..." / "đổi ngày..." / "chuyển sang ngày..."
        if (lower.Contains("đổi sang ngày") || lower.Contains("đổi ngày") || lower.Contains("đổi lịch sang") ||
            lower.Contains("không khám hôm nay") || lower.Contains("chuyển sang ngày") || lower.Contains("dời sang ngày"))
        {
            result.Intent = AiChatIntentTypes.ModifyDraft;
            result.IsCorrection = true;
            result.CorrectionTarget = "Date";
            result.ExtractedDate = ExtractDateTokenFromText(lower);
            return true;
        }

        return false;
    }

    private static string NormalizeText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var normalized = text.Trim().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(c);
            if (cat != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c);
            }
        }
        return sb.ToString().Normalize(NormalizationForm.FormC).Replace("đ", "d").Replace("Đ", "D").ToLowerInvariant().Trim();
    }
}
