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
    private const int EarliestSlotSearchHorizonDays = 7;
    private readonly AppDbContext _dbContext;
    private readonly IAiSpecialtySuggestionProvider _aiProvider;
    private readonly IClinicAiContextService _clinicAiContextService;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ILogger<AiSpecialtyService> _logger;
    private readonly ClinicManagement.Application.Appointments.Interfaces.IAppointmentAvailabilityPolicy _availabilityPolicy;
    private readonly ClinicManagement.Application.Authentication.Interfaces.ICurrentUserService _currentUserService;
    private readonly IAiSpecialtyClassifier? _classifier;

    public AiSpecialtyService(
        AppDbContext dbContext,
        IAiSpecialtySuggestionProvider aiProvider,
        IClinicAiContextService clinicAiContextService,
        IDateTimeProvider dateTimeProvider,
        ILogger<AiSpecialtyService> logger,
        ClinicManagement.Application.Appointments.Interfaces.IAppointmentAvailabilityPolicy availabilityPolicy,
        ClinicManagement.Application.Authentication.Interfaces.ICurrentUserService currentUserService,
        IAiSpecialtyClassifier? classifier = null)
    {
        _dbContext = dbContext;
        _aiProvider = aiProvider;
        _clinicAiContextService = clinicAiContextService;
        _dateTimeProvider = dateTimeProvider;
        _logger = logger;
        _availabilityPolicy = availabilityPolicy;
        _currentUserService = currentUserService;
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
        if (ContainsActiveEmergency(lowerMsg))
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
        bool hasPhone = Regex.IsMatch(rawMessage, @"(?:\+84|0)[35789]\d{8}");
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

        // Operational booking intents are resolved from authenticated context and the
        // canonical availability policy. They must not wait for or depend on an AI provider.
        if (string.Equals(request.Intent, AiChatIntentTypes.FindEarliestAvailableSlot, StringComparison.Ordinal))
        {
            return await FindEarliestAvailableSlotsAsync(request, cancellationToken);
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
        var validUrgencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ROUTINE", "SOON", "EMERGENCY" };
        var cleanUrgency = (!string.IsNullOrWhiteSpace(aiResult.Urgency) && validUrgencies.Contains(aiResult.Urgency.Trim()))
            ? aiResult.Urgency.Trim().ToUpperInvariant()
            : "ROUTINE";

        var sanitizedReply = await ComposeGroundedReplyAsync(aiResult.Reply, whitelistData, cancellationToken);

        var responseDto = new AiChatResponseDto
        {
            Message = sanitizedReply,
            Urgency = cleanUrgency,
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
        await AddNavigationActionsIfRequestedAsync(cleanMessage, lowerMsg, aiResult.RequestedActionType, responseDto, cancellationToken);

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
                Payload = new AiActionPayloadDto { TargetUrl = SafeRoutes.BookAppointment }
            });
        }

        // Ensure all returned actions conform to security allowlist and safety rules, capped at max 6 actions
        responseDto.Actions = responseDto.Actions
            .Where(a => AiActionValidator.Validate(a, out _))
            .Take(6)
            .ToList();

        responseDto.SpecialtySuggestions = responseDto.SpecialtySuggestions
            .Take(3)
            .ToList();

        return responseDto;
    }

    private async Task<AiChatResponseDto> FindEarliestAvailableSlotsAsync(
        AiChatRequestDto request,
        CancellationToken cancellationToken)
    {
        var response = new AiChatResponseDto
        {
            Urgency = "ROUTINE",
            PromptVersion = GeminiAiProvider.CurrentPromptVersion
        };

        var reason = request.Reason?.Trim();
        if (!request.PendingSpecialtyId.HasValue)
        {
            response.Message = "Bạn chưa chọn chuyên khoa. Vui lòng chọn chuyên khoa hoặc mô tả triệu chứng để hệ thống tìm lịch phù hợp.";
            response.ManualSelectionRequired = true;
            response.MissingFields = new List<string> { "Specialty", "Doctor", "TimeSlot" };
            if (!AiActionValidator.IsValidBookingReason(reason))
            {
                response.MissingFields.Add("Reason");
            }
            response.Actions.Add(BuildManualSpecialtySelectionAction());
            return response;
        }

        var specialty = await _dbContext.Specialties
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == request.PendingSpecialtyId.Value, cancellationToken);

        if (specialty == null || !specialty.IsActive)
        {
            response.Message = "Chuyên khoa đã chọn không tồn tại hoặc đã ngừng hoạt động. Vui lòng chọn lại chuyên khoa.";
            response.ManualSelectionRequired = true;
            response.MissingFields = new List<string> { "Specialty", "Doctor", "TimeSlot" };
            response.Actions.Add(BuildManualSpecialtySelectionAction());
            return response;
        }

        if (!specialty.AiEnabled)
        {
            response.Message = $"Chuyên khoa {specialty.Name} hiện chưa hỗ trợ tìm lịch qua trợ lý AI. Vui lòng chọn lịch trực tiếp trên trang đặt khám.";
            response.ManualSelectionRequired = true;
            response.BookingDraft = new AiBookingDraftDto
            {
                SpecialtyId = specialty.Id,
                SpecialtyName = specialty.Name,
                Reason = reason,
                IsComplete = false
            };
            response.MissingFields = new List<string> { "Doctor", "TimeSlot" };
            return response;
        }

        var searchFrom = _dateTimeProvider.VietnamToday;
        if (!string.IsNullOrWhiteSpace(request.PendingSlotDate))
        {
            if (!DateOnly.TryParseExact(
                    request.PendingSlotDate,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out searchFrom))
            {
                response.Message = "Ngày bắt đầu tìm lịch không hợp lệ. Vui lòng chọn lại ngày theo định dạng ngày/tháng/năm.";
                response.BookingDraft = BuildSearchDraft(specialty.Id, specialty.Name, request.PendingDoctorId, null, null, reason);
                response.MissingFields = new List<string> { "DesiredDate", "TimeSlot" };
                return response;
            }
        }

        if (searchFrom < _dateTimeProvider.VietnamToday)
        {
            response.Message = "Ngày bắt đầu tìm lịch đã qua. Vui lòng chọn hôm nay hoặc một ngày trong tương lai.";
            response.BookingDraft = BuildSearchDraft(specialty.Id, specialty.Name, request.PendingDoctorId, null, null, reason);
            response.MissingFields = new List<string> { "DesiredDate", "TimeSlot" };
            return response;
        }

        var activeDoctors = await (from ds in _dbContext.DoctorSpecialties
                                   join d in _dbContext.Doctors on ds.DoctorId equals d.Id
                                   join u in _dbContext.Users on d.UserId equals u.Id
                                   where ds.SpecialtyId == specialty.Id && d.IsActive && u.IsActive
                                   orderby d.Id
                                   select new
                                   {
                                       d.Id,
                                       u.FullName,
                                       d.AcademicTitle
                                   })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        if (activeDoctors.Count == 0)
        {
            response.Message = $"Hiện không có bác sĩ đang hoạt động thuộc chuyên khoa {specialty.Name}.";
            response.BookingDraft = BuildSearchDraft(specialty.Id, specialty.Name, null, null, searchFrom, reason);
            response.MissingFields = new List<string> { "Doctor", "TimeSlot" };
            return response;
        }

        var selectedDoctor = request.PendingDoctorId.HasValue
            ? activeDoctors.FirstOrDefault(d => d.Id == request.PendingDoctorId.Value)
            : null;

        if (request.PendingDoctorId.HasValue && selectedDoctor == null)
        {
            var doctorIsActive = await _dbContext.Doctors
                .Join(_dbContext.Users, d => d.UserId, u => u.Id, (d, u) => new { d, u })
                .AnyAsync(x => x.d.Id == request.PendingDoctorId.Value && x.d.IsActive && x.u.IsActive, cancellationToken);

            response.Message = doctorIsActive
                ? $"Bác sĩ đã chọn không thuộc chuyên khoa {specialty.Name}. Vui lòng chọn lại bác sĩ."
                : "Bác sĩ đã chọn không tồn tại hoặc đã ngừng hoạt động. Vui lòng chọn lại bác sĩ.";
            response.BookingDraft = BuildSearchDraft(specialty.Id, specialty.Name, null, null, searchFrom, reason);
            response.MissingFields = new List<string> { "Doctor", "TimeSlot" };
            return response;
        }

        if (!_currentUserService.UserId.HasValue)
        {
            response.Message = "Không thể xác định tài khoản bệnh nhân để kiểm tra lịch trùng. Vui lòng đăng nhập lại.";
            response.BookingDraft = BuildSearchDraft(specialty.Id, specialty.Name, selectedDoctor?.Id, FormatDoctorName(selectedDoctor?.AcademicTitle, selectedDoctor?.FullName), searchFrom, reason);
            response.MissingFields = new List<string> { "TimeSlot" };
            return response;
        }

        var patientId = await _dbContext.Patients
            .AsNoTracking()
            .Where(p => p.UserId == _currentUserService.UserId.Value)
            .Select(p => (long?)p.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (!patientId.HasValue)
        {
            response.Message = "Không thể xác định hồ sơ bệnh nhân để kiểm tra lịch trùng. Vui lòng liên hệ quản trị hệ thống.";
            response.BookingDraft = BuildSearchDraft(specialty.Id, specialty.Name, selectedDoctor?.Id, FormatDoctorName(selectedDoctor?.AcademicTitle, selectedDoctor?.FullName), searchFrom, reason);
            response.MissingFields = new List<string> { "TimeSlot" };
            return response;
        }

        var searchTo = searchFrom.AddDays(EarliestSlotSearchHorizonDays);
        var slots = await _availabilityPolicy.GetAvailableSlotsAsync(
            new ClinicManagement.Application.Appointments.Interfaces.BatchSlotAvailabilityRequest
            {
                DoctorId = selectedDoctor?.Id,
                DoctorIds = selectedDoctor == null ? activeDoctors.Select(d => d.Id).ToList() : null,
                SpecialtyId = specialty.Id,
                FromDate = searchFrom,
                ToDate = searchTo,
                PatientId = patientId,
                CheckAiEnabledSpecialty = true,
                Limit = 6,
                ThrowOnValidationFailure = true
            },
            cancellationToken);

        var doctorLookup = activeDoctors.ToDictionary(d => d.Id);
        response.BookingDraft = BuildSearchDraft(
            specialty.Id,
            specialty.Name,
            selectedDoctor?.Id,
            FormatDoctorName(selectedDoctor?.AcademicTitle, selectedDoctor?.FullName),
            searchFrom,
            reason);
        response.MissingFields = new List<string>();
        if (selectedDoctor == null)
        {
            response.MissingFields.Add("Doctor");
        }
        response.MissingFields.Add("TimeSlot");
        if (!AiActionValidator.IsValidBookingReason(reason))
        {
            response.MissingFields.Add("Reason");
        }

        if (slots.Count == 0)
        {
            var doctorScope = selectedDoctor == null
                ? $"các bác sĩ đang hoạt động của chuyên khoa {specialty.Name}"
                : FormatDoctorName(selectedDoctor.AcademicTitle, selectedDoctor.FullName);
            response.Message = $"Không có lịch trống phù hợp của {doctorScope} trong khoảng {searchFrom:dd/MM/yyyy} đến {searchTo:dd/MM/yyyy}. Bạn có thể chọn ngày bắt đầu khác để tìm tiếp.";
            return response;
        }

        var selectedScope = selectedDoctor == null
            ? "tất cả bác sĩ phù hợp"
            : FormatDoctorName(selectedDoctor.AcademicTitle, selectedDoctor.FullName);
        response.Message = $"Mình đang dùng chuyên khoa {specialty.Name} bạn vừa chọn và tìm trong khoảng {searchFrom:dd/MM/yyyy} đến {searchTo:dd/MM/yyyy} cho {selectedScope}. Dưới đây là các lịch trống lấy trực tiếp từ hệ thống, theo thứ tự sớm nhất. Đây mới là gợi ý, chưa phải lịch đã đặt.";

        foreach (var slot in slots)
        {
            if (!doctorLookup.TryGetValue(slot.DoctorId, out var doctor))
            {
                continue;
            }

            var doctorName = FormatDoctorName(doctor.AcademicTitle, doctor.FullName);
            response.Actions.Add(new AiActionDto
            {
                Id = $"act-select-slot-{slot.SlotId}",
                Type = AiActionTypes.SelectSlot,
                Label = $"{slot.StartTime:HH\\:mm} - {slot.SlotDate:dd/MM} · {doctorName}",
                Description = $"Chọn lịch trống của {doctorName} thuộc chuyên khoa {specialty.Name}",
                Style = "primary",
                RequiresAuthentication = false,
                RequiresConfirmation = false,
                Payload = new AiActionPayloadDto
                {
                    SpecialtyId = specialty.Id,
                    SpecialtyCode = specialty.SpecialtyCode,
                    SpecialtyName = specialty.Name,
                    DoctorId = doctor.Id,
                    DoctorName = doctorName,
                    AcademicTitle = doctor.AcademicTitle,
                    SlotId = slot.SlotId,
                    SlotDate = slot.SlotDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    StartTime = slot.StartTime.ToString("HH:mm", CultureInfo.InvariantCulture),
                    EndTime = slot.EndTime.ToString("HH:mm", CultureInfo.InvariantCulture),
                    Reason = reason
                }
            });
        }

        response.Actions = response.Actions
            .Where(a => AiActionValidator.Validate(a, out _))
            .Take(6)
            .ToList();
        return response;
    }

    private static AiBookingDraftDto BuildSearchDraft(
        long specialtyId,
        string specialtyName,
        long? doctorId,
        string? doctorName,
        DateOnly? searchFrom,
        string? reason)
    {
        return new AiBookingDraftDto
        {
            SpecialtyId = specialtyId,
            SpecialtyName = specialtyName,
            DoctorId = doctorId,
            DoctorName = doctorName,
            SlotDate = searchFrom?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Reason = reason,
            IsComplete = false
        };
    }

    private static string? FormatDoctorName(string? academicTitle, string? fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(academicTitle)
            ? fullName.Trim()
            : $"{academicTitle.Trim()} {fullName.Trim()}";
    }

    private static AiActionDto BuildManualSpecialtySelectionAction()
    {
        return new AiActionDto
        {
            Id = "act-manual-spec",
            Type = AiActionTypes.ManualSpecialtySelection,
            Label = "Chọn chuyên khoa",
            Style = "secondary",
            RequiresAuthentication = false,
            RequiresConfirmation = false,
            Payload = new AiActionPayloadDto { TargetUrl = SafeRoutes.BookAppointment }
        };
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

            if (targetSpecialty == null)
            {
                responseDto.Message = "Chuyên khoa đã chọn không tồn tại, đã ngừng hoạt động hoặc chưa hỗ trợ AI. Vui lòng chọn lại chuyên khoa.";
                responseDto.ManualSelectionRequired = true;
                responseDto.MissingFields = new List<string> { "Specialty", "Doctor", "TimeSlot" };
                responseDto.Actions.Add(BuildManualSpecialtySelectionAction());
                return;
            }
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
        if (!string.IsNullOrWhiteSpace(request.PendingSlotDate) &&
            DateOnly.TryParseExact(request.PendingSlotDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
        {
            targetDate = parsedDate;
        }
        else if (!string.IsNullOrWhiteSpace(request.PendingSlotDate))
        {
            responseDto.Message = "Ngày khám đã chọn không hợp lệ. Vui lòng chọn lại ngày theo định dạng ngày/tháng/năm.";
            responseDto.MissingFields = new List<string> { "DesiredDate", "TimeSlot" };
            return;
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

        // Sunday Rule: Detect if user selected Sunday
        bool requestedSunday = false;
        DateOnly sundayDate = default;
        if (targetDate.HasValue && targetDate.Value.DayOfWeek == DayOfWeek.Sunday)
        {
            requestedSunday = true;
            sundayDate = targetDate.Value;
            targetDate = null; // Do NOT silently move to Monday
        }

        // Default to next working date if looking for earliest slot
        if (!targetDate.HasValue && !requestedSunday && (aiResult.WantsEarliest || lowerMsg.Contains("sớm nhất")))
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
                                             }).AsNoTracking().OrderBy(d => d.Id).ToListAsync(cancellationToken);

            if (activeDoctorsInSpec.Count == 0)
            {
                var unresolvedReason = !string.IsNullOrWhiteSpace(request.Reason)
                    ? request.Reason.Trim()
                    : RecoverInitialReason(cleanMessage, request.Context, aiResult.ExtractedReason);
                responseDto.Message = $"Hiện không có bác sĩ đang hoạt động thuộc chuyên khoa {targetSpecialty.Name}.";
                responseDto.BookingDraft = new AiBookingDraftDto
                {
                    SpecialtyId = targetSpecialty.Id,
                    SpecialtyName = targetSpecialty.Name,
                    SlotDate = targetDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    Reason = unresolvedReason,
                    IsComplete = false
                };
                responseDto.MissingFields = new List<string> { "Doctor", "TimeSlot" };
                return;
            }

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
                    var doctorIsActive = await _dbContext.Doctors
                        .Join(_dbContext.Users, d => d.UserId, u => u.Id, (d, u) => new { d, u })
                        .AnyAsync(x => x.d.Id == targetDoctorId.Value && x.d.IsActive && x.u.IsActive, cancellationToken);
                    responseDto.Message = doctorIsActive
                        ? $"Bác sĩ đã chọn không thuộc chuyên khoa {targetSpecialty.Name}. Vui lòng chọn lại bác sĩ."
                        : "Bác sĩ đã chọn không tồn tại hoặc đã ngừng hoạt động. Vui lòng chọn lại bác sĩ.";
                    responseDto.BookingDraft = new AiBookingDraftDto
                    {
                        SpecialtyId = targetSpecialty.Id,
                        SpecialtyName = targetSpecialty.Name,
                        SlotDate = targetDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        Reason = request.Reason?.Trim(),
                        IsComplete = false
                    };
                    responseDto.MissingFields = new List<string> { "Doctor", "TimeSlot" };
                    return;
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

            // Sunday Rule: If user requested Sunday, do NOT query slots; inform user and suggest Monday
            if (requestedSunday)
            {
                var nextMonday = sundayDate.AddDays(1);
                responseDto.Message = $"Phòng khám không mở lịch khám vào Chủ nhật ({sundayDate:dd/MM/yyyy}). Bạn có thể chọn ngày làm việc kế tiếp (Thứ Hai, {nextMonday:dd/MM/yyyy}) hoặc một ngày khác nhé.";

                var sundayReason = !string.IsNullOrWhiteSpace(request.Reason)
                    ? request.Reason.Trim()
                    : RecoverInitialReason(cleanMessage, request.Context, aiResult.ExtractedReason);

                responseDto.BookingDraft = new AiBookingDraftDto
                {
                    SpecialtyId = targetSpecialty.Id,
                    SpecialtyName = targetSpecialty.Name,
                    DoctorId = targetDoctorId,
                    DoctorName = targetDoctorName,
                    Reason = sundayReason,
                    IsComplete = false
                };

                responseDto.Actions.Add(new AiActionDto
                {
                    Id = $"act-change-date-{nextMonday:yyyyMMdd}",
                    Type = AiActionTypes.ChangePreferredDate,
                    Label = $"Đổi sang Thứ Hai ({nextMonday:dd/MM})",
                    Description = $"Chọn ngày làm việc kế tiếp: {nextMonday:dd/MM/yyyy}",
                    Style = "primary",
                    RequiresAuthentication = false,
                    RequiresConfirmation = false,
                    Payload = new AiActionPayloadDto
                    {
                        SpecialtyId = targetSpecialty.Id,
                        SpecialtyName = targetSpecialty.Name,
                        DoctorId = targetDoctorId,
                        DoctorName = targetDoctorName,
                        SlotDate = nextMonday.ToString("yyyy-MM-dd"),
                        Reason = sundayReason
                    }
                });
                return;
            }

            // D. Query Available Slots via Canonical Availability Policy
            long? currentPatientId = null;
            if (_currentUserService.UserId.HasValue)
            {
                var pat = await _dbContext.Patients
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.UserId == _currentUserService.UserId.Value, cancellationToken);
                currentPatientId = pat?.Id;
            }

            var batchRequest = new ClinicManagement.Application.Appointments.Interfaces.BatchSlotAvailabilityRequest
            {
                DoctorId = targetDoctorId,
                DoctorIds = targetDoctorId.HasValue ? null : activeDoctorsInSpec.Select(d => d.Id).ToList(),
                SpecialtyId = targetSpecialty.Id,
                FromDate = targetDate ?? vnToday,
                ToDate = targetDate ?? vnToday.AddDays(7),
                CheckAiEnabledSpecialty = true,
                TimePreference = aiResult.ExtractedTimePreference,
                Limit = 6,
                PatientId = currentPatientId
            };

            var availableSlotDtos = await _availabilityPolicy.GetAvailableSlotsAsync(batchRequest, cancellationToken);
            var availableSlots = availableSlotDtos.Select(s => new ClinicManagement.Domain.Entities.AppointmentSlot
            {
                Id = s.SlotId,
                DoctorId = s.DoctorId,
                SlotDate = s.SlotDate,
                StartTime = s.StartTime,
                EndTime = s.EndTime
            }).ToList();

            // E. Resolve Selected Slot via Canonical Availability Policy
            ClinicManagement.Domain.Entities.AppointmentSlot? chosenSlot = null;
            if (request.PendingSlotId.HasValue && request.PendingSlotId.Value > 0)
            {
                var availResult = await _availabilityPolicy.EvaluateSlotAvailabilityAsync(new ClinicManagement.Application.Appointments.Interfaces.SlotAvailabilityRequest
                {
                    SlotId = request.PendingSlotId.Value,
                    DoctorId = targetDoctorId,
                    SpecialtyId = targetSpecialty.Id,
                    PatientId = currentPatientId,
                    CheckAiEnabledSpecialty = true
                }, cancellationToken);

                if (availResult.IsAvailable)
                {
                    targetDoctorId = availResult.DoctorId;
                    targetDoctorName = availResult.DoctorName;
                    targetDoctorAcademicTitle = availResult.AcademicTitle;
                    targetDate = availResult.SlotDate;
                    chosenSlot = new ClinicManagement.Domain.Entities.AppointmentSlot
                    {
                        Id = availResult.SlotId!.Value,
                        DoctorId = availResult.DoctorId!.Value,
                        SlotDate = availResult.SlotDate!.Value,
                        StartTime = availResult.StartTime!.Value,
                        EndTime = availResult.EndTime!.Value
                    };
                }
                else
                {
                    responseDto.Message = $"Khung giờ đã chọn không khả dụng ({availResult.FailureReason}). Vui lòng chọn một khung giờ khác bên dưới.";
                    chosenSlot = null;
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
                : RecoverInitialReason(cleanMessage, request.Context, aiResult.ExtractedReason);
            var hasValidReason = AiActionValidator.IsValidBookingReason(reason);

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
                IsComplete = targetDoctorId.HasValue && chosenSlot != null && hasValidReason
            };

            responseDto.BookingDraft = draft;

            // Compute missing fields
            var missing = new List<string>();
            if (draft.SpecialtyId == null) missing.Add("Specialty");
            if (draft.DoctorId == null) missing.Add("Doctor");
            if (draft.SlotDate == null) missing.Add("DesiredDate");
            if (draft.SlotId == null) missing.Add("TimeSlot");
            if (!hasValidReason) missing.Add("Reason");
            responseDto.MissingFields = missing;

            if (chosenSlot != null && !hasValidReason)
            {
                responseDto.Message = "Đã ghi nhận chuyên khoa, bác sĩ và khung giờ bạn chọn. Vui lòng bổ sung lý do khám từ 10 đến 500 ký tự trước khi xem lại và xác nhận đặt lịch.";
            }
            else if (availableSlots.Count == 0 && chosenSlot == null && !request.PendingSlotId.HasValue)
            {
                var rangeFrom = batchRequest.FromDate;
                var rangeTo = batchRequest.ToDate;
                responseDto.Message = $"Không có lịch trống phù hợp trong khoảng {rangeFrom:dd/MM/yyyy} đến {rangeTo:dd/MM/yyyy}. Vui lòng chọn một khoảng ngày khác để tìm tiếp.";
            }

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

                    if (targetDoctorId.HasValue && !responseDto.Actions.Any(a => a.Type == AiActionTypes.ViewAvailableSlots))
                    {
                        responseDto.Actions.Add(new AiActionDto
                        {
                            Id = $"act-view-available-slots-{targetDoctorId.Value}",
                            Type = AiActionTypes.ViewAvailableSlots,
                            Label = "Xem tất cả lịch trống",
                            Description = $"Xem các khung giờ khám còn trống của {targetDoctorName}",
                            Style = "secondary",
                            RequiresAuthentication = false,
                            RequiresConfirmation = false,
                            Payload = new AiActionPayloadDto
                            {
                                SpecialtyId = currentSpecialty.Id,
                                SpecialtyName = currentSpecialty.Name,
                                DoctorId = targetDoctorId,
                                DoctorName = targetDoctorName,
                                SlotDate = targetDate?.ToString("yyyy-MM-dd")
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

    private async Task AddNavigationActionsIfRequestedAsync(
        string cleanMessage,
        string lowerMsg,
        string? requestedActionType,
        AiChatResponseDto responseDto,
        CancellationToken cancellationToken = default)
    {
        var currentUserId = _currentUserService?.UserId;
        long? verifiedAppointmentId = null;
        string? verifiedAppointmentCode = null;

        if (currentUserId.HasValue && currentUserId.Value != Guid.Empty)
        {
            var pat = await _dbContext.Patients.AsNoTracking()
                .FirstOrDefaultAsync(p => p.UserId == currentUserId.Value, cancellationToken);
            if (pat != null)
            {
                var apt = await _dbContext.Appointments.AsNoTracking()
                    .Where(a => a.PatientId == pat.Id && ClinicManagement.Domain.Enums.AppointmentStatusExtensions.HoldingSlotStatuses.Contains(a.Status))
                    .OrderByDescending(a => a.AppointmentDate)
                    .ThenByDescending(a => a.StartTime)
                    .FirstOrDefaultAsync(cancellationToken);
                if (apt != null)
                {
                    verifiedAppointmentId = apt.Id;
                    verifiedAppointmentCode = apt.AppointmentCode;
                }
            }
        }

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
                Payload = new AiActionPayloadDto { TargetUrl = SafeRoutes.Appointments }
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
                Payload = new AiActionPayloadDto { TargetUrl = SafeRoutes.DiagnosticResults }
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
                Payload = new AiActionPayloadDto { TargetUrl = SafeRoutes.Prescriptions }
            });
        }

        // 4. Bills (Canonically routed to /patient/invoices)
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
                Payload = new AiActionPayloadDto { TargetUrl = SafeRoutes.Invoices }
            });
        }

        // 5. Contact Reception (No fake /contact route; strictly honest contact info)
        if (string.Equals(requestedActionType, AiActionTypes.ContactReception, StringComparison.OrdinalIgnoreCase)
            || lowerMsg.Contains("lễ tân") || lowerMsg.Contains("tiếp đón") || lowerMsg.Contains("liên hệ phòng khám"))
        {
            responseDto.Actions.Add(new AiActionDto
            {
                Id = "act-contact-reception",
                Type = AiActionTypes.ContactReception,
                Label = "Liên hệ bàn tiếp đón lễ tân",
                Description = "Thông tin liên hệ quầy tiếp đón của phòng khám",
                Style = "secondary",
                RequiresAuthentication = false,
                RequiresConfirmation = false,
                Payload = new AiActionPayloadDto
                {
                    Reason = "Thông tin liên hệ lễ tân chưa được cấu hình trong hệ thống."
                }
            });
        }

        // 6. View Doctors
        if (string.Equals(requestedActionType, AiActionTypes.ViewDoctors, StringComparison.OrdinalIgnoreCase)
            || lowerMsg.Contains("danh sách bác sĩ") || lowerMsg.Contains("đội ngũ bác sĩ"))
        {
            responseDto.Actions.Add(new AiActionDto
            {
                Id = "act-nav-doctors",
                Type = AiActionTypes.ViewDoctors,
                Label = "Xem danh sách bác sĩ",
                Description = "Tra cứu thông tin đội ngũ bác sĩ chuyên khoa",
                Style = "secondary",
                RequiresAuthentication = false,
                RequiresConfirmation = false,
                Payload = new AiActionPayloadDto { TargetUrl = SafeRoutes.Doctors }
            });
        }

        // 7. Start Booking
        if (string.Equals(requestedActionType, AiActionTypes.StartBooking, StringComparison.OrdinalIgnoreCase))
        {
            responseDto.Actions.Add(new AiActionDto
            {
                Id = "act-nav-start-booking",
                Type = AiActionTypes.StartBooking,
                Label = "Mở trang Đặt lịch khám",
                Style = "primary",
                RequiresAuthentication = false,
                RequiresConfirmation = false,
                Payload = new AiActionPayloadDto { TargetUrl = SafeRoutes.BookAppointment }
            });
        }

        // 8. Open Appointment Detail (highlights or opens modal for verified appointment, or opens list)
        if (string.Equals(requestedActionType, AiActionTypes.OpenAppointmentDetail, StringComparison.OrdinalIgnoreCase))
        {
            var target = verifiedAppointmentId.HasValue
                ? $"{SafeRoutes.Appointments}?appointmentId={verifiedAppointmentId.Value}"
                : SafeRoutes.Appointments;

            responseDto.Actions.Add(new AiActionDto
            {
                Id = "act-nav-appointment-detail",
                Type = AiActionTypes.OpenAppointmentDetail,
                Label = "Xem chi tiết lịch hẹn",
                Description = "Mở danh sách lịch hẹn và xem chi tiết cuộc hẹn",
                Style = "secondary",
                RequiresAuthentication = true,
                RequiresConfirmation = false,
                Payload = new AiActionPayloadDto
                {
                    AppointmentId = verifiedAppointmentId,
                    AppointmentCode = verifiedAppointmentCode,
                    TargetUrl = target
                }
            });
        }

        // 9. Request Reschedule
        if (string.Equals(requestedActionType, AiActionTypes.RequestReschedule, StringComparison.OrdinalIgnoreCase)
            || lowerMsg.Contains("đổi lịch") || lowerMsg.Contains("dời lịch"))
        {
            var target = verifiedAppointmentId.HasValue
                ? $"{SafeRoutes.Appointments}?appointmentId={verifiedAppointmentId.Value}&action=reschedule"
                : SafeRoutes.Appointments;

            responseDto.Actions.Add(new AiActionDto
            {
                Id = "act-nav-reschedule",
                Type = AiActionTypes.RequestReschedule,
                Label = "Yêu cầu đổi lịch khám",
                Description = "Mở trang lịch hẹn để chọn đổi ngày/giờ khám",
                Style = "secondary",
                RequiresAuthentication = true,
                RequiresConfirmation = false,
                Payload = new AiActionPayloadDto
                {
                    AppointmentId = verifiedAppointmentId,
                    AppointmentCode = verifiedAppointmentCode,
                    TargetUrl = target
                }
            });
        }

        // 10. Request Cancellation
        if (string.Equals(requestedActionType, AiActionTypes.RequestCancellation, StringComparison.OrdinalIgnoreCase)
            || lowerMsg.Contains("hủy lịch"))
        {
            var target = verifiedAppointmentId.HasValue
                ? $"{SafeRoutes.Appointments}?appointmentId={verifiedAppointmentId.Value}&action=cancel"
                : SafeRoutes.Appointments;

            responseDto.Actions.Add(new AiActionDto
            {
                Id = "act-nav-cancel-apt",
                Type = AiActionTypes.RequestCancellation,
                Label = "Yêu cầu hủy lịch khám",
                Description = "Mở trang lịch hẹn để gửi yêu cầu hủy lịch",
                Style = "danger",
                RequiresAuthentication = true,
                RequiresConfirmation = false,
                Payload = new AiActionPayloadDto
                {
                    AppointmentId = verifiedAppointmentId,
                    AppointmentCode = verifiedAppointmentCode,
                    TargetUrl = target
                }
            });
        }

        // 11. View Available Slots
        if (string.Equals(requestedActionType, AiActionTypes.ViewAvailableSlots, StringComparison.OrdinalIgnoreCase)
            || lowerMsg.Contains("lịch trống") || lowerMsg.Contains("khung giờ trống") || lowerMsg.Contains("xem slot"))
        {
            if (!responseDto.Actions.Any(a => a.Type == AiActionTypes.ViewAvailableSlots))
            {
                responseDto.Actions.Add(new AiActionDto
                {
                    Id = "act-view-available-slots",
                    Type = AiActionTypes.ViewAvailableSlots,
                    Label = "Xem lịch trống khả dụng",
                    Description = "Tra cứu các khung giờ khám khả dụng",
                    Style = "secondary",
                    RequiresAuthentication = false,
                    RequiresConfirmation = false,
                    Payload = new AiActionPayloadDto
                    {
                        Reason = "Tra cứu khung giờ khám khả dụng."
                    }
                });
            }
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

    private static bool ContainsActiveEmergency(string lowerMsg)
    {
        var emergencyKeywords = new[]
        {
            "đau ngực dữ dội", "khó thở nặng", "khó thở", "ngất", "lú lẫn",
            "đột quỵ", "co giật", "chảy máu", "nôn ra máu", "dị ứng nặng",
            "tự tử", "làm hại", "bất tỉnh", "hôn mê", "ngừng tim", "sốc phản vệ"
        };

        var negationPrefixes = new[]
        {
            "không ", "chẳng ", "hết ", "không còn ", "đỡ ", "không bị ", "chưa từng ", "hết hẳn "
        };

        foreach (var kw in emergencyKeywords)
        {
            int index = 0;
            while ((index = lowerMsg.IndexOf(kw, index, StringComparison.Ordinal)) >= 0)
            {
                int prefixStart = Math.Max(0, index - 20);
                string preceding = lowerMsg[prefixStart..index];
                bool isNegated = negationPrefixes.Any(neg => preceding.EndsWith(neg, StringComparison.Ordinal));
                if (!isNegated)
                {
                    return true;
                }
                index += kw.Length;
            }
        }
        return false;
    }

    private static string? RecoverInitialReason(string cleanMessage, List<ChatMessageDto>? context, string? extractedReason)
    {
        var isActionPhrase = IsBookingActionPhrase(cleanMessage);

        if (isActionPhrase && context != null && context.Any())
        {
            var initialUserMsg = context
                .Where(c => c.Role == "user" &&
                            !IsBookingActionPhrase(c.Content))
                .Select(c => c.Content.Trim())
                .LastOrDefault();

            if (!string.IsNullOrWhiteSpace(initialUserMsg))
            {
                return initialUserMsg;
            }
        }

        if (isActionPhrase)
        {
            return !string.IsNullOrWhiteSpace(extractedReason) &&
                   !string.Equals(extractedReason.Trim(), cleanMessage, StringComparison.OrdinalIgnoreCase)
                ? extractedReason.Trim()
                : null;
        }

        return !string.IsNullOrWhiteSpace(extractedReason) ? extractedReason.Trim() : cleanMessage;
    }

    private static bool IsBookingActionPhrase(string message)
    {
        return message.StartsWith("Tôi chọn", StringComparison.OrdinalIgnoreCase) ||
               message.StartsWith("Chọn ", StringComparison.OrdinalIgnoreCase) ||
               message.StartsWith("Xem các lịch", StringComparison.OrdinalIgnoreCase) ||
               message.StartsWith("Tôi muốn đặt khám với", StringComparison.OrdinalIgnoreCase) ||
               message.StartsWith("Tôi muốn xem lịch khám vào ngày", StringComparison.OrdinalIgnoreCase) ||
               message.StartsWith("Tìm lịch khám sớm nhất", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<string> ComposeGroundedReplyAsync(
        string rawReply,
        List<WhitelistItemDto> activeSpecialties,
        CancellationToken cancellationToken)
    {
        const string neutralFallback = "Thông tin tư vấn định hướng đã được đối soát an toàn với hệ thống phòng khám. Vui lòng tham khảo các gợi ý chuyên khoa và thao tác hỗ trợ bên dưới.";

        if (string.IsNullOrWhiteSpace(rawReply))
        {
            return neutralFallback;
        }

        var trimmed = rawReply.Trim();

        // 1. Check for unverified URLs or markdown links
        if (Regex.IsMatch(trimmed, @"https?://", RegexOptions.IgnoreCase) ||
            Regex.IsMatch(trimmed, @"\[[^\]]+\]\([^)]+\)") ||
            Regex.IsMatch(trimmed, @"\bwww\.[a-z0-9]", RegexOptions.IgnoreCase))
        {
            return neutralFallback;
        }

        // 2. Check for unverified phone numbers (allow only 115 for emergency)
        var phoneMatches = Regex.Matches(trimmed, @"(?:\+84|0|\b1\d{3})[\d\s\.\-]{6,15}\b");
        foreach (Match match in phoneMatches)
        {
            var digitsOnly = Regex.Replace(match.Value, @"\D", "");
            if (digitsOnly != "115")
            {
                return neutralFallback;
            }
        }

        // 3. Check for unverified prices / monetary amounts
        if (Regex.IsMatch(trimmed, @"\b\d+[\.,]?\d*\s*(vnđ|vnd|đ|đồng|k\b|nghìn|triệu|usd|\$)\b|\b(giá|chi phí|học phí|tiền khám)\s*[:=]?\s*\d+", RegexOptions.IgnoreCase))
        {
            return neutralFallback;
        }

        // 4. Check for unverified addresses, clinic locations, floors, rooms, or hours
        if (Regex.IsMatch(trimmed, @"\b(tầng\s+\d+|phòng\s+\d+|quầy\s+\d+|sảnh\s+[\p{L}\d]+|đường\s+[\p{L}\d]+|quận\s+\d+|hàng ngày|\d{1,2}[:h]\d{2}\s*[-–]\s*\d{1,2}[:h]\d{2})\b", RegexOptions.IgnoreCase))
        {
            return neutralFallback;
        }

        // 5. Check for doctor mentions: must match active doctors in DB
        var docMatches = Regex.Matches(trimmed, @"\b(?:bác sĩ|bs\.|dr\.)\s+([A-ZÀ-Ỹ\p{Lu}][a-zà-ỹ\p{Ll}]+(?:\s+[A-ZÀ-Ỹ\p{Lu}][a-zà-ỹ\p{Ll}]+)+)", RegexOptions.IgnoreCase);
        if (docMatches.Count > 0)
        {
            var activeDocNames = await _dbContext.Doctors
                .Join(_dbContext.Users, d => d.UserId, u => u.Id, (d, u) => new { d, u })
                .Where(x => x.d.IsActive && x.u.IsActive)
                .Select(x => x.u.FullName)
                .ToListAsync(cancellationToken);

            foreach (Match match in docMatches)
            {
                var docName = match.Groups[1].Value.Trim();
                bool matchesActive = activeDocNames.Any(realName =>
                    realName.Contains(docName, StringComparison.OrdinalIgnoreCase) ||
                    docName.Contains(realName, StringComparison.OrdinalIgnoreCase));

                if (!matchesActive)
                {
                    return neutralFallback;
                }
            }
        }

        // 6. Check for specialty mentions: must match active specialties in DB
        var specMatches = Regex.Matches(trimmed, @"\b(?:chuyên khoa|khoa)\s+([A-ZÀ-Ỹ\p{Lu}][a-zà-ỹ\p{Ll}]+(?:\s+[A-ZÀ-Ỹ\p{Lu}][a-zà-ỹ\p{Ll}]+)*)", RegexOptions.IgnoreCase);
        if (specMatches.Count > 0)
        {
            foreach (Match match in specMatches)
            {
                var specName = match.Groups[1].Value.Trim();
                bool matchesActive = activeSpecialties.Any(s =>
                    s.Name.Contains(specName, StringComparison.OrdinalIgnoreCase) ||
                    specName.Contains(s.Name, StringComparison.OrdinalIgnoreCase));

                if (!matchesActive)
                {
                    return neutralFallback;
                }
            }
        }

        return trimmed.Length > 1000 ? trimmed[..1000] : trimmed;
    }
}
