using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.AI.Interfaces;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Infrastructure.AI;

public class AiSpecialtyService : IAiSpecialtyService
{
    private readonly AppDbContext _dbContext;
    private readonly IAiSpecialtySuggestionProvider _aiProvider;
    private readonly IClinicAiContextService _clinicAiContextService;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ILogger<AiSpecialtyService> _logger;
    private readonly IAiSpecialtyClassifier? _classifier;

    public AiSpecialtyService(
        AppDbContext dbContext,
        IAiSpecialtySuggestionProvider aiProvider,
        IClinicAiContextService clinicAiContextService,
        IDateTimeProvider dateTimeProvider,
        ILogger<AiSpecialtyService> logger,
        IAiSpecialtyClassifier? classifier = null)
    {
        _dbContext = dbContext;
        _aiProvider = aiProvider;
        _clinicAiContextService = clinicAiContextService;
        _dateTimeProvider = dateTimeProvider;
        _logger = logger;
        _classifier = classifier;
    }

    public async Task<AiSuggestionResponseDto> GetSuggestionsAsync(AiSuggestionRequestDto request, CancellationToken cancellationToken = default)
    {
        var cleanInput = SanitizeInput(request.SymptomDescription);

        if (string.IsNullOrWhiteSpace(cleanInput) || cleanInput.Length < 10)
        {
            return new AiSuggestionResponseDto { Outcome = "INVALID_INPUT" };
        }

        var whitelistData = await GetActiveWhitelistAsync(cancellationToken);

        if (!whitelistData.Any())
        {
            _logger.LogWarning("AI Suggestion aborted: Whitelist is empty.");
            return new AiSuggestionResponseDto { Outcome = "MANUAL_SELECTION_REQUIRED" };
        }

        var sw = Stopwatch.StartNew();
        List<AiProviderSuggestionResult> aiResult;

        try
        {
            aiResult = await _aiProvider.GetSuggestionsFromAiAsync(cleanInput, whitelistData, cancellationToken);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "AI Provider call failed after {ElapsedMs}ms", sw.ElapsedMilliseconds);
            return new AiSuggestionResponseDto { Outcome = "UNAVAILABLE" };
        }

        sw.Stop();

        if (aiResult == null || !aiResult.Any())
        {
            _logger.LogInformation("AI returned no results. Outcome: MANUAL_SELECTION_REQUIRED. Latency: {ElapsedMs}ms", sw.ElapsedMilliseconds);
            return new AiSuggestionResponseDto { Outcome = "MANUAL_SELECTION_REQUIRED" };
        }

        var validSuggestions = new List<AiSpecialtySuggestionDto>();
        var addedCodes = new HashSet<string>();
        int rank = 1;

        foreach (var item in aiResult)
        {
            if (addedCodes.Contains(item.SpecialtyCode)) continue;

            var match = whitelistData.FirstOrDefault(w => w.Code == item.SpecialtyCode);
            if (match != null && !string.IsNullOrWhiteSpace(item.Reason) && item.Reason.Length <= 300)
            {
                validSuggestions.Add(new AiSpecialtySuggestionDto
                {
                    SpecialtyId = match.Id,
                    SpecialtyCode = match.Code,
                    SpecialtyName = match.Name,
                    Rank = rank++,
                    Reason = item.Reason.Trim()
                });
                addedCodes.Add(match.Code);
            }

            if (validSuggestions.Count == 3) break;
        }

        _logger.LogInformation("AI Success. WhitelistCount: {WCount}, ValidSuggestions: {VCount}, Latency: {ElapsedMs}ms", whitelistData.Count, validSuggestions.Count, sw.ElapsedMilliseconds);

        if (!validSuggestions.Any())
        {
            return new AiSuggestionResponseDto { Outcome = "MANUAL_SELECTION_REQUIRED" };
        }

        return new AiSuggestionResponseDto
        {
            Outcome = "SUCCESS",
            Suggestions = validSuggestions
        };
    }

    public async Task<AiChatResponseDto> ChatAsync(AiChatRequestDto request, CancellationToken cancellationToken = default)
    {
        var rawMessage = request.Message ?? string.Empty;
        var lowerMsg = rawMessage.ToLowerInvariant();

        // 1. EMERGENCY RULES - Executed FIRST before any AI provider or database search
        var emergencyKeywords = new[]
        {
            "đau ngực dữ dội", "khó thở nặng", "khó thở", "ngất", "lú lẫn",
            "đột quỵ", "co giật", "chảy máu", "nôn ra máu", "dị ứng nặng",
            "tự tử", "làm hại", "bất tỉnh", "hôn mê", "ngừng tim", "sốc phản vệ"
        };

        if (emergencyKeywords.Any(k => lowerMsg.Contains(k)))
        {
            return new AiChatResponseDto
            {
                Message = "Dấu hiệu bạn mô tả có thể là tình huống y tế khẩn cấp. Bạn hãy gọi ngay 115 hoặc đến ngay cơ sở cấp cứu gần nhất. Vui lòng không chờ đợi phản hồi qua kênh trò chuyện.",
                Urgency = "EMERGENCY",
                SafetyNotice = "TÌNH HUỐNG Y TẾ CẤP CỨU: Hãy gọi 115 hoặc đến ngay cơ sở y tế gần nhất.",
                PromptVersion = GeminiAiProvider.CurrentPromptVersion,
                SpecialtySuggestions = new List<AiSpecialtySuggestionDto>(),
                Actions = new List<AiActionDto>
                {
                    new AiActionDto
                    {
                        Id = "act-emergency-115",
                        Type = AiActionTypes.CallEmergency,
                        Label = "Gọi cấp cứu 115",
                        Description = "Kết nối trực tiếp đường dây nóng cấp cứu y tế 115",
                        Style = "danger",
                        RequiresAuthentication = false,
                        RequiresConfirmation = false,
                        Payload = new AiActionPayloadDto
                        {
                            TargetUrl = "tel:115"
                        }
                    }
                },
                ManualSelectionRequired = false
            };
        }

        // 2. PII FILTERING
        bool hasPhone = Regex.IsMatch(rawMessage, @"(?:0|\+84)[3|5|7|8|9]\d{8}");
        bool hasEmail = Regex.IsMatch(rawMessage, @"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}");
        bool hasId = Regex.IsMatch(rawMessage, @"\b\d{9}\b|\b\d{12}\b");

        if (hasPhone || hasEmail || hasId)
        {
            return new AiChatResponseDto
            {
                Message = "Hệ thống phát hiện thông tin cá nhân (SĐT/Email/CCCD) trong tin nhắn. Vì lý do bảo mật quyền riêng tư y tế, vui lòng không gửi thông tin định danh vào đây. Bạn hãy mô tả lại triệu chứng sức khỏe hoặc nhu cầu khám mà không kèm thông tin nhạy cảm nhé.",
                Urgency = "ROUTINE",
                SafetyNotice = "Bảo vệ thông tin cá nhân: Không cung cấp CCCD, SĐT hoặc Email trong hội thoại.",
                PromptVersion = GeminiAiProvider.CurrentPromptVersion,
                SpecialtySuggestions = new List<AiSpecialtySuggestionDto>(),
                Actions = new List<AiActionDto>(),
                ManualSelectionRequired = true
            };
        }

        // 3. PROMPT INJECTION DEFENSE
        var injectionKeywords = new[]
        {
            "bỏ qua quy tắc", "ignore previous", "bỏ qua hướng dẫn", "đóng vai bác sĩ",
            "hãy chẩn đoán", "kê thuốc", "xuất toàn bộ dữ liệu", "system prompt", "developer mode"
        };
        if (injectionKeywords.Any(k => lowerMsg.Contains(k)))
        {
            return new AiChatResponseDto
            {
                Message = "Tôi là Trợ lý ClinicCare AI hỗ trợ điều hướng chuyên khoa, lịch khám và thông tin phòng khám. Tôi tuân thủ nghiêm ngặt các quy tắc an toàn y khoa và không thực hiện các yêu cầu nằm ngoài phạm vi hỗ trợ.",
                Urgency = "ROUTINE",
                PromptVersion = GeminiAiProvider.CurrentPromptVersion,
                SpecialtySuggestions = new List<AiSpecialtySuggestionDto>(),
                Actions = new List<AiActionDto>(),
                ManualSelectionRequired = true
            };
        }

        var cleanMessage = SanitizeInput(rawMessage);
        if (string.IsNullOrWhiteSpace(cleanMessage))
        {
            return new AiChatResponseDto
            {
                Message = "Tin nhắn không hợp lệ. Vui lòng nhập triệu chứng hoặc câu hỏi của bạn.",
                PromptVersion = GeminiAiProvider.CurrentPromptVersion,
                ManualSelectionRequired = true
            };
        }

        // 4. ML.NET Classifier Evaluation (if enabled & present)
        SpecialtyClassificationResult? mlClassification = null;
        if (_classifier != null)
        {
            try
            {
                mlClassification = _classifier.ClassifySymptom(cleanMessage);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ML.NET specialty classification encountered an issue. Falling back cleanly.");
            }
        }

        // 5. Get DB Whitelist & Clinic Context
        var whitelistData = await GetActiveWhitelistAsync(cancellationToken);
        var clinicContextJson = await _clinicAiContextService.GetClinicContextJsonAsync(cancellationToken);

        var cleanContext = request.Context?
            .Select(c => new ChatMessageDto { Role = c.Role, Content = SanitizeInput(c.Content) })
            .ToList() ?? new List<ChatMessageDto>();

        // 6. Call AI Provider for Intent & Information Extraction
        AiChatProviderResult aiResult;
        try
        {
            aiResult = await _aiProvider.ChatWithAiAsync(cleanMessage, cleanContext, whitelistData, clinicContextJson, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI Provider chat failed.");
            return BuildFallbackResponse("Hệ thống AI đang quá tải hoặc gặp gián đoạn kết nối. Bạn có thể chọn chuyên khoa và bác sĩ trực tiếp bên dưới.", whitelistData);
        }

        if (aiResult == null || string.IsNullOrWhiteSpace(aiResult.Reply))
        {
            return BuildFallbackResponse("Không nhận được phản hồi từ trợ lý AI. Bạn vui lòng thử lại hoặc chọn khoa khám trực tiếp.", whitelistData);
        }

        // 7. DB GROUNDING & ACTION SYNTHESIS
        var responseDto = new AiChatResponseDto
        {
            Message = aiResult.Reply.Trim(),
            Urgency = string.IsNullOrWhiteSpace(aiResult.Urgency) ? "ROUTINE" : aiResult.Urgency,
            PromptVersion = GeminiAiProvider.CurrentPromptVersion
        };

        // Re-check emergency urgency from AI output
        if (string.Equals(responseDto.Urgency, "EMERGENCY", StringComparison.OrdinalIgnoreCase))
        {
            responseDto.SafetyNotice = "TÌNH HUỐNG Y TẾ CẤP CỨU: Hãy gọi 115 hoặc đến ngay cơ sở y tế gần nhất.";
            responseDto.Actions.Add(new AiActionDto
            {
                Id = "act-emergency-115",
                Type = AiActionTypes.CallEmergency,
                Label = "Gọi cấp cứu 115",
                Style = "danger",
                RequiresAuthentication = false,
                RequiresConfirmation = false,
                Payload = new AiActionPayloadDto { TargetUrl = "tel:115" }
            });
            return responseDto;
        }

        // 7.1 Ground Specialty Suggestions
        var suggestedCodes = new List<string>();
        if (mlClassification != null && !string.IsNullOrWhiteSpace(mlClassification.SpecialtyCode))
        {
            suggestedCodes.Add(mlClassification.SpecialtyCode);
        }
        if (aiResult.SuggestedSpecialtyCodes != null)
        {
            suggestedCodes.AddRange(aiResult.SuggestedSpecialtyCodes);
        }

        var addedCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int rank = 1;
        foreach (var code in suggestedCodes)
        {
            if (addedCodes.Contains(code)) continue;
            var match = whitelistData.FirstOrDefault(w => string.Equals(w.Code, code, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                responseDto.SpecialtySuggestions.Add(new AiSpecialtySuggestionDto
                {
                    SpecialtyId = match.Id,
                    SpecialtyCode = match.Code,
                    SpecialtyName = match.Name,
                    Rank = rank++,
                    Reason = "Phù hợp để tham khảo dựa trên thông tin triệu chứng bạn cung cấp."
                });
                addedCodes.Add(code);
            }
            if (responseDto.SpecialtySuggestions.Count == 3) break;
        }

        // 7.2 Synthesize Quick Navigation Actions if requested
        AddNavigationActionsIfRequested(cleanMessage, lowerMsg, aiResult.RequestedActionType, responseDto);

        // 7.3 Ground Booking Flow: Resolve Specialty, Doctor, Date, Slots
        await GroundBookingFlowAsync(request, cleanMessage, lowerMsg, aiResult, whitelistData, responseDto, cancellationToken);

        if (!responseDto.SpecialtySuggestions.Any() && !responseDto.Actions.Any())
        {
            responseDto.ManualSelectionRequired = true;
            responseDto.Actions.Add(new AiActionDto
            {
                Id = "act-manual-spec",
                Type = AiActionTypes.ManualSpecialtySelection,
                Label = "Chọn chuyên khoa thủ công",
                Style = "secondary",
                RequiresAuthentication = false,
                RequiresConfirmation = false,
                Payload = new AiActionPayloadDto { TargetUrl = "/patient/book" }
            });
        }

        return responseDto;
    }

    private async Task GroundBookingFlowAsync(
        AiChatRequestDto request,
        string cleanMessage,
        string lowerMsg,
        AiChatProviderResult aiResult,
        List<WhitelistItemDto> whitelistData,
        AiChatResponseDto responseDto,
        CancellationToken cancellationToken)
    {
        var vnToday = _dateTimeProvider.VietnamToday;
        var vnTime = _dateTimeProvider.VietnamTime;

        // A. Resolve Specialty
        WhitelistItemDto? targetSpecialty = null;
        if (request.PendingSpecialtyId.HasValue && request.PendingSpecialtyId.Value > 0)
        {
            targetSpecialty = whitelistData.FirstOrDefault(w => w.Id == request.PendingSpecialtyId.Value);
        }

        if (targetSpecialty == null && !string.IsNullOrWhiteSpace(aiResult.ExtractedSpecialtyCode))
        {
            targetSpecialty = whitelistData.FirstOrDefault(w => string.Equals(w.Code, aiResult.ExtractedSpecialtyCode, StringComparison.OrdinalIgnoreCase));
        }

        if (targetSpecialty == null && responseDto.SpecialtySuggestions.Any())
        {
            var topCode = responseDto.SpecialtySuggestions.First().SpecialtyCode;
            targetSpecialty = whitelistData.FirstOrDefault(w => string.Equals(w.Code, topCode, StringComparison.OrdinalIgnoreCase));
        }

        // B. Resolve Preferred Date
        DateOnly? targetDate = null;
        if (!string.IsNullOrWhiteSpace(request.PendingSlotDate) && DateOnly.TryParse(request.PendingSlotDate, out var parsedDate))
        {
            targetDate = parsedDate;
        }
        else if (!string.IsNullOrWhiteSpace(aiResult.ExtractedDate))
        {
            targetDate = ResolveRelativeDate(aiResult.ExtractedDate, vnToday);
        }
        else if (lowerMsg.Contains("hôm nay") || lowerMsg.Contains("chiều nay") || lowerMsg.Contains("sáng nay"))
        {
            targetDate = vnToday;
        }
        else if (lowerMsg.Contains("ngày mai") || lowerMsg.Contains("sáng mai") || lowerMsg.Contains("chiều mai"))
        {
            targetDate = vnToday.AddDays(1);
        }

        // Sunday Rule: Clinics do not operate on Sundays; move to Monday
        if (targetDate.HasValue && targetDate.Value.DayOfWeek == DayOfWeek.Sunday)
        {
            targetDate = targetDate.Value.AddDays(1);
        }

        // Default to next working date if looking for earliest slot
        if (!targetDate.HasValue && (aiResult.WantsEarliest || lowerMsg.Contains("sớm nhất")))
        {
            targetDate = vnToday.DayOfWeek == DayOfWeek.Sunday ? vnToday.AddDays(1) : vnToday;
        }

        // C. Resolve Doctor
        long? targetDoctorId = request.PendingDoctorId;
        string? targetDoctorName = null;
        string? targetDoctorAcademicTitle = null;

        if (targetSpecialty != null)
        {
            var currentSpecialty = targetSpecialty;
            var activeDoctorsInSpec = await (from ds in _dbContext.DoctorSpecialties
                                             join d in _dbContext.Doctors on ds.DoctorId equals d.Id
                                             join u in _dbContext.Users on d.UserId equals u.Id
                                             where ds.SpecialtyId == targetSpecialty.Id && d.IsActive && u.IsActive
                                             select new
                                             {
                                                 d.Id,
                                                 u.FullName,
                                                 d.AcademicTitle
                                             }).AsNoTracking().ToListAsync(cancellationToken);

            if (targetDoctorId.HasValue)
            {
                var docMatch = activeDoctorsInSpec.FirstOrDefault(d => d.Id == targetDoctorId.Value);
                if (docMatch != null)
                {
                    targetDoctorName = docMatch.FullName;
                    targetDoctorAcademicTitle = docMatch.AcademicTitle;
                }
                else
                {
                    targetDoctorId = null; // Discard invalid doctor ID
                }
            }
            else if (!string.IsNullOrWhiteSpace(aiResult.ExtractedDoctorName))
            {
                var search = aiResult.ExtractedDoctorName.Trim().ToLowerInvariant();
                var docMatch = activeDoctorsInSpec.FirstOrDefault(d => d.FullName.ToLowerInvariant().Contains(search));
                if (docMatch != null)
                {
                    targetDoctorId = docMatch.Id;
                    targetDoctorName = docMatch.FullName;
                    targetDoctorAcademicTitle = docMatch.AcademicTitle;
                }
            }

            // D. Query Available Slots
            var slotsQuery = _dbContext.AppointmentSlots
                .AsNoTracking()
                .Where(s => !s.IsBooked);

            if (targetDoctorId.HasValue)
            {
                slotsQuery = slotsQuery.Where(s => s.DoctorId == targetDoctorId.Value);
            }
            else
            {
                var docIds = activeDoctorsInSpec.Select(d => d.Id).ToList();
                slotsQuery = slotsQuery.Where(s => docIds.Contains(s.DoctorId));
            }

            if (targetDate.HasValue)
            {
                slotsQuery = slotsQuery.Where(s => s.SlotDate == targetDate.Value
                    && (s.SlotDate > vnToday || s.StartTime > vnTime));
            }
            else
            {
                var maxDate = vnToday.AddDays(7);
                slotsQuery = slotsQuery.Where(s => s.SlotDate >= vnToday && s.SlotDate <= maxDate
                    && (s.SlotDate > vnToday || s.StartTime > vnTime));
            }

            // Filter time preference if specified
            if (!string.IsNullOrWhiteSpace(aiResult.ExtractedTimePreference))
            {
                var pref = aiResult.ExtractedTimePreference.ToLowerInvariant();
                if (pref.Contains("sáng"))
                {
                    slotsQuery = slotsQuery.Where(s => s.StartTime < new TimeOnly(12, 0, 0));
                }
                else if (pref.Contains("chiều"))
                {
                    slotsQuery = slotsQuery.Where(s => s.StartTime >= new TimeOnly(12, 0, 0));
                }
            }

            var availableSlots = await slotsQuery
                .OrderBy(s => s.SlotDate)
                .ThenBy(s => s.StartTime)
                .Take(6)
                .ToListAsync(cancellationToken);

            // E. Resolve Selected Slot
            ClinicManagement.Domain.Entities.AppointmentSlot? chosenSlot = null;
            if (request.PendingSlotId.HasValue && request.PendingSlotId.Value > 0)
            {
                chosenSlot = await _dbContext.AppointmentSlots
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.Id == request.PendingSlotId.Value && !s.IsBooked
                        && (s.SlotDate > vnToday || (s.SlotDate == vnToday && s.StartTime > vnTime)), cancellationToken);

                if (chosenSlot != null)
                {
                    targetDoctorId = chosenSlot.DoctorId;
                    var doc = activeDoctorsInSpec.FirstOrDefault(d => d.Id == chosenSlot.DoctorId);
                    if (doc != null)
                    {
                        targetDoctorName = doc.FullName;
                        targetDoctorAcademicTitle = doc.AcademicTitle;
                    }
                    targetDate = chosenSlot.SlotDate;
                }
            }
            else if (aiResult.WantsEarliest && availableSlots.Any())
            {
                chosenSlot = availableSlots.First();
                targetDoctorId = chosenSlot.DoctorId;
                var doc = activeDoctorsInSpec.FirstOrDefault(d => d.Id == chosenSlot.DoctorId);
                if (doc != null)
                {
                    targetDoctorName = doc.FullName;
                    targetDoctorAcademicTitle = doc.AcademicTitle;
                }
                targetDate = chosenSlot.SlotDate;
            }

            // F. Build Booking Draft
            var reason = !string.IsNullOrWhiteSpace(request.Reason)
                ? request.Reason.Trim()
                : (!string.IsNullOrWhiteSpace(aiResult.ExtractedReason) ? aiResult.ExtractedReason.Trim() : cleanMessage);

            var draft = new AiBookingDraftDto
            {
                SpecialtyId = targetSpecialty.Id,
                SpecialtyName = targetSpecialty.Name,
                DoctorId = targetDoctorId,
                DoctorName = targetDoctorName != null && !string.IsNullOrWhiteSpace(targetDoctorAcademicTitle)
                    ? $"{targetDoctorAcademicTitle} {targetDoctorName}"
                    : targetDoctorName,
                SlotId = chosenSlot?.Id,
                SlotDate = chosenSlot?.SlotDate.ToString("yyyy-MM-dd") ?? targetDate?.ToString("yyyy-MM-dd"),
                StartTime = chosenSlot?.StartTime.ToString("HH:mm"),
                EndTime = chosenSlot?.EndTime.ToString("HH:mm"),
                Reason = reason,
                IsComplete = targetSpecialty != null && targetDoctorId.HasValue && chosenSlot != null && !string.IsNullOrWhiteSpace(reason)
            };

            responseDto.BookingDraft = draft;

            // Compute missing fields
            var missing = new List<string>();
            if (draft.SpecialtyId == null) missing.Add("Specialty");
            if (draft.DoctorId == null) missing.Add("Doctor");
            if (draft.SlotDate == null) missing.Add("DesiredDate");
            if (draft.SlotId == null) missing.Add("TimeSlot");
            responseDto.MissingFields = missing;

            // G. Add Contextual Actions
            if (draft.IsComplete)
            {
                // Confirmation Action (Requires Confirmation!)
                responseDto.Actions.Add(new AiActionDto
                {
                    Id = $"act-confirm-booking-{chosenSlot!.Id}",
                    Type = AiActionTypes.ConfirmBooking,
                    Label = "Xác nhận đặt lịch khám",
                    Description = $"Đặt lịch {draft.SpecialtyName} với {draft.DoctorName} vào {draft.StartTime} ngày {draft.SlotDate}",
                    Style = "primary",
                    RequiresAuthentication = true,
                    RequiresConfirmation = true,
                    Payload = new AiActionPayloadDto
                    {
                        SpecialtyId = draft.SpecialtyId,
                        SpecialtyName = draft.SpecialtyName,
                        DoctorId = draft.DoctorId,
                        DoctorName = draft.DoctorName,
                        SlotId = draft.SlotId,
                        SlotDate = draft.SlotDate,
                        StartTime = draft.StartTime,
                        EndTime = draft.EndTime,
                        Reason = draft.Reason
                    }
                });

                responseDto.Actions.Add(new AiActionDto
                {
                    Id = $"act-review-booking-{chosenSlot.Id}",
                    Type = AiActionTypes.ReviewBooking,
                    Label = "Xem tóm tắt thông tin khám",
                    Style = "secondary",
                    RequiresAuthentication = true,
                    RequiresConfirmation = false,
                    Payload = new AiActionPayloadDto
                    {
                        SpecialtyId = draft.SpecialtyId,
                        DoctorId = draft.DoctorId,
                        SlotId = draft.SlotId,
                        SlotDate = draft.SlotDate,
                        StartTime = draft.StartTime,
                        EndTime = draft.EndTime,
                        Reason = draft.Reason
                    }
                });
            }
            else
            {
                // If Doctor not selected, suggest SelectDoctor actions
                if (!targetDoctorId.HasValue && activeDoctorsInSpec.Any())
                {
                    foreach (var doc in activeDoctorsInSpec.Take(3))
                    {
                        var docDisplayName = string.IsNullOrWhiteSpace(doc.AcademicTitle) ? doc.FullName : $"{doc.AcademicTitle} {doc.FullName}";
                        responseDto.Actions.Add(new AiActionDto
                        {
                            Id = $"act-select-doc-{doc.Id}",
                            Type = AiActionTypes.SelectDoctor,
                            Label = $"Chọn {docDisplayName}",
                            Style = "secondary",
                            RequiresAuthentication = false,
                            RequiresConfirmation = false,
                            Payload = new AiActionPayloadDto
                            {
                                SpecialtyId = currentSpecialty.Id,
                                SpecialtyName = currentSpecialty.Name,
                                DoctorId = doc.Id,
                                DoctorName = docDisplayName,
                                AcademicTitle = doc.AcademicTitle,
                                SlotDate = targetDate?.ToString("yyyy-MM-dd")
                            }
                        });
                    }
                }
                // If Doctor selected but slot not chosen, suggest available slots
                else if (availableSlots.Any())
                {
                    foreach (var slot in availableSlots.Take(4))
                    {
                        responseDto.Actions.Add(new AiActionDto
                        {
                            Id = $"act-select-slot-{slot.Id}",
                            Type = AiActionTypes.SelectSlot,
                            Label = $"{slot.StartTime:HH\\:mm} - {slot.SlotDate:dd/MM}",
                            Style = "secondary",
                            RequiresAuthentication = false,
                            RequiresConfirmation = false,
                            Payload = new AiActionPayloadDto
                            {
                                SpecialtyId = currentSpecialty.Id,
                                SpecialtyName = currentSpecialty.Name,
                                DoctorId = slot.DoctorId,
                                DoctorName = targetDoctorName,
                                SlotId = slot.Id,
                                SlotDate = slot.SlotDate.ToString("yyyy-MM-dd"),
                                StartTime = slot.StartTime.ToString("HH:mm"),
                                EndTime = slot.EndTime.ToString("HH:mm")
                            }
                        });
                    }
                }

                // Action to view specialty details
                responseDto.Actions.Add(new AiActionDto
                {
                    Id = $"act-view-spec-{currentSpecialty.Id}",
                    Type = AiActionTypes.ViewSpecialty,
                    Label = $"Xem thông tin khoa {currentSpecialty.Name}",
                    Style = "secondary",
                    RequiresAuthentication = false,
                    RequiresConfirmation = false,
                    Payload = new AiActionPayloadDto
                    {
                        SpecialtyId = currentSpecialty.Id,
                        SpecialtyCode = currentSpecialty.Code,
                        SpecialtyName = currentSpecialty.Name
                    }
                });
            }
        }
    }

    private static void AddNavigationActionsIfRequested(
        string cleanMessage,
        string lowerMsg,
        string? requestedActionType,
        AiChatResponseDto responseDto)
    {
        // 1. Appointments
        if (string.Equals(requestedActionType, AiActionTypes.ViewMyAppointments, StringComparison.OrdinalIgnoreCase)
            || lowerMsg.Contains("lịch hẹn") || lowerMsg.Contains("lịch khám của tôi"))
        {
            responseDto.Actions.Add(new AiActionDto
            {
                Id = "act-nav-my-appointments",
                Type = AiActionTypes.ViewMyAppointments,
                Label = "Xem lịch hẹn của tôi",
                Description = "Mở danh sách các lịch khám sắp tới và lịch sử khám bệnh",
                Style = "primary",
                RequiresAuthentication = true,
                RequiresConfirmation = false,
                Payload = new AiActionPayloadDto { TargetUrl = "/patient/appointments" }
            });
        }

        // 2. Diagnostic Results
        if (string.Equals(requestedActionType, AiActionTypes.ViewDiagnosticResults, StringComparison.OrdinalIgnoreCase)
            || lowerMsg.Contains("xét nghiệm") || lowerMsg.Contains("cận lâm sàng") || lowerMsg.Contains("kết quả khám"))
        {
            responseDto.Actions.Add(new AiActionDto
            {
                Id = "act-nav-diagnostic-results",
                Type = AiActionTypes.ViewDiagnosticResults,
                Label = "Xem kết quả cận lâm sàng",
                Description = "Tra cứu phiếu chỉ định và kết quả xét nghiệm cá nhân",
                Style = "primary",
                RequiresAuthentication = true,
                RequiresConfirmation = false,
                Payload = new AiActionPayloadDto { TargetUrl = "/patient/diagnostic-results" }
            });
        }

        // 3. Prescriptions
        if (string.Equals(requestedActionType, AiActionTypes.ViewPrescriptions, StringComparison.OrdinalIgnoreCase)
            || lowerMsg.Contains("đơn thuốc") || lowerMsg.Contains("toa thuốc"))
        {
            responseDto.Actions.Add(new AiActionDto
            {
                Id = "act-nav-prescriptions",
                Type = AiActionTypes.ViewPrescriptions,
                Label = "Xem đơn thuốc",
                Description = "Tra cứu đơn thuốc và hướng dẫn sử dụng từ bác sĩ",
                Style = "secondary",
                RequiresAuthentication = true,
                RequiresConfirmation = false,
                Payload = new AiActionPayloadDto { TargetUrl = "/patient/prescriptions" }
            });
        }

        // 4. Bills
        if (string.Equals(requestedActionType, AiActionTypes.ViewBills, StringComparison.OrdinalIgnoreCase)
            || lowerMsg.Contains("hóa đơn") || lowerMsg.Contains("tiền khám") || lowerMsg.Contains("chi phí"))
        {
            responseDto.Actions.Add(new AiActionDto
            {
                Id = "act-nav-bills",
                Type = AiActionTypes.ViewBills,
                Label = "Xem hóa đơn của tôi",
                Description = "Xem chi tiết viện phí và các khoản thanh toán",
                Style = "secondary",
                RequiresAuthentication = true,
                RequiresConfirmation = false,
                Payload = new AiActionPayloadDto { TargetUrl = "/patient/bills" }
            });
        }

        // 5. Contact Reception
        if (string.Equals(requestedActionType, AiActionTypes.ContactReception, StringComparison.OrdinalIgnoreCase)
            || lowerMsg.Contains("lễ tân") || lowerMsg.Contains("tiếp đón") || lowerMsg.Contains("liên hệ phòng khám"))
        {
            responseDto.Actions.Add(new AiActionDto
            {
                Id = "act-contact-reception",
                Type = AiActionTypes.ContactReception,
                Label = "Liên hệ bàn tiếp đón lễ tân",
                Description = "Hỗ trợ trực tiếp tại quầy tiếp đón hoặc qua hotline",
                Style = "secondary",
                RequiresAuthentication = false,
                RequiresConfirmation = false,
                Payload = new AiActionPayloadDto { TargetUrl = "/contact" }
            });
        }
    }

    private static DateOnly? ResolveRelativeDate(string text, DateOnly today)
    {
        var clean = text.Trim().ToLowerInvariant();
        if (clean.Contains("hôm nay") || clean == "today") return today;
        if (clean.Contains("ngày mai") || clean == "tomorrow" || clean.Contains("mai")) return today.AddDays(1);
        if (clean.Contains("ngày kia") || clean.Contains("mốt")) return today.AddDays(2);

        if (DateOnly.TryParseExact(clean, new[] { "yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var exactDate))
        {
            return exactDate;
        }

        // Days of week
        var dayMap = new Dictionary<string, DayOfWeek>
        {
            { "thứ hai", DayOfWeek.Monday },
            { "thứ 2", DayOfWeek.Monday },
            { "thứ ba", DayOfWeek.Tuesday },
            { "thứ 3", DayOfWeek.Tuesday },
            { "thứ tư", DayOfWeek.Wednesday },
            { "thứ 4", DayOfWeek.Wednesday },
            { "thứ năm", DayOfWeek.Thursday },
            { "thứ 5", DayOfWeek.Thursday },
            { "thứ sáu", DayOfWeek.Friday },
            { "thứ 6", DayOfWeek.Friday },
            { "thứ bảy", DayOfWeek.Saturday },
            { "thứ 7", DayOfWeek.Saturday }
        };

        foreach (var entry in dayMap)
        {
            if (clean.Contains(entry.Key))
            {
                int diff = ((int)entry.Value - (int)today.DayOfWeek + 7) % 7;
                if (diff == 0) diff = 7; // Next week's day
                return today.AddDays(diff);
            }
        }

        return null;
    }

    private AiChatResponseDto BuildFallbackResponse(string message, List<WhitelistItemDto> whitelistData)
    {
        var response = new AiChatResponseDto
        {
            Message = message,
            Urgency = "ROUTINE",
            PromptVersion = GeminiAiProvider.CurrentPromptVersion,
            ManualSelectionRequired = true
        };

        foreach (var spec in whitelistData.Take(3))
        {
            response.SpecialtySuggestions.Add(new AiSpecialtySuggestionDto
            {
                SpecialtyId = spec.Id,
                SpecialtyCode = spec.Code,
                SpecialtyName = spec.Name,
                Rank = response.SpecialtySuggestions.Count + 1,
                Reason = "Chuyên khoa đang mở tiếp nhận lịch khám."
            });

            response.Actions.Add(new AiActionDto
            {
                Id = $"act-view-spec-{spec.Id}",
                Type = AiActionTypes.ViewSpecialty,
                Label = $"Xem khoa {spec.Name}",
                Style = "secondary",
                RequiresAuthentication = false,
                RequiresConfirmation = false,
                Payload = new AiActionPayloadDto
                {
                    SpecialtyId = spec.Id,
                    SpecialtyCode = spec.Code,
                    SpecialtyName = spec.Name
                }
            });
        }

        return response;
    }

    private async Task<List<WhitelistItemDto>> GetActiveWhitelistAsync(CancellationToken cancellationToken)
    {
        return await (from s in _dbContext.Specialties
                      where s.IsActive && s.AiEnabled
                      let activeDoctorCount = (
                          from ds in _dbContext.DoctorSpecialties
                          join d in _dbContext.Doctors on ds.DoctorId equals d.Id
                          join u in _dbContext.Users on d.UserId equals u.Id
                          where ds.SpecialtyId == s.Id && d.IsActive && u.IsActive
                          select d.Id
                      ).Count()
                      where activeDoctorCount > 0
                      select new WhitelistItemDto
                      {
                          Id = s.Id,
                          Code = s.SpecialtyCode,
                          Name = s.Name
                      }).AsNoTracking().ToListAsync(cancellationToken);
    }

    private static string SanitizeInput(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var text = input.Trim();

        // Remove emails
        text = Regex.Replace(text, @"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b", "[EMAIL_REMOVED]");

        // Remove phone numbers
        text = Regex.Replace(text, @"\b(?:\+84|0)(?:\d[\s.-]?){8,10}\b", "[PHONE_REMOVED]");

        // Remove continuous digits (likely IDs/CCCD)
        text = Regex.Replace(text, @"\b\d{6,}\b", "[ID_REMOVED]");

        return text;
    }
}
