using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.AI.Interfaces;
using Microsoft.ML;
using Microsoft.ML.Data;

namespace ClinicManagement.Infrastructure.AI;

public class IntentInferenceInput
{
    public string Text { get; set; } = string.Empty;
}

public class IntentInferenceOutput
{
    [ColumnName("PredictedLabel")]
    public string PredictedLabel { get; set; } = string.Empty;

    public float[] Score { get; set; } = Array.Empty<float>();
}

public class VietnameseIntentClassifier : IVietnameseIntentClassifier
{
    private static readonly object _mlLock = new();
    private static PredictionEngine<IntentInferenceInput, IntentInferenceOutput>? _mlEngine;
    private static bool _mlAttempted;
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

    private static readonly string[] ClinicalKeywords = new[]
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

        // 1. Check for Gibberish / Out of scope
        if (IsGibberish(trimmed, lower, normalized))
        {
            result.Intent = AiChatIntentTypes.UnclearOrOutOfScope;
            result.IsClear = false;
            result.ClarificationPrompt = "ClinicCare chưa hiểu rõ yêu cầu của bạn. Bạn có thể mô tả cụ thể hơn về triệu chứng sức khỏe, nhu cầu đặt lịch hoặc thông tin phòng khám cần tìm hiểu không ạ?";
            return result;
        }

        // 2. Cancellation Intent
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
        if (ConfirmationWords.Any(w => lower == w || lower.StartsWith(w + " ") || lower.EndsWith(" " + w)))
        {
            result.Intent = AiChatIntentTypes.ConfirmBooking;
            return result;
        }

        // Context-dependent "ok"
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

        // 6. Greetings ("xin chào", "hello", "hi")
        if (ExactGreetings.Contains(lower) || lower.StartsWith("chào ") || lower.StartsWith("xin chào"))
        {
            if (!ContainsClinicalEvidence(lower))
            {
                result.Intent = AiChatIntentTypes.Greeting;
                return result;
            }
        }

        // 7. Operational: Find Earliest Available Slot
        if (lower.Contains("sớm nhất") || lower.Contains("lúc nào sớm nhất") || lower.Contains("tìm lịch sớm nhất"))
        {
            result.Intent = AiChatIntentTypes.FindEarliestAvailableSlot;
            return result;
        }

        // 8. Pricing Inquiry
        if (lower.Contains("bảng giá") || lower.Contains("chi phí khám") || lower.Contains("giá khám") ||
            lower.Contains("bao nhiêu tiền") || lower.Contains("hết bao nhiêu tiền") || lower.Contains("tiền khám") ||
            lower.Contains("phí khám") || lower.Contains("giá dịch vụ") || lower.Contains("viện phí"))
        {
            result.Intent = AiChatIntentTypes.PricingInquiry;
            return result;
        }

        // 9. Facility / Reception Inquiry
        if (lower.Contains("lễ tân") || lower.Contains("tiếp đón") || lower.Contains("bàn tiếp đón") ||
            lower.Contains("hotline") || lower.Contains("số điện thoại") || lower.Contains("sđt") ||
            lower.Contains("địa chỉ") || lower.Contains("ở đâu") || lower.Contains("giờ mở cửa") ||
            lower.Contains("giờ làm việc") || lower.Contains("mấy giờ làm việc") || lower.Contains("liên hệ phòng khám") ||
            lower.Contains("chủ nhật có khám") || lower.Contains("có khám chủ nhật") || lower.Contains("phòng khám ở đâu"))
        {
            result.Intent = AiChatIntentTypes.FacilityInquiry;
            return result;
        }

        // 10. View Appointments
        if (lower.Contains("lịch hẹn của tôi") || lower.Contains("lịch đã đặt") || lower.Contains("xem lịch hẹn") ||
            lower.Contains("danh sách lịch hẹn") || lower.Contains("các lịch khám của tôi") || lower.Contains("tra cứu lịch hẹn"))
        {
            result.Intent = AiChatIntentTypes.ViewAppointments;
            return result;
        }

        // 11. Contextual Relative Doctor Selection ("người đầu", "bác sĩ đầu tiên", "bác sĩ 1")
        if (lower == "người đầu" || lower == "bác sĩ đầu tiên" || lower == "bác sĩ thứ nhất" || lower == "bác sĩ 1" ||
            lower == "người thứ nhất" || lower == "bác sĩ đầu")
        {
            result.Intent = AiChatIntentTypes.SelectDoctor;
            result.ExtractedDoctorName = "@first";
            return result;
        }

        // 12. Doctor Selection / Search with explicit doctor mention
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

        // 14. Start Booking Intent
        if (lower.StartsWith("tôi muốn đặt") || lower.StartsWith("muốn đặt lịch") || lower.StartsWith("đặt lịch") ||
            lower.StartsWith("đặt khám") || lower.StartsWith("đăng ký khám") || lower.StartsWith("muốn khám") ||
            lower.StartsWith("tôi muốn khám") || lower.StartsWith("tôi muốn hẹn") || lower.StartsWith("hẹn khám") ||
            lower.Contains("tư vấn giúp tôi") || lower.Contains("tư vấn cho tôi") || lower == "tư vấn" ||
            lower.Contains("xem lịch khám") || lower.Contains("xem lịch") || lower.Contains("lịch khám"))
        {
            if (!ContainsClinicalEvidence(lower))
            {
                result.Intent = AiChatIntentTypes.StartBooking;
                return result;
            }
        }

        // 15. Modify Draft Intent ("đổi ngày", "đổi bác sĩ", "đổi giờ")
        if (lower.StartsWith("đổi ngày") || lower.StartsWith("đổi bác sĩ") || lower.StartsWith("đổi giờ") ||
            lower.StartsWith("đổi khung giờ") || lower.StartsWith("chọn lại"))
        {
            result.Intent = AiChatIntentTypes.ModifyDraft;
            result.IsCorrection = true;
            return result;
        }

        // 16. Clinical Symptoms / Provide Reason
        if (ContainsClinicalEvidence(lower))
        {
            result.Intent = AiChatIntentTypes.ProvideReason;
            result.ExtractedReason = trimmed;
            return result;
        }

        // ML.NET Model Fallback for semantic generalization
        var engine = GetPredictionEngine();
        if (engine != null)
        {
            try
            {
                var mlPred = engine.Predict(new IntentInferenceInput { Text = trimmed });
                if (!string.IsNullOrWhiteSpace(mlPred.PredictedLabel))
                {
                    result.Intent = mlPred.PredictedLabel;
                    result.IsClear = mlPred.PredictedLabel != AiChatIntentTypes.UnclearOrOutOfScope;
                    if (!result.IsClear)
                    {
                        result.ClarificationPrompt = "ClinicCare chưa hiểu rõ yêu cầu của bạn. Bạn có thể mô tả cụ thể hơn về triệu chứng sức khỏe, nhu cầu đặt lịch hoặc thông tin phòng khám cần tìm hiểu không ạ?";
                    }
                    else if (result.Intent == AiChatIntentTypes.ProvideReason && ContainsClinicalEvidence(lower))
                    {
                        result.ExtractedReason = trimmed;
                    }
                    return result;
                }
            }
            catch
            {
                // Fallback to heuristic
            }
        }

        // Fallback: If not gibberish, keep IsClear = true so AI Provider can handle conversational turns
        if (!IsGibberish(trimmed, lower, normalized) && trimmed.Length >= 2)
        {
            result.Intent = ContainsClinicalEvidence(lower) ? AiChatIntentTypes.ProvideReason : AiChatIntentTypes.StartBooking;
            result.IsClear = true;
            if (result.Intent == AiChatIntentTypes.ProvideReason)
            {
                result.ExtractedReason = trimmed;
            }
            return result;
        }

        // Only true gibberish / nonsensical input reaches here
        result.Intent = AiChatIntentTypes.UnclearOrOutOfScope;
        result.IsClear = false;
        result.ClarificationPrompt = "ClinicCare chưa hiểu rõ yêu cầu của bạn. Bạn có thể mô tả cụ thể hơn về triệu chứng sức khỏe, nhu cầu đặt lịch hoặc thông tin phòng khám cần tìm hiểu không ạ?";
        return result;
    }

    private static PredictionEngine<IntentInferenceInput, IntentInferenceOutput>? GetPredictionEngine()
    {
        if (_mlAttempted) return _mlEngine;
        lock (_mlLock)
        {
            if (_mlAttempted) return _mlEngine;
            _mlAttempted = true;
            try
            {
                var candidates = new[]
                {
                    Path.Combine(AppContext.BaseDirectory, "models", "vietnamese_intent_classifier_v1.zip"),
                    Path.Combine(AppContext.BaseDirectory, "vietnamese_intent_classifier_v1.zip"),
                    Path.Combine(Directory.GetCurrentDirectory(), "models", "vietnamese_intent_classifier_v1.zip"),
                    Path.Combine(Directory.GetCurrentDirectory(), "src", "tools", "ClinicManagement.AI.Training", "models", "vietnamese_intent_classifier_v1.zip"),
                    Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "tools", "ClinicManagement.AI.Training", "models", "vietnamese_intent_classifier_v1.zip"),
                    Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "tools", "ClinicManagement.AI.Training", "models", "vietnamese_intent_classifier_v1.zip")
                };

                var modelPath = candidates.FirstOrDefault(File.Exists);
                if (modelPath != null)
                {
                    var mlContext = new MLContext(seed: 42);
                    var model = mlContext.Model.Load(modelPath, out _);
                    _mlEngine = mlContext.Model.CreatePredictionEngine<IntentInferenceInput, IntentInferenceOutput>(model);
                }
            }
            catch
            {
                _mlEngine = null;
            }
            return _mlEngine;
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
            lower == "ok" || lower == "oke" || lower == "chốt")
        {
            return true;
        }

        return IsGibberish(trimmed, lower, NormalizeText(trimmed));
    }

    public static bool IsPlausibleClinicalReason(string? text)
    {
        if (IsDisallowedReason(text)) return false;
        var trimmed = text!.Trim();
        var lower = trimmed.ToLowerInvariant();
        return ContainsClinicalEvidence(lower) || trimmed.Length >= 10;
    }

    private static bool ContainsClinicalEvidence(string lower)
    {
        return ClinicalKeywords.Any(k => lower.Contains(k));
    }

    private static bool IsGibberish(string trimmed, string lower, string normalized)
    {
        if (trimmed.Length < 2) return true;

        // Check for common consonant clusters in keyboard smashing (e.g. "jsdkjvsdcj", "sdjf", "asdfgh")
        if (Regex.IsMatch(normalized, @"[bcdfghjklmnpqrstvwxyz]{5,}", RegexOptions.IgnoreCase))
        {
            return true;
        }

        // Check for repeated random characters (e.g. "aaaaa", "asdasd")
        if (Regex.IsMatch(lower, @"(.)\1{4,}"))
        {
            return true;
        }

        // Has words with no vowels
        var words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length > 0 && words.All(w => w.Length > 3 && !Regex.IsMatch(w, @"[aeiouy]")))
        {
            return true;
        }

        return false;
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

        // Pattern 2: "đổi sang ngày mai" / "đổi ngày"
        if (lower.Contains("đổi sang ngày") || lower.Contains("đổi ngày") || lower.Contains("không khám hôm nay"))
        {
            result.Intent = AiChatIntentTypes.ModifyDraft;
            result.IsCorrection = true;
            result.CorrectionTarget = "Date";
            if (lower.Contains("mai")) result.ExtractedDate = "mai";
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
