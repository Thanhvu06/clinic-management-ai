using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
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
    private readonly IAiSessionSnapshotStore _snapshotStore;
    private readonly IAiAuditService _auditService;
    private readonly IAiBookingConfirmationStore _confirmationStore;
    private readonly IAiSpecialtyClassifier? _classifier;
    private readonly IVietnameseIntentClassifier _intentClassifier;

    public AiSpecialtyService(
        AppDbContext dbContext,
        IAiSpecialtySuggestionProvider aiProvider,
        IClinicAiContextService clinicAiContextService,
        IDateTimeProvider dateTimeProvider,
        ILogger<AiSpecialtyService> logger,
        ClinicManagement.Application.Appointments.Interfaces.IAppointmentAvailabilityPolicy availabilityPolicy,
        ClinicManagement.Application.Authentication.Interfaces.ICurrentUserService currentUserService,
        IAiSessionSnapshotStore? snapshotStore = null,
        IAiAuditService? auditService = null,
        IAiBookingConfirmationStore? confirmationStore = null,
        IAiSpecialtyClassifier? classifier = null,
        IVietnameseIntentClassifier? intentClassifier = null)
    {
        _dbContext = dbContext;
        _aiProvider = aiProvider;
        _clinicAiContextService = clinicAiContextService;
        _dateTimeProvider = dateTimeProvider;
        _logger = logger;
        _availabilityPolicy = availabilityPolicy;
        _currentUserService = currentUserService;
        _snapshotStore = snapshotStore ?? new Persistence.EfAiSessionSnapshotStore(dbContext, dateTimeProvider, LoggerFactory.Create(_ => { }).CreateLogger<Persistence.EfAiSessionSnapshotStore>());
        _auditService = auditService ?? new Persistence.EfAiAuditService(dbContext, dateTimeProvider, LoggerFactory.Create(_ => { }).CreateLogger<Persistence.EfAiAuditService>());
        _confirmationStore = confirmationStore ?? new Persistence.EfAiBookingConfirmationStore(dbContext, dateTimeProvider, LoggerFactory.Create(_ => { }).CreateLogger<Persistence.EfAiBookingConfirmationStore>());
        _classifier = classifier;
        _intentClassifier = intentClassifier ?? new VietnameseIntentClassifier();
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

        // A client may start a new tab with no session ID, but an existing session
        // can never be silently rebound to another account or revived after expiry.
        // Stateless cancellation intentionally remains stateless until its scope is
        // proven, so an ambiguous cancel cannot create a new session as a side effect.
        var likelyCancel = string.Equals(request.Intent, AiChatIntentTypes.CancelDraft, StringComparison.OrdinalIgnoreCase) ||
                           lowerMsg.Contains("hủy", StringComparison.OrdinalIgnoreCase) ||
                           lowerMsg.Contains("huy", StringComparison.OrdinalIgnoreCase);
        // If a caller is continuing from a server-bound snapshot, leave the
        // session unset until snapshot validation. This preserves support for
        // legacy user-bound snapshots while still rejecting bound snapshots
        // presented without their session identifier.
        if (string.IsNullOrWhiteSpace(request.SessionId) && !likelyCancel && string.IsNullOrWhiteSpace(request.ContextSnapshotId))
        {
            request.SessionId = $"sess_{Guid.NewGuid():N}";
        }

        if (!string.IsNullOrWhiteSpace(request.SessionId))
        {
            var sessionTouch = await _snapshotStore.TouchSessionAsync(
                request.SessionId,
                _currentUserService.UserId,
                draftId: null,
                draftVersion: request.DraftVersion,
                facilityId: null,
                cancellationToken: cancellationToken);
            if (!sessionTouch.IsAccepted)
            {
                return BuildSessionFailureResponse(request, sessionTouch);
            }
        }

        // 1. EMERGENCY RULES - Executed FIRST before any AI provider or database search
        if (ContainsActiveEmergency(lowerMsg))
        {
            await _auditService.LogActionAsync(new AiAuditLogEntry
            {
                UserId = _currentUserService.UserId,
                SessionId = request.SessionId,
                ActionType = "EmergencyAlert",
                Outcome = "EMERGENCY"
            }, cancellationToken);

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

        // 3. Dialogue Manager: Intent Understanding & Contextual Handling
        var intentContext = new IntentClassificationContext
        {
            HasActiveDraft = request.PendingSpecialtyId.HasValue,
            HasDoctor = request.PendingDoctorId.HasValue,
            HasSlot = request.PendingSlotId.HasValue,
            HasReason = !string.IsNullOrWhiteSpace(request.Reason),
            LastModelQuestion = request.Context?.LastOrDefault(c => c.Role == "model")?.Content,
            DisplayedDoctorIds = request.DisplayedDoctorIds ?? new List<long>(),
            DisplayedSlotIds = request.DisplayedSlotIds ?? new List<long>(),
            ContextSnapshotId = request.ContextSnapshotId
        };

        var localClassification = _intentClassifier.Classify(cleanMessage, intentContext);
        var resolvedIntent = !string.IsNullOrWhiteSpace(request.Intent) && AiChatIntentTypes.IsAllowed(request.Intent)
            ? request.Intent
            : localClassification.Intent;

        var passiveDraftVersion = ResolveDraftVersion(request.DraftVersion, hasSubstantiveChanges: false);

        // 3.1 Unclear or Out-of-Scope: Politeness Guardian (Prevents dropping into Degraded or polluting Reason)
        if (!localClassification.IsClear || resolvedIntent == AiChatIntentTypes.UnclearOrOutOfScope)
        {
            var unclearResponse = new AiChatResponseDto
            {
                Message = localClassification.ClarificationPrompt ?? "ClinicCare chưa hiểu rõ yêu cầu của bạn. Bạn có thể mô tả cụ thể hơn về triệu chứng sức khỏe, nhu cầu đặt lịch hoặc thông tin phòng khám cần tìm hiểu không ạ?",
                ClarificationPrompt = localClassification.ClarificationPrompt,
                DialogueOutcome = "UnclearInput",
                PrimaryIntent = AiChatIntentTypes.UnclearOrOutOfScope,
                AssistantStatus = "Online",
                ProviderStatus = "NotCalled",
                PromptVersion = GeminiAiProvider.CurrentPromptVersion
            };

            await PreserveExistingDraftAsync(request, unclearResponse, cancellationToken);
            return WithDraftVersionSync(unclearResponse, passiveDraftVersion);
        }

        // 3.2 Cancel Draft: Clears active draft in session without touching database appointments
        if (resolvedIntent == AiChatIntentTypes.CancelDraft)
        {
            var currentUserId = _currentUserService.UserId;
            var nowUtc = _dateTimeProvider.UtcNow;
            var scopeResult = await _snapshotStore.TryResolveCancelScopeFromSnapshotAsync(
                request.ContextSnapshotId,
                currentUserId,
                request.SessionId,
                request.DraftId,
                nowUtc,
                cancellationToken);

            var hasResolvedScope = scopeResult.HasResolved;
            var cancelTargetSessionId = scopeResult.SessionId;
            var cancelTargetDraftId = scopeResult.DraftId;

            if (hasResolvedScope && cancelTargetSessionId != null && cancelTargetDraftId != null)
            {
                await _snapshotStore.InvalidateDraftSnapshotsForCancelAsync(
                    cancelTargetDraftId,
                    cancelTargetSessionId,
                    currentUserId,
                    facilityId: null,
                    nowUtc: nowUtc,
                    cancellationToken: cancellationToken);

                if (currentUserId.HasValue)
                {
                    await _confirmationStore.RevokeForDraftAsync(
                        currentUserId.Value,
                        cancelTargetSessionId,
                        cancelTargetDraftId,
                        cancellationToken);
                }

                await _auditService.LogActionAsync(new AiAuditLogEntry
                {
                    UserId = currentUserId,
                    SessionId = cancelTargetSessionId,
                    DraftId = cancelTargetDraftId,
                    DraftVersion = passiveDraftVersion,
                    ActionType = "CancelDraft",
                    Outcome = "DraftCancelled"
                }, cancellationToken);

                var cancelResponse = new AiChatResponseDto
                {
                    Message = "Đã hủy bản nháp đặt lịch hiện tại. Bạn có cần hỗ trợ gì khác không?",
                    DialogueOutcome = "DraftCancelled",
                    PrimaryIntent = AiChatIntentTypes.CancelDraft,
                    AssistantStatus = "Online",
                    ProviderStatus = "NotCalled",
                    PromptVersion = GeminiAiProvider.CurrentPromptVersion,
                    BookingDraft = null,
                    SessionId = cancelTargetSessionId,
                    DraftId = null
                };
                return WithDraftVersionSync(cancelResponse, passiveDraftVersion);
            }

            if (!string.IsNullOrWhiteSpace(request.DraftId) ||
                !string.IsNullOrWhiteSpace(request.SessionId) ||
                !string.IsNullOrWhiteSpace(request.ContextSnapshotId) ||
                request.DraftVersion.HasValue ||
                string.Equals(request.Intent, AiChatIntentTypes.CancelDraft, StringComparison.Ordinal))
            {
                await _auditService.LogActionAsync(new AiAuditLogEntry
                {
                    UserId = currentUserId,
                    SessionId = request.SessionId,
                    DraftId = request.DraftId,
                    DraftVersion = passiveDraftVersion,
                    ActionType = "CancelDraft",
                    Outcome = "ClarificationRequired",
                    ErrorCode = "AMBIGUOUS_SCOPE"
                }, cancellationToken);

                // Fail closed: do NOT revoke snapshots across sessions/drafts when target scope cannot be uniquely determined
                var failClosedCancelResponse = new AiChatResponseDto
                {
                    Message = "Không thể xác định chính xác phiên làm việc hoặc bản nháp cần hủy (thiếu SessionId hoặc DraftId). Vui lòng gửi lại yêu cầu kèm đầy đủ SessionId và DraftId của phiên hiện tại.",
                    ClarificationPrompt = "Vui lòng gửi đầy đủ SessionId và DraftId của phiên cần hủy.",
                    DialogueOutcome = "ClarificationRequired",
                    PrimaryIntent = AiChatIntentTypes.CancelDraft,
                    AssistantStatus = "Online",
                    ProviderStatus = "NotCalled",
                    PromptVersion = GeminiAiProvider.CurrentPromptVersion,
                    BookingDraft = null,
                    SessionId = !string.IsNullOrWhiteSpace(request.SessionId) ? request.SessionId.Trim() : null,
                    DraftId = null
                };
                return WithDraftVersionSync(failClosedCancelResponse, passiveDraftVersion);
            }

            var statelessCancelResponse = new AiChatResponseDto
            {
                Message = "Đã hủy bản nháp đặt lịch hiện tại. Bạn có cần hỗ trợ gì khác không?",
                DialogueOutcome = "DraftCancelled",
                PrimaryIntent = AiChatIntentTypes.CancelDraft,
                AssistantStatus = "Online",
                ProviderStatus = "NotCalled",
                PromptVersion = GeminiAiProvider.CurrentPromptVersion,
                BookingDraft = null,
                SessionId = $"sess_{Guid.NewGuid():N}",
                DraftId = null
            };
            return WithDraftVersionSync(statelessCancelResponse, passiveDraftVersion);
        }

        // 3.3 Pricing Inquiry: Honest Consultation Fees from DB
        if (resolvedIntent == AiChatIntentTypes.PricingInquiry)
        {
            return await HandlePricingInquiryAsync(request, cleanMessage, passiveDraftVersion, cancellationToken);
        }

        // 3.4 Facility Inquiry: Address, phone, operating hours from DB
        if (resolvedIntent == AiChatIntentTypes.FacilityInquiry)
        {
            return await HandleFacilityInquiryAsync(request, cleanMessage, passiveDraftVersion, cancellationToken);
        }

        // 3.5 View Appointments: Authenticated navigation to user's appointments
        if (resolvedIntent == AiChatIntentTypes.ViewAppointments)
        {
            return await HandleViewAppointmentsAsync(request, passiveDraftVersion, cancellationToken);
        }

        // 3.6 Confirm Booking: Validates prerequisites and permissions, never accepts 'chốt' as reason
        if (resolvedIntent == AiChatIntentTypes.ConfirmBooking)
        {
            return await HandleConfirmBookingIntentAsync(request, passiveDraftVersion, cancellationToken);
        }

        // 3.7 Review Draft
        if (resolvedIntent == AiChatIntentTypes.ReviewDraft)
        {
            return await HandleReviewDraftIntentAsync(request, passiveDraftVersion, cancellationToken);
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
        AiChatProviderResult? aiResult = null;
        bool providerActuallyFailed = false;
        try
        {
            aiResult = await _aiProvider.ChatWithAiAsync(cleanMessage, cleanContext, whitelistData, clinicContextJson, cancellationToken);
        }
        catch (Exception ex)
        {
            providerActuallyFailed = true;
            _logger.LogError(ex, "AI Provider chat failed.");
            if (!localClassification.ExtractedRelativeDoctorIndex.HasValue && !localClassification.ExtractedRelativeSlotIndex.HasValue)
            {
                return await BuildDegradedResponseAsync(request, cleanMessage, "NetworkError", cancellationToken);
            }
        }

        if (aiResult == null || !aiResult.IsSuccess || string.IsNullOrWhiteSpace(aiResult.Reply))
        {
            providerActuallyFailed = true;
            if (localClassification.ExtractedRelativeDoctorIndex.HasValue || localClassification.ExtractedRelativeSlotIndex.HasValue)
            {
                aiResult = new AiChatProviderResult
                {
                    IsSuccess = true,
                    Status = "Success",
                    Reply = "Đã ghi nhận lựa chọn từ danh sách hiển thị.",
                    PrimaryIntent = resolvedIntent,
                    Urgency = "ROUTINE"
                };
            }
            else
            {
                var providerStatus = !string.IsNullOrWhiteSpace(aiResult?.Status) ? aiResult.Status : "NetworkError";
                return await BuildDegradedResponseAsync(request, cleanMessage, providerStatus, cancellationToken);
            }
        }

        // Merge local entity extractions & corrections if model missed them
        if (localClassification.IsCorrection && !aiResult.IsCorrection)
        {
            aiResult.IsCorrection = true;
            aiResult.NegatedDoctorName ??= localClassification.NegatedDoctorName;
            aiResult.CorrectionTarget ??= localClassification.CorrectionTarget;
            aiResult.ExtractedDoctorName ??= localClassification.ExtractedDoctorName;
            aiResult.ExtractedDate ??= localClassification.ExtractedDate;
        }

        if (!string.IsNullOrWhiteSpace(localClassification.ExtractedDoctorName) && string.IsNullOrWhiteSpace(aiResult.ExtractedDoctorName))
        {
            aiResult.ExtractedDoctorName = localClassification.ExtractedDoctorName;
        }

        var effectivePrimaryIntent = !string.IsNullOrWhiteSpace(aiResult.PrimaryIntent) && AiChatIntentTypes.IsAllowed(aiResult.PrimaryIntent)
            ? aiResult.PrimaryIntent
            : resolvedIntent;

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
            PromptVersion = GeminiAiProvider.CurrentPromptVersion,
            PrimaryIntent = effectivePrimaryIntent,
            AssistantStatus = "Online",
            ProviderStatus = "Healthy"
        };
        if (providerActuallyFailed)
        {
            responseDto.ProviderStatus = "Degraded";
        }

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

        // Special handling for Greeting: preserve draft without running slot override (Issue A Fix)
        if (string.Equals(effectivePrimaryIntent, AiChatIntentTypes.Greeting, StringComparison.OrdinalIgnoreCase))
        {
            responseDto.DialogueOutcome = "Greeting";
            await PreserveExistingDraftAsync(request, responseDto, cancellationToken);
            return WithDraftVersionSync(responseDto, passiveDraftVersion);
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
        await GroundBookingFlowAsync(request, cleanMessage, lowerMsg, aiResult, localClassification, whitelistData, responseDto, cancellationToken);

        var clinicalReason = RecoverInitialReason(cleanMessage, request.Context, request.Reason, aiResult.ExtractedReason);
        if (responseDto.BookingDraft == null)
        {
            if (!string.IsNullOrWhiteSpace(clinicalReason))
            {
                responseDto.BookingDraft = new AiBookingDraftDto
                {
                    Reason = clinicalReason,
                    IsComplete = false,
                    Version = ResolveDraftVersion(request.DraftVersion, hasSubstantiveChanges: true)
                };
            }
        }
        else if (!string.IsNullOrWhiteSpace(clinicalReason))
        {
            responseDto.BookingDraft.Reason = clinicalReason;
        }

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

        var activeDraftVersion = responseDto.BookingDraft?.Version is int computedVersion && computedVersion >= 1
            ? computedVersion
            : ResolveDraftVersion(request.DraftVersion, hasSubstantiveChanges: false);
        WithDraftVersionSync(responseDto, activeDraftVersion);

        var activeSessionId = !string.IsNullOrWhiteSpace(request.SessionId)
            ? request.SessionId.Trim()
            : null;
        var activeDraftId = !string.IsNullOrWhiteSpace(request.DraftId) && !await _snapshotStore.IsDraftCancelledAsync(request.DraftId, _currentUserService.UserId, activeSessionId, _dateTimeProvider.UtcNow, cancellationToken)
            ? request.DraftId.Trim()
            : null;

        var resolvedSessionId = activeSessionId ?? $"sess_{Guid.NewGuid():N}";
        responseDto.SessionId = resolvedSessionId;
        if (responseDto.BookingDraft != null)
        {
            var resolvedDraftId = activeDraftId ?? $"draft_{Guid.NewGuid():N}";
            responseDto.BookingDraft.DraftId = resolvedDraftId;
            responseDto.DraftId = resolvedDraftId;
        }
        else
        {
            responseDto.DraftId = activeDraftId;
        }

        // Ensure all returned actions conform to security allowlist and safety rules, capped at max 6 actions
        responseDto.Actions = responseDto.Actions
            .Where(a => AiActionValidator.Validate(a, out _))
            .Take(6)
            .ToList();

        if (string.Equals(responseDto.DialogueOutcome, "DraftModified", StringComparison.OrdinalIgnoreCase) &&
            _currentUserService.UserId.HasValue &&
            !string.IsNullOrWhiteSpace(request.SessionId) &&
            !string.IsNullOrWhiteSpace(request.DraftId))
        {
            await _confirmationStore.RevokeForDraftAsync(
                _currentUserService.UserId.Value,
                request.SessionId.Trim(),
                request.DraftId.Trim(),
                cancellationToken);
        }

        var finalSessionTouch = await _snapshotStore.TouchSessionAsync(
            resolvedSessionId,
            _currentUserService.UserId,
            responseDto.DraftId,
            activeDraftVersion,
            facilityId: null,
            cancellationToken: cancellationToken);
        if (!finalSessionTouch.IsAccepted)
        {
            return BuildSessionFailureResponse(request, finalSessionTouch);
        }

        var docIdsInActions = responseDto.Actions
            .Where(a => a.Type == AiActionTypes.SelectDoctor && a.Payload?.DoctorId != null)
            .Select(a => a.Payload!.DoctorId!.Value)
            .Distinct()
            .ToList();
        var slotIdsInActions = responseDto.Actions
            .Where(a => a.Type == AiActionTypes.SelectSlot && a.Payload?.SlotId != null)
            .Select(a => a.Payload!.SlotId!.Value)
            .Distinct()
            .ToList();
        if (docIdsInActions.Count > 0 || slotIdsInActions.Count > 0)
        {
            var snapshot = await _snapshotStore.CreateSnapshotAsync(new CreateSnapshotRequest
            {
                UserId = _currentUserService.UserId,
                SessionId = resolvedSessionId,
                DraftId = responseDto.DraftId,
                DraftVersion = activeDraftVersion,
                FacilityId = null, // Patient actor - facility resolved at booking time
                SpecialtyId = responseDto.BookingDraft?.SpecialtyId ?? request.PendingSpecialtyId,
                DoctorId = responseDto.BookingDraft?.DoctorId ?? request.PendingDoctorId,
                SlotDate = responseDto.BookingDraft?.SlotDate ?? request.PendingSlotDate,
                DoctorIds = docIdsInActions,
                SlotIds = slotIdsInActions
            }, cancellationToken);
            responseDto.ContextSnapshotId = snapshot.SnapshotId;
        }

        responseDto.SpecialtySuggestions = responseDto.SpecialtySuggestions
            .Take(3)
            .ToList();

        return responseDto;
    }

    private async Task<AiChatResponseDto> FindEarliestAvailableSlotsAsync(
        AiChatRequestDto request,
        CancellationToken cancellationToken)
    {
        var nextDraftVersion = ResolveDraftVersion(request.DraftVersion);
        var response = new AiChatResponseDto
        {
            Urgency = "ROUTINE",
            PromptVersion = GeminiAiProvider.CurrentPromptVersion,
            AssistantStatus = "Online",
            ProviderStatus = "NotCalled"
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
            return WithDraftVersionSync(response, nextDraftVersion);
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
            return WithDraftVersionSync(response, nextDraftVersion);
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
                IsComplete = false,
                Version = nextDraftVersion
            };
            response.MissingFields = new List<string> { "Doctor", "TimeSlot" };
            return WithDraftVersionSync(response, nextDraftVersion);
        }

        var vnToday = _dateTimeProvider.VietnamToday;
        var newlyExtractedDate = !string.IsNullOrWhiteSpace(request.Message)
            ? ResolveRelativeDate(request.Message, vnToday)
            : null;
        if (newlyExtractedDate.HasValue)
        {
            request.PendingSlotDate = newlyExtractedDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            request.PendingSlotId = null;
        }

        var searchFrom = vnToday;
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
                response.BookingDraft = BuildSearchDraft(specialty.Id, specialty.Name, request.PendingDoctorId, null, null, reason, nextDraftVersion);
                response.MissingFields = new List<string> { "DesiredDate", "TimeSlot" };
                return WithDraftVersionSync(response, nextDraftVersion);
            }
        }

        if (searchFrom < _dateTimeProvider.VietnamToday)
        {
            response.Message = "Ngày bắt đầu tìm lịch đã qua. Vui lòng chọn hôm nay hoặc một ngày trong tương lai.";
            response.BookingDraft = BuildSearchDraft(specialty.Id, specialty.Name, request.PendingDoctorId, null, null, reason, nextDraftVersion);
            response.MissingFields = new List<string> { "DesiredDate", "TimeSlot" };
            return WithDraftVersionSync(response, nextDraftVersion);
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
            response.BookingDraft = BuildSearchDraft(specialty.Id, specialty.Name, null, null, searchFrom, reason, nextDraftVersion);
            response.MissingFields = new List<string> { "Doctor", "TimeSlot" };
            return WithDraftVersionSync(response, nextDraftVersion);
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
            response.BookingDraft = BuildSearchDraft(specialty.Id, specialty.Name, null, null, searchFrom, reason, nextDraftVersion);
            response.MissingFields = new List<string> { "Doctor", "TimeSlot" };
            return WithDraftVersionSync(response, nextDraftVersion);
        }

        if (!_currentUserService.UserId.HasValue)
        {
            response.Message = "Không thể xác định tài khoản bệnh nhân để kiểm tra lịch trùng. Vui lòng đăng nhập lại.";
            response.BookingDraft = BuildSearchDraft(specialty.Id, specialty.Name, selectedDoctor?.Id, FormatDoctorName(selectedDoctor?.AcademicTitle, selectedDoctor?.FullName), searchFrom, reason, nextDraftVersion);
            response.MissingFields = new List<string> { "TimeSlot" };
            return WithDraftVersionSync(response, nextDraftVersion);
        }

        var patientId = await _dbContext.Patients
            .AsNoTracking()
            .Where(p => p.UserId == _currentUserService.UserId.Value)
            .Select(p => (long?)p.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (!patientId.HasValue)
        {
            response.Message = "Không thể xác định hồ sơ bệnh nhân để kiểm tra lịch trùng. Vui lòng liên hệ quản trị hệ thống.";
            response.BookingDraft = BuildSearchDraft(specialty.Id, specialty.Name, selectedDoctor?.Id, FormatDoctorName(selectedDoctor?.AcademicTitle, selectedDoctor?.FullName), searchFrom, reason, nextDraftVersion);
            response.MissingFields = new List<string> { "TimeSlot" };
            return WithDraftVersionSync(response, nextDraftVersion);
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
            reason,
            nextDraftVersion);
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
            return WithDraftVersionSync(response, nextDraftVersion);
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
                DraftVersion = nextDraftVersion,
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
                    Reason = reason,
                    DraftVersion = nextDraftVersion
                }
            });
        }

        WithDraftVersionSync(response, nextDraftVersion);

        var activeSessionId = !string.IsNullOrWhiteSpace(request.SessionId) ? request.SessionId.Trim() : null;
        var activeDraftId = !string.IsNullOrWhiteSpace(request.DraftId) && !await _snapshotStore.IsDraftCancelledAsync(request.DraftId, _currentUserService.UserId, activeSessionId, _dateTimeProvider.UtcNow, cancellationToken) ? request.DraftId.Trim() : null;
        var resolvedSessionId = activeSessionId ?? $"sess_{Guid.NewGuid():N}";
        response.SessionId = resolvedSessionId;
        if (response.BookingDraft != null)
        {
            var resolvedDraftId = activeDraftId ?? $"draft_{Guid.NewGuid():N}";
            response.BookingDraft.DraftId = resolvedDraftId;
            response.DraftId = resolvedDraftId;
        }

        var earliestSlotIds = slots.Select(s => s.SlotId).ToList();
        var earliestDocIds = slots.Select(s => s.DoctorId).Distinct().ToList();
        if (earliestSlotIds.Count > 0 || earliestDocIds.Count > 0)
        {
            var snapshot = await _snapshotStore.CreateSnapshotAsync(new CreateSnapshotRequest
            {
                UserId = _currentUserService.UserId,
                SessionId = resolvedSessionId,
                DraftId = response.DraftId,
                DraftVersion = nextDraftVersion,
                FacilityId = null, // Patient actor - facility resolved at booking time
                SpecialtyId = specialty.Id,
                DoctorId = selectedDoctor?.Id,
                SlotDate = searchFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                DoctorIds = earliestDocIds,
                SlotIds = earliestSlotIds
            }, cancellationToken);
            response.ContextSnapshotId = snapshot.SnapshotId;
        }

        response.Actions = response.Actions
            .Where(a => AiActionValidator.Validate(a, out _))
            .Take(6)
            .ToList();
        return response;
    }

    public static int ResolveDraftVersion(int? requestDraftVersion, bool hasSubstantiveChanges = false)
    {
        if (requestDraftVersion.HasValue && requestDraftVersion.Value >= 1)
        {
            return hasSubstantiveChanges ? requestDraftVersion.Value + 1 : requestDraftVersion.Value;
        }
        return 1;
    }

    private static AiChatResponseDto WithDraftVersionSync(AiChatResponseDto response, int nextVersion)
    {
        if (response.BookingDraft != null)
        {
            response.BookingDraft.Version = nextVersion;
        }

        foreach (var action in response.Actions)
        {
            if (AiActionTypes.IsBookingAction(action.Type))
            {
                action.DraftVersion = nextVersion;
                if (action.Payload != null)
                {
                    action.Payload.DraftVersion = nextVersion;
                }
            }
        }

        return response;
    }

    private static AiBookingDraftDto BuildSearchDraft(
        long specialtyId,
        string specialtyName,
        long? doctorId,
        string? doctorName,
        DateOnly? searchFrom,
        string? reason,
        int version = 1)
    {
        return new AiBookingDraftDto
        {
            SpecialtyId = specialtyId,
            SpecialtyName = specialtyName,
            DoctorId = doctorId,
            DoctorName = doctorName,
            SlotDate = searchFrom?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Reason = reason,
            IsComplete = false,
            Version = version
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
        IntentClassificationResult localClassification,
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

        // B. Resolve Preferred Date & Date Modification
        DateOnly? newlyExtractedDate = null;
        if (!string.IsNullOrWhiteSpace(localClassification.ExtractedDate))
        {
            newlyExtractedDate = ResolveRelativeDate(localClassification.ExtractedDate, vnToday);
        }
        else if (!string.IsNullOrWhiteSpace(aiResult.ExtractedDate))
        {
            newlyExtractedDate = ResolveRelativeDate(aiResult.ExtractedDate, vnToday);
        }

        if (!newlyExtractedDate.HasValue &&
            !localClassification.ExtractedRelativeDoctorIndex.HasValue &&
            !localClassification.ExtractedRelativeSlotIndex.HasValue)
        {
            newlyExtractedDate = ResolveRelativeDate(cleanMessage, vnToday);
        }

        DateOnly? parsedPendingDate = null;
        if (!string.IsNullOrWhiteSpace(request.PendingSlotDate))
        {
            if (DateOnly.TryParseExact(request.PendingSlotDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                parsedPendingDate = parsed;
            }
            else
            {
                responseDto.Message = "Ngày khám đã chọn không hợp lệ. Vui lòng chọn lại ngày theo định dạng ngày/tháng/năm.";
                responseDto.MissingFields = new List<string> { "DesiredDate", "TimeSlot" };
                return;
            }
        }

        bool isDateModified = (localClassification.IsCorrection && localClassification.CorrectionTarget == "Date") ||
                              (newlyExtractedDate.HasValue && parsedPendingDate.HasValue && newlyExtractedDate.Value != parsedPendingDate.Value);

        DateOnly? targetDate = null;
        if (newlyExtractedDate.HasValue)
        {
            targetDate = newlyExtractedDate.Value;
            if (parsedPendingDate.HasValue && newlyExtractedDate.Value != parsedPendingDate.Value)
            {
                isDateModified = true;
            }
            request.PendingSlotDate = newlyExtractedDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
        else if (parsedPendingDate.HasValue)
        {
            targetDate = parsedPendingDate.Value;
        }

        if (isDateModified)
        {
            request.PendingSlotId = null; // Evict slot on date change!
            responseDto.DialogueOutcome = "DraftModified";
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
        var originalPendingDoctorId = request.PendingDoctorId;
        var originalPendingSlotId = request.PendingSlotId;
        long? targetDoctorId = request.PendingDoctorId;
        string? targetDoctorName = null;
        string? targetDoctorAcademicTitle = null;

        var allActiveDoctors = await QueryActiveDoctorsAsync(cancellationToken);

        // Check Doctor Modification / Negation
        bool isDoctorModified = (localClassification.IsCorrection && (localClassification.CorrectionTarget == "Doctor" || !string.IsNullOrWhiteSpace(localClassification.NegatedDoctorName))) ||
                                (!string.IsNullOrWhiteSpace(localClassification.ExtractedDoctorName) && targetDoctorId.HasValue) ||
                                (localClassification.ExtractedRelativeDoctorIndex.HasValue && targetDoctorId.HasValue) ||
                                (aiResult.IsCorrection && (aiResult.CorrectionTarget == "Doctor" || !string.IsNullOrWhiteSpace(aiResult.NegatedDoctorName)));

        if (isDoctorModified)
        {
            targetDoctorId = null;
            request.PendingDoctorId = null;
            request.PendingSlotId = null; // Evict slot on doctor change!
            responseDto.DialogueOutcome = "DraftModified";
        }

        // Relative doctor selection from verified server-side snapshot ONLY
        if (localClassification.ExtractedRelativeDoctorIndex.HasValue)
        {
            var relIdx = localClassification.ExtractedRelativeDoctorIndex.Value; // 0-based
            var validationResult = await _snapshotStore.ValidateSnapshotAsync(new ValidateSnapshotRequest
            {
                SnapshotId = request.ContextSnapshotId,
                CurrentUserId = _currentUserService.UserId,
                CurrentSessionId = request.SessionId,
                CurrentDraftId = request.DraftId,
                CurrentDraftVersion = request.DraftVersion,
                CurrentSpecialtyId = targetSpecialty?.Id ?? request.PendingSpecialtyId,
                RequestedDoctorIds = request.DisplayedDoctorIds,
                RequestedSlotIds = null,
                NowUtc = _dateTimeProvider.UtcNow
            }, cancellationToken);

            if (!validationResult.IsValid)
            {
                request.PendingDoctorId = originalPendingDoctorId;
                request.PendingSlotId = originalPendingSlotId;
                await PreserveExistingDraftAsync(request, responseDto, cancellationToken);
                responseDto.Message = validationResult.ErrorMessage ?? "Danh sách lựa chọn không hợp lệ. Vui lòng chọn lại.";
                responseDto.MissingFields = new List<string> { "Doctor", "TimeSlot" };
                responseDto.DialogueOutcome = "ClarificationRequired";
                return;
            }

            var snapshot = validationResult.Snapshot!;
            var candidateDocIds = snapshot.DoctorIds;
            if (relIdx >= 0 && relIdx < candidateDocIds.Count)
            {
                var resolvedDocId = candidateDocIds[relIdx];
                var matchedDoc = allActiveDoctors.FirstOrDefault(d => d.DoctorId == resolvedDocId);
                if (matchedDoc != null)
                {
                    var expectedSpecialtyId = targetSpecialty?.Id ?? snapshot.SpecialtyId;
                    if (expectedSpecialtyId.HasValue && !matchedDoc.Specialties.Any(s => s.Id == expectedSpecialtyId.Value))
                    {
                        request.PendingDoctorId = originalPendingDoctorId;
                        request.PendingSlotId = originalPendingSlotId;
                        await PreserveExistingDraftAsync(request, responseDto, cancellationToken);
                        responseDto.Message = $"Bác sĩ {matchedDoc.DisplayName} không thuộc chuyên khoa đã chọn. Vui lòng chọn lại bác sĩ phù hợp.";
                        responseDto.MissingFields = new List<string> { "Doctor", "TimeSlot" };
                        responseDto.DialogueOutcome = "ClarificationRequired";
                        return;
                    }

                    targetDoctorId = matchedDoc.DoctorId;
                    targetDoctorName = matchedDoc.FullName;
                    targetDoctorAcademicTitle = matchedDoc.AcademicTitle;
                    if (targetSpecialty == null && matchedDoc.Specialties.Any())
                    {
                        targetSpecialty = matchedDoc.Specialties.First();
                    }
                }
                else
                {
                    request.PendingDoctorId = originalPendingDoctorId;
                    request.PendingSlotId = originalPendingSlotId;
                    await PreserveExistingDraftAsync(request, responseDto, cancellationToken);
                    responseDto.Message = "Bác sĩ được chọn theo danh sách hiển thị không còn hoạt động. Vui lòng chọn lại bác sĩ.";
                    responseDto.MissingFields = new List<string> { "Doctor", "TimeSlot" };
                    responseDto.DialogueOutcome = "ClarificationRequired";
                    return;
                }
            }
            else
            {
                request.PendingDoctorId = originalPendingDoctorId;
                request.PendingSlotId = originalPendingSlotId;
                await PreserveExistingDraftAsync(request, responseDto, cancellationToken);
                responseDto.Message = "ClinicCare chưa rõ bạn muốn chọn bác sĩ nào trong danh sách. Vui lòng chọn trực tiếp từ danh sách bác sĩ hoặc nêu rõ tên bác sĩ nhé.";
                responseDto.MissingFields = new List<string> { "Doctor", "TimeSlot" };
                responseDto.DialogueOutcome = "ClarificationRequired";
                return;
            }
        }

        // Relative slot selection from verified server-side snapshot ONLY
        if (localClassification.ExtractedRelativeSlotIndex.HasValue)
        {
            var relSlotIdx = localClassification.ExtractedRelativeSlotIndex.Value; // 0-based
            var validationResult = await _snapshotStore.ValidateSnapshotAsync(new ValidateSnapshotRequest
            {
                SnapshotId = request.ContextSnapshotId,
                CurrentUserId = _currentUserService.UserId,
                CurrentSessionId = request.SessionId,
                CurrentDraftId = request.DraftId,
                CurrentDraftVersion = request.DraftVersion,
                CurrentSpecialtyId = targetSpecialty?.Id ?? request.PendingSpecialtyId,
                RequestedDoctorIds = null,
                RequestedSlotIds = request.DisplayedSlotIds,
                NowUtc = _dateTimeProvider.UtcNow
            }, cancellationToken);

            if (!validationResult.IsValid)
            {
                await PreserveExistingDraftAsync(request, responseDto, cancellationToken);
                responseDto.Message = validationResult.ErrorMessage ?? "Danh sách lựa chọn không hợp lệ. Vui lòng chọn lại.";
                responseDto.MissingFields = new List<string> { "TimeSlot" };
                responseDto.DialogueOutcome = "ClarificationRequired";
                return;
            }

            var snapshot = validationResult.Snapshot!;
            var candidateSlotIds = snapshot.SlotIds;
            if (relSlotIdx >= 0 && relSlotIdx < candidateSlotIds.Count)
            {
                var resolvedSlotId = candidateSlotIds[relSlotIdx];
                long? verifyPatientId = null;
                if (_currentUserService.UserId.HasValue)
                {
                    var pat = await _dbContext.Patients
                        .AsNoTracking()
                        .FirstOrDefaultAsync(p => p.UserId == _currentUserService.UserId.Value, cancellationToken);
                    verifyPatientId = pat?.Id;
                }

                var slotVerify = await _availabilityPolicy.EvaluateSlotAvailabilityAsync(
                    new ClinicManagement.Application.Appointments.Interfaces.SlotAvailabilityRequest
                    {
                        SlotId = resolvedSlotId,
                        DoctorId = targetDoctorId ?? snapshot.DoctorId,
                        SpecialtyId = targetSpecialty?.Id ?? snapshot.SpecialtyId,
                        PatientId = verifyPatientId,
                        CheckAiEnabledSpecialty = true
                    },
                    cancellationToken);

                bool dateMatches = !targetDate.HasValue || (slotVerify.SlotDate.HasValue && slotVerify.SlotDate.Value == targetDate.Value);
                if (!slotVerify.IsAvailable || !dateMatches)
                {
                    await PreserveExistingDraftAsync(request, responseDto, cancellationToken);
                    responseDto.Message = $"Khung giờ ở vị trí số {relSlotIdx + 1} trong danh sách hiển thị vừa được đặt hoặc không còn khả dụng ({slotVerify.FailureReason ?? "Đã kín lịch"}). Vui lòng chọn một khung giờ khác.";
                    responseDto.MissingFields = new List<string> { "TimeSlot" };
                    responseDto.DialogueOutcome = "ClarificationRequired";
                    return;
                }

                request.PendingSlotId = resolvedSlotId;
            }
            else
            {
                await PreserveExistingDraftAsync(request, responseDto, cancellationToken);
                responseDto.Message = "ClinicCare chưa rõ bạn muốn chọn khung giờ nào. Vui lòng chọn trực tiếp từ các khung giờ hiển thị hoặc nêu rõ giờ khám nhé.";
                responseDto.MissingFields = new List<string> { "TimeSlot" };
                responseDto.DialogueOutcome = "ClarificationRequired";
                return;
            }
        }

        if (targetDoctorId.HasValue)
        {
            var docMatch = allActiveDoctors.FirstOrDefault(d => d.DoctorId == targetDoctorId.Value);
            if (docMatch != null)
            {
                targetDoctorName = docMatch.FullName;
                targetDoctorAcademicTitle = docMatch.AcademicTitle;

                if (targetSpecialty == null && docMatch.Specialties.Any())
                {
                    targetSpecialty = docMatch.Specialties.First();
                }
                else if (targetSpecialty != null && !docMatch.Specialties.Any(s => s.Id == targetSpecialty.Id))
                {
                    responseDto.Message = $"Bác sĩ {docMatch.DisplayName} không thuộc chuyên khoa {targetSpecialty.Name}. Vui lòng chọn lại bác sĩ hoặc chuyên khoa phù hợp.";
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
            else
            {
                responseDto.Message = "Bác sĩ đã chọn không tồn tại hoặc đã ngừng hoạt động. Vui lòng chọn lại bác sĩ.";
                responseDto.BookingDraft = new AiBookingDraftDto
                {
                    SpecialtyId = targetSpecialty?.Id,
                    SpecialtyName = targetSpecialty?.Name,
                    SlotDate = targetDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    Reason = request.Reason?.Trim(),
                    IsComplete = false
                };
                responseDto.MissingFields = new List<string> { "Doctor", "TimeSlot" };
                return;
            }
        }
        else if (!string.IsNullOrWhiteSpace(localClassification.ExtractedDoctorName) || !string.IsNullOrWhiteSpace(aiResult.ExtractedDoctorName))
        {
            var doctorSearchName = !string.IsNullOrWhiteSpace(localClassification.ExtractedDoctorName)
                ? localClassification.ExtractedDoctorName
                : aiResult.ExtractedDoctorName!;

            // Negation filter: If user negated a doctor, exclude from search
            var negatedName = localClassification.NegatedDoctorName ?? aiResult.NegatedDoctorName;
            var searchableDoctors = allActiveDoctors;
            if (!string.IsNullOrWhiteSpace(negatedName))
            {
                var normNegated = NormalizeVietnamese(negatedName);
                searchableDoctors = searchableDoctors.Where(d =>
                {
                    var normDoc = NormalizeVietnamese(d.FullName);
                    return !normDoc.Contains(normNegated) && !normNegated.Contains(normDoc);
                }).ToList();
            }

            var matches = MatchDoctors(doctorSearchName, searchableDoctors);

            if (matches.Count == 0)
            {
                var unresolvedReason = !string.IsNullOrWhiteSpace(request.Reason) && VietnameseIntentClassifier.IsPlausibleClinicalReason(request.Reason)
                    ? request.Reason.Trim()
                    : RecoverInitialReason(cleanMessage, request.Context, request.Reason, aiResult.ExtractedReason);

                responseDto.Message = $"Hệ thống không tìm thấy bác sĩ nào có tên \"{aiResult.ExtractedDoctorName.Trim()}\" tại phòng khám ClinicCare. Bạn có thể xem danh sách bác sĩ của phòng khám hoặc cho tôi biết chuyên khoa/triệu chứng để được hỗ trợ nhé.";
                responseDto.BookingDraft = new AiBookingDraftDto
                {
                    SpecialtyId = targetSpecialty?.Id,
                    SpecialtyName = targetSpecialty?.Name,
                    SlotDate = targetDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    Reason = unresolvedReason,
                    IsComplete = false,
                    Version = ResolveDraftVersion(request.DraftVersion, hasSubstantiveChanges: true)
                };
                responseDto.Actions.Add(new AiActionDto
                {
                    Id = "act-view-doctors",
                    Type = AiActionTypes.ViewDoctors,
                    Label = "Xem danh sách bác sĩ",
                    Style = "primary",
                    RequiresAuthentication = false,
                    RequiresConfirmation = false,
                    Payload = new AiActionPayloadDto { TargetUrl = SafeRoutes.Doctors }
                });
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
                responseDto.Actions.Add(await CreateContactReceptionActionAsync(cancellationToken));
                responseDto.MissingFields = new List<string> { "Doctor", "TimeSlot" };
                return;
            }
            else if (matches.Count > 1)
            {
                var unresolvedReason = !string.IsNullOrWhiteSpace(request.Reason) && VietnameseIntentClassifier.IsPlausibleClinicalReason(request.Reason)
                    ? request.Reason.Trim()
                    : RecoverInitialReason(cleanMessage, request.Context, request.Reason, aiResult.ExtractedReason);

                var namesList = string.Join(", ", matches.Select(d => $"{d.DisplayName} ({string.Join(", ", d.Specialties.Select(s => s.Name))})"));
                responseDto.Message = $"Hệ thống tìm thấy {matches.Count} bác sĩ phù hợp: {namesList}. Bạn vui lòng chọn bác sĩ mong muốn khám nhé:";
                responseDto.BookingDraft = new AiBookingDraftDto
                {
                    SpecialtyId = targetSpecialty?.Id,
                    SpecialtyName = targetSpecialty?.Name,
                    SlotDate = targetDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    Reason = unresolvedReason,
                    IsComplete = false,
                    Version = ResolveDraftVersion(request.DraftVersion, hasSubstantiveChanges: true)
                };

                foreach (var doc in matches.Take(4))
                {
                    var primarySpec = doc.Specialties.FirstOrDefault();
                    responseDto.Actions.Add(new AiActionDto
                    {
                        Id = $"act-select-doc-{doc.DoctorId}",
                        Type = AiActionTypes.SelectDoctor,
                        Label = $"Chọn {doc.DisplayName}",
                        Description = primarySpec != null ? $"Khoa {primarySpec.Name}" : null,
                        Style = "secondary",
                        RequiresAuthentication = false,
                        RequiresConfirmation = false,
                        Payload = new AiActionPayloadDto
                        {
                            SpecialtyId = primarySpec?.Id,
                            SpecialtyName = primarySpec?.Name,
                            DoctorId = doc.DoctorId,
                            DoctorName = doc.DisplayName,
                            AcademicTitle = doc.AcademicTitle,
                            SlotDate = targetDate?.ToString("yyyy-MM-dd")
                        }
                    });
                }

                responseDto.MissingFields = new List<string> { "Doctor", "TimeSlot" };
                return;
            }
            else
            {
                // Exactly 1 match
                var singleDoc = matches[0];
                targetDoctorId = singleDoc.DoctorId;
                targetDoctorName = singleDoc.FullName;
                targetDoctorAcademicTitle = singleDoc.AcademicTitle;

                if (targetSpecialty == null)
                {
                    if (singleDoc.Specialties.Any())
                    {
                        targetSpecialty = singleDoc.Specialties.First();
                    }
                }
                else
                {
                    if (!singleDoc.Specialties.Any(s => s.Id == targetSpecialty.Id))
                    {
                        var docSpecNames = string.Join(", ", singleDoc.Specialties.Select(s => s.Name));
                        var primaryDocSpec = singleDoc.Specialties.FirstOrDefault();
                        responseDto.Message = $"Bác sĩ {singleDoc.DisplayName} thuộc chuyên khoa {docSpecNames}, không thuộc chuyên khoa {targetSpecialty.Name} bạn đã chọn trước đó. Bạn có muốn đổi sang chuyên khoa {primaryDocSpec?.Name} để khám với bác sĩ không?";
                        if (primaryDocSpec != null)
                        {
                            responseDto.Actions.Add(new AiActionDto
                            {
                                Id = $"act-switch-spec-doc-{singleDoc.DoctorId}",
                                Type = AiActionTypes.SelectDoctor,
                                Label = $"Đổi sang khoa {primaryDocSpec.Name} & chọn {singleDoc.DisplayName}",
                                Style = "primary",
                                RequiresAuthentication = false,
                                RequiresConfirmation = false,
                                Payload = new AiActionPayloadDto
                                {
                                    SpecialtyId = primaryDocSpec.Id,
                                    SpecialtyName = primaryDocSpec.Name,
                                    DoctorId = singleDoc.DoctorId,
                                    DoctorName = singleDoc.DisplayName,
                                    AcademicTitle = singleDoc.AcademicTitle,
                                    SlotDate = targetDate?.ToString("yyyy-MM-dd")
                                }
                            });
                        }
                        responseDto.MissingFields = new List<string> { "Doctor", "TimeSlot" };
                        return;
                    }
                }
            }
        }

        if (targetSpecialty != null)
        {
            var currentSpecialty = targetSpecialty;
            var activeDoctorsInSpec = allActiveDoctors
                .Where(d => d.Specialties.Any(s => s.Id == targetSpecialty.Id))
                .ToList();

            if (activeDoctorsInSpec.Count == 0)
            {
                var unresolvedReason = !string.IsNullOrWhiteSpace(request.Reason)
                    ? request.Reason.Trim()
                    : RecoverInitialReason(cleanMessage, request.Context, request.Reason, aiResult.ExtractedReason);
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

            // Sunday Rule: If user requested Sunday, do NOT query slots; inform user and suggest Monday
            if (requestedSunday)
            {
                var nextMonday = sundayDate.AddDays(1);
                responseDto.Message = $"Phòng khám không mở lịch khám vào Chủ nhật ({sundayDate:dd/MM/yyyy}). Bạn có thể chọn ngày làm việc kế tiếp (Thứ Hai, {nextMonday:dd/MM/yyyy}) hoặc một ngày khác nhé.";

                var sundayReason = !string.IsNullOrWhiteSpace(request.Reason)
                    ? request.Reason.Trim()
                    : RecoverInitialReason(cleanMessage, request.Context, request.Reason, aiResult.ExtractedReason);

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
                DoctorIds = targetDoctorId.HasValue ? null : activeDoctorsInSpec.Select(d => d.DoctorId).ToList(),
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
                    request.PendingSlotId = null;
                    responseDto.DialogueOutcome = "ClarificationRequired";
                }
            }
            else if (aiResult.WantsEarliest && availableSlots.Any())
            {
                chosenSlot = availableSlots.First();
                targetDoctorId = chosenSlot.DoctorId;
                var doc = activeDoctorsInSpec.FirstOrDefault(d => d.DoctorId == chosenSlot.DoctorId);
                if (doc != null)
                {
                    targetDoctorName = doc.FullName;
                    targetDoctorAcademicTitle = doc.AcademicTitle;
                }
                targetDate = chosenSlot.SlotDate;
            }

            // F. Build Booking Draft
            var reason = RecoverInitialReason(cleanMessage, request.Context, request.Reason, aiResult.ExtractedReason);
            var hasValidReason = AiActionValidator.IsValidBookingReason(reason);

            bool hasSubstantiveChanges = isDateModified || isDoctorModified ||
                string.Equals(responseDto.DialogueOutcome, "DraftModified", StringComparison.OrdinalIgnoreCase);

            if (targetSpecialty?.Id != request.PendingSpecialtyId)
            {
                hasSubstantiveChanges = true;
            }
            if (targetDoctorId != request.PendingDoctorId)
            {
                hasSubstantiveChanges = true;
            }
            if (chosenSlot?.Id != request.PendingSlotId)
            {
                hasSubstantiveChanges = true;
            }
            if (!string.IsNullOrWhiteSpace(request.PendingSlotDate) && targetDate.HasValue &&
                request.PendingSlotDate != targetDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
            {
                hasSubstantiveChanges = true;
            }
            if (!string.IsNullOrWhiteSpace(reason) && !string.Equals(reason, request.Reason, StringComparison.OrdinalIgnoreCase))
            {
                hasSubstantiveChanges = true;
            }

            var currentDraftVersion = ResolveDraftVersion(request.DraftVersion, hasSubstantiveChanges);

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
                IsComplete = targetDoctorId.HasValue && chosenSlot != null && hasValidReason,
                Version = currentDraftVersion
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

            bool isSlotOrDoctorAction = string.Equals(aiResult.PrimaryIntent, AiChatIntentTypes.SelectSlot, StringComparison.OrdinalIgnoreCase) ||
                                       string.Equals(aiResult.PrimaryIntent, AiChatIntentTypes.SelectDoctor, StringComparison.OrdinalIgnoreCase) ||
                                       string.Equals(aiResult.PrimaryIntent, AiChatIntentTypes.ProvideReason, StringComparison.OrdinalIgnoreCase) ||
                                       string.Equals(aiResult.PrimaryIntent, AiChatIntentTypes.StartBooking, StringComparison.OrdinalIgnoreCase) ||
                                       request.PendingSlotId.HasValue;

            bool isPassiveTurn = string.Equals(aiResult.PrimaryIntent, AiChatIntentTypes.Greeting, StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(aiResult.PrimaryIntent, AiChatIntentTypes.PricingInquiry, StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(aiResult.PrimaryIntent, AiChatIntentTypes.FacilityInquiry, StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(aiResult.PrimaryIntent, AiChatIntentTypes.ViewAppointments, StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(aiResult.PrimaryIntent, AiChatIntentTypes.UnclearOrOutOfScope, StringComparison.OrdinalIgnoreCase);

            if (isSlotOrDoctorAction && chosenSlot != null && !hasValidReason && !isPassiveTurn)
            {
                if (!string.IsNullOrWhiteSpace(reason))
                {
                    responseDto.Message = $"Đã ghi nhận chuyên khoa, bác sĩ, khung giờ khám và triệu chứng sơ bộ: \"{reason}\". Bạn vui lòng chia sẻ thêm một chút thông tin về triệu chứng (từ 10 đến 500 ký tự) trước khi xem lại và xác nhận đặt lịch nhé.";
                }
                else
                {
                    responseDto.Message = "Đã ghi nhận chuyên khoa, bác sĩ và khung giờ bạn chọn. Vui lòng bổ sung lý do khám từ 10 đến 500 ký tự trước khi xem lại và xác nhận đặt lịch.";
                }
            }
            else if (availableSlots.Count == 0 && chosenSlot == null && !request.PendingSlotId.HasValue && !isPassiveTurn)
            {
                var rangeFrom = batchRequest.FromDate;
                var rangeTo = batchRequest.ToDate;
                if (targetDoctorId.HasValue)
                {
                    responseDto.Message = $"Bác sĩ {targetDoctorName ?? "đã chọn"} hiện chưa có khung giờ khám trống trong khoảng {rangeFrom:dd/MM/yyyy} đến {rangeTo:dd/MM/yyyy}. Bạn có thể xem lịch của bác sĩ khác trong khoa {targetSpecialty.Name} hoặc chọn ngày khác nhé.";
                }
                else
                {
                    responseDto.Message = $"Không có lịch trống phù hợp trong khoảng {rangeFrom:dd/MM/yyyy} đến {rangeTo:dd/MM/yyyy}. Vui lòng chọn một khoảng ngày khác để tìm tiếp.";
                }
            }

            // G. Add Contextual Actions
            if (draft.IsComplete)
            {
                var confirmationId = Guid.NewGuid().ToString("N");
                draft.ConfirmationId = confirmationId;
                responseDto.DialogueOutcome = "PendingConfirmation";

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
                    DraftVersion = currentDraftVersion,
                    Payload = new AiActionPayloadDto
                    {
                        ConfirmationId = confirmationId,
                        SpecialtyId = draft.SpecialtyId,
                        SpecialtyName = draft.SpecialtyName,
                        DoctorId = draft.DoctorId,
                        DoctorName = draft.DoctorName,
                        SlotId = draft.SlotId,
                        SlotDate = draft.SlotDate,
                        StartTime = draft.StartTime,
                        EndTime = draft.EndTime,
                        Reason = draft.Reason,
                        DraftVersion = currentDraftVersion
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
                    DraftVersion = currentDraftVersion,
                    Payload = new AiActionPayloadDto
                    {
                        ConfirmationId = confirmationId,
                        SpecialtyId = draft.SpecialtyId,
                        DoctorId = draft.DoctorId,
                        SlotId = draft.SlotId,
                        SlotDate = draft.SlotDate,
                        StartTime = draft.StartTime,
                        EndTime = draft.EndTime,
                        Reason = draft.Reason,
                        DraftVersion = currentDraftVersion
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
                            Id = $"act-select-doc-{doc.DoctorId}",
                            Type = AiActionTypes.SelectDoctor,
                            Label = $"Chọn {docDisplayName}",
                            Style = "secondary",
                            RequiresAuthentication = false,
                            RequiresConfirmation = false,
                            Payload = new AiActionPayloadDto
                            {
                                SpecialtyId = currentSpecialty.Id,
                                SpecialtyName = currentSpecialty.Name,
                                DoctorId = doc.DoctorId,
                                DoctorName = docDisplayName,
                                AcademicTitle = doc.AcademicTitle,
                                SlotDate = targetDate?.ToString("yyyy-MM-dd")
                            }
                        });
                    }
                }
                // If Doctor selected but slot not chosen, suggest available slots
                else if (availableSlots.Any() && !isPassiveTurn)
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
                else if (targetDoctorId.HasValue && !availableSlots.Any())
                {
                    var otherDocs = activeDoctorsInSpec.Where(d => d.DoctorId != targetDoctorId.Value).Take(2).ToList();
                    foreach (var doc in otherDocs)
                    {
                        var docDisplayName = string.IsNullOrWhiteSpace(doc.AcademicTitle) ? doc.FullName : $"{doc.AcademicTitle} {doc.FullName}";
                        responseDto.Actions.Add(new AiActionDto
                        {
                            Id = $"act-select-doc-{doc.DoctorId}",
                            Type = AiActionTypes.SelectDoctor,
                            Label = $"Khám với {docDisplayName}",
                            Style = "secondary",
                            RequiresAuthentication = false,
                            RequiresConfirmation = false,
                            Payload = new AiActionPayloadDto
                            {
                                SpecialtyId = currentSpecialty.Id,
                                SpecialtyName = currentSpecialty.Name,
                                DoctorId = doc.DoctorId,
                                DoctorName = docDisplayName,
                                AcademicTitle = doc.AcademicTitle,
                                SlotDate = targetDate?.ToString("yyyy-MM-dd")
                            }
                        });
                    }

                    responseDto.Actions.Add(new AiActionDto
                    {
                        Id = "act-view-doctors",
                        Type = AiActionTypes.ViewDoctors,
                        Label = "Xem danh sách bác sĩ",
                        Style = "secondary",
                        RequiresAuthentication = false,
                        RequiresConfirmation = false,
                        Payload = new AiActionPayloadDto { TargetUrl = SafeRoutes.Doctors }
                    });

                    responseDto.Actions.Add(await CreateContactReceptionActionAsync(cancellationToken));
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

        // 5. Contact Reception (No fake /contact route; strictly honest contact info from DB)
        if (string.Equals(requestedActionType, AiActionTypes.ContactReception, StringComparison.OrdinalIgnoreCase)
            || lowerMsg.Contains("lễ tân") || lowerMsg.Contains("tiếp đón") || lowerMsg.Contains("liên hệ phòng khám"))
        {
            var contactAction = await CreateContactReceptionActionAsync(cancellationToken);
            responseDto.Actions.Add(contactAction);
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
        if (string.IsNullOrWhiteSpace(text)) return null;
        var clean = text.Trim().ToLowerInvariant();

        var extractedToken = VietnameseIntentClassifier.ExtractDateTokenFromText(clean) ?? clean;
        if (DateOnly.TryParseExact(extractedToken, new[] { "yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var exactDate))
        {
            return exactDate;
        }

        if (Regex.IsMatch(clean, @"\b(?:hôm\s+nay|sáng\s+nay|chiều\s+nay|tối\s+nay)\b") || clean == "today")
        {
            return today;
        }
        if (Regex.IsMatch(clean, @"\b(?:ngày\s+mai|sáng\s+mai|chiều\s+mai|tối\s+mai)\b") || clean == "tomorrow" || clean == "mai")
        {
            return today.AddDays(1);
        }
        if (Regex.IsMatch(clean, @"\b(?:ngày\s+kia|ngày\s+mốt|mốt)\b"))
        {
            return today.AddDays(2);
        }

        // Days of week
        var dayMap = new Dictionary<string, DayOfWeek>
        {
            { "chủ nhật", DayOfWeek.Sunday },
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

    private async Task PreserveExistingDraftAsync(
        AiChatRequestDto request,
        AiChatResponseDto responseDto,
        CancellationToken cancellationToken)
    {
        string? validReason = !string.IsNullOrWhiteSpace(request.Reason) && !VietnameseIntentClassifier.IsDisallowedReason(request.Reason)
            ? request.Reason.Trim()
            : null;

        if (request.PendingSpecialtyId.HasValue && request.PendingSpecialtyId.Value > 0)
        {
            var spec = await _dbContext.Specialties.FindAsync(new object[] { request.PendingSpecialtyId.Value }, cancellationToken);
            string? docName = null;
            if (request.PendingDoctorId.HasValue)
            {
                var doc = await (from d in _dbContext.Doctors
                                 join u in _dbContext.Users on d.UserId equals u.Id
                                 where d.Id == request.PendingDoctorId.Value
                                 select new { u.FullName, d.AcademicTitle }).FirstOrDefaultAsync(cancellationToken);
                if (doc != null)
                {
                    docName = FormatDoctorName(doc.AcademicTitle, doc.FullName);
                }
            }

            long? validSlotId = null;
            string? slotDate = request.PendingSlotDate;
            string? slotStartTime = null;
            string? slotEndTime = null;

            if (request.PendingSlotId.HasValue && request.PendingSlotId.Value > 0)
            {
                var eval = await _availabilityPolicy.EvaluateSlotAvailabilityAsync(new ClinicManagement.Application.Appointments.Interfaces.SlotAvailabilityRequest
                {
                    SlotId = request.PendingSlotId.Value,
                    DoctorId = request.PendingDoctorId,
                    SpecialtyId = request.PendingSpecialtyId.Value,
                    CheckAiEnabledSpecialty = true
                }, cancellationToken);

                if (eval.IsAvailable)
                {
                    validSlotId = request.PendingSlotId.Value;
                    slotDate = eval.SlotDate?.ToString("yyyy-MM-dd");
                    slotStartTime = eval.StartTime?.ToString("HH:mm");
                    slotEndTime = eval.EndTime?.ToString("HH:mm");
                }
            }

            responseDto.BookingDraft = new AiBookingDraftDto
            {
                SpecialtyId = request.PendingSpecialtyId.Value,
                SpecialtyName = spec?.Name,
                DoctorId = request.PendingDoctorId,
                DoctorName = docName,
                SlotId = validSlotId,
                SlotDate = slotDate,
                StartTime = slotStartTime,
                EndTime = slotEndTime,
                Reason = validReason,
                IsComplete = request.PendingDoctorId.HasValue && validSlotId.HasValue && AiActionValidator.IsValidBookingReason(validReason),
                Version = ResolveDraftVersion(request.DraftVersion, hasSubstantiveChanges: false)
            };
        }
        else if (!string.IsNullOrWhiteSpace(validReason))
        {
            responseDto.BookingDraft = new AiBookingDraftDto
            {
                Reason = validReason,
                IsComplete = false,
                Version = ResolveDraftVersion(request.DraftVersion, hasSubstantiveChanges: false)
            };
        }
    }

    private async Task<AiChatResponseDto> HandlePricingInquiryAsync(
        AiChatRequestDto request,
        string cleanMessage,
        int nextDraftVersion,
        CancellationToken cancellationToken)
    {
        var specialtiesWithFee = await _dbContext.Specialties
            .AsNoTracking()
            .Where(s => s.IsActive && s.AiEnabled && s.ConsultationFee > 0)
            .OrderBy(s => s.Id)
            .ToListAsync(cancellationToken);

        var response = new AiChatResponseDto
        {
            Urgency = "ROUTINE",
            PromptVersion = GeminiAiProvider.CurrentPromptVersion,
            DialogueOutcome = "PricingInquiryResolved",
            PrimaryIntent = AiChatIntentTypes.PricingInquiry,
            AssistantStatus = "Online",
            ProviderStatus = "NotCalled"
        };

        var lower = cleanMessage.ToLowerInvariant();
        ClinicManagement.Domain.Entities.Specialty? targetSpec = specialtiesWithFee
            .FirstOrDefault(s => lower.Contains(s.Name.ToLowerInvariant())
                || (!string.IsNullOrWhiteSpace(s.SpecialtyCode) && lower.Contains(s.SpecialtyCode.ToLowerInvariant()))
                || (!string.IsNullOrWhiteSpace(s.Name.ToLowerInvariant().Replace("khoa", "").Trim()) &&
                    Regex.IsMatch(lower, $@"\b{Regex.Escape(s.Name.ToLowerInvariant().Replace("khoa", "").Trim())}\b")));

        if (targetSpec == null && request.PendingSpecialtyId.HasValue)
        {
            targetSpec = specialtiesWithFee.FirstOrDefault(s => s.Id == request.PendingSpecialtyId.Value);
        }

        if (targetSpec != null)
        {
            response.Message = $"Chi phí khám chuyên khoa **{targetSpec.Name}** tại ClinicCare là **{targetSpec.ConsultationFee:N0} VNĐ** / lượt khám (chưa bao gồm các dịch vụ cận lâm sàng hoặc thuốc nếu có chỉ định của bác sĩ).";
        }
        else
        {
            var sb = new StringBuilder();
            sb.AppendLine("Bảng giá chi phí khám tham khảo tại Phòng khám ClinicCare theo từng chuyên khoa:");
            foreach (var s in specialtiesWithFee.Take(6))
            {
                sb.AppendLine($"- **Khoa {s.Name}**: {s.ConsultationFee:N0} VNĐ / lượt khám");
            }
            sb.AppendLine("\n*Lưu ý: Chi phí trên là phí khám ban đầu, chưa bao gồm các chỉ định cận lâm sàng phát sinh nếu có.*");
            response.Message = sb.ToString();
        }

        response.Actions.Add(new AiActionDto
        {
            Id = "act-start-booking",
            Type = AiActionTypes.StartBooking,
            Label = "Đặt lịch khám ngay",
            Style = "primary",
            RequiresAuthentication = false,
            RequiresConfirmation = false,
            Payload = new AiActionPayloadDto { TargetUrl = SafeRoutes.BookAppointment }
        });

        response.Actions.Add(new AiActionDto
        {
            Id = "act-view-invoices",
            Type = AiActionTypes.ViewBills,
            Label = "Xem hóa đơn của tôi",
            Style = "secondary",
            RequiresAuthentication = true,
            RequiresConfirmation = false,
            Payload = new AiActionPayloadDto { TargetUrl = SafeRoutes.Invoices }
        });

        await PreserveExistingDraftAsync(request, response, cancellationToken);
        return WithDraftVersionSync(response, nextDraftVersion);
    }

    private async Task<AiChatResponseDto> HandleFacilityInquiryAsync(
        AiChatRequestDto request,
        string cleanMessage,
        int nextDraftVersion,
        CancellationToken cancellationToken)
    {
        var facility = await _dbContext.Facilities
            .AsNoTracking()
            .Where(f => f.IsActive)
            .OrderBy(f => f.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var response = new AiChatResponseDto
        {
            Urgency = "ROUTINE",
            PromptVersion = GeminiAiProvider.CurrentPromptVersion,
            DialogueOutcome = "FacilityInquiryResolved",
            PrimaryIntent = AiChatIntentTypes.FacilityInquiry,
            AssistantStatus = "Online",
            ProviderStatus = "NotCalled"
        };

        var facilityName = facility?.Name ?? "Phòng khám ClinicCare";
        var address = !string.IsNullOrWhiteSpace(facility?.Address)
            ? (!string.IsNullOrWhiteSpace(facility.City) ? $"{facility.Address.Trim()}, {facility.City.Trim()}" : facility.Address.Trim())
            : "Chưa cập nhật";
        var phone = !string.IsNullOrWhiteSpace(facility?.Phone) ? facility.Phone.Trim() : "Chưa cập nhật";
        var email = !string.IsNullOrWhiteSpace(facility?.Email) ? facility.Email.Trim() : "Chưa cập nhật";

        var sb = new StringBuilder();
        sb.AppendLine($"Thông tin liên hệ và cơ sở của **{facilityName}**:");
        sb.AppendLine($"- **Địa chỉ**: {address}");
        sb.AppendLine($"- **Hotline/SĐT**: {phone}");
        sb.AppendLine($"- **Email**: {email}");
        sb.AppendLine("- **Thời gian làm việc**: Thứ Hai - Thứ Bảy (07:30 - 17:00), Chủ nhật nghỉ.");
        response.Message = sb.ToString();

        var contactAction = await CreateContactReceptionActionAsync(cancellationToken);
        response.Actions.Add(contactAction);

        response.Actions.Add(new AiActionDto
        {
            Id = "act-start-booking",
            Type = AiActionTypes.StartBooking,
            Label = "Đặt lịch khám ngay",
            Style = "primary",
            RequiresAuthentication = false,
            RequiresConfirmation = false,
            Payload = new AiActionPayloadDto { TargetUrl = SafeRoutes.BookAppointment }
        });

        await PreserveExistingDraftAsync(request, response, cancellationToken);
        return WithDraftVersionSync(response, nextDraftVersion);
    }

    private async Task<AiChatResponseDto> HandleViewAppointmentsAsync(
        AiChatRequestDto request,
        int nextDraftVersion,
        CancellationToken cancellationToken)
    {
        var response = new AiChatResponseDto
        {
            Urgency = "ROUTINE",
            PromptVersion = GeminiAiProvider.CurrentPromptVersion,
            DialogueOutcome = "NavigationInquiryResolved",
            PrimaryIntent = AiChatIntentTypes.ViewAppointments,
            AssistantStatus = "Online",
            ProviderStatus = "NotCalled",
            Message = "Bạn có thể xem danh sách lịch khám đã đặt và lịch sử khám bệnh của mình tại mục Lịch hẹn."
        };

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
                    response.Message = $"Lịch khám gần nhất của bạn là mã **{apt.AppointmentCode}** vào ngày **{apt.AppointmentDate:dd/MM/yyyy}** ({apt.StartTime:hh\\:mm}). Bạn có thể mở chi tiết hoặc vào danh sách lịch hẹn để quản lý:";
                }
            }
        }

        response.Actions.Add(new AiActionDto
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

        if (verifiedAppointmentId.HasValue)
        {
            response.Actions.Add(new AiActionDto
            {
                Id = "act-nav-appointment-detail",
                Type = AiActionTypes.OpenAppointmentDetail,
                Label = $"Xem chi tiết lịch {verifiedAppointmentCode}",
                Description = "Mở xem chi tiết cuộc hẹn gần nhất",
                Style = "secondary",
                RequiresAuthentication = true,
                RequiresConfirmation = false,
                Payload = new AiActionPayloadDto
                {
                    AppointmentId = verifiedAppointmentId,
                    AppointmentCode = verifiedAppointmentCode,
                    TargetUrl = $"{SafeRoutes.Appointments}?appointmentId={verifiedAppointmentId.Value}"
                }
            });
        }

        await PreserveExistingDraftAsync(request, response, cancellationToken);
        return WithDraftVersionSync(response, nextDraftVersion);
    }

    private async Task<AiChatResponseDto> HandleConfirmBookingIntentAsync(
        AiChatRequestDto request,
        int nextDraftVersion,
        CancellationToken cancellationToken)
    {
        var response = new AiChatResponseDto
        {
            Urgency = "ROUTINE",
            PromptVersion = GeminiAiProvider.CurrentPromptVersion,
            PrimaryIntent = AiChatIntentTypes.ConfirmBooking,
            AssistantStatus = "Online",
            ProviderStatus = "NotCalled"
        };

        await PreserveExistingDraftAsync(request, response, cancellationToken);
        var draft = response.BookingDraft;

        bool hasSpec = draft?.SpecialtyId != null;
        bool hasDoc = draft?.DoctorId != null;
        bool hasSlot = draft?.SlotId != null;
        bool hasValidReason = AiActionValidator.IsValidBookingReason(draft?.Reason);

        if (hasSpec && hasDoc && hasSlot && hasValidReason)
        {
            var currentUserId = _currentUserService.UserId;
            if (string.IsNullOrWhiteSpace(request.SessionId))
            {
                request.SessionId = $"sess_{Guid.NewGuid():N}";
            }
            if (string.IsNullOrWhiteSpace(request.DraftId))
            {
                request.DraftId = $"draft_{Guid.NewGuid():N}";
            }
            if (!currentUserId.HasValue)
            {
                response.DialogueOutcome = "ClarificationRequired";
                response.Message = "Không thể xác định phiên hoặc bản nháp hiện tại. Vui lòng tải lại phiên đặt lịch rồi xác nhận lại.";
                return WithDraftVersionSync(response, nextDraftVersion);
            }

            var patient = await _dbContext.Patients.AsNoTracking()
                .FirstOrDefaultAsync(x => x.UserId == currentUserId.Value, cancellationToken);
            var slotCheck = await _availabilityPolicy.EvaluateSlotAvailabilityAsync(new ClinicManagement.Application.Appointments.Interfaces.SlotAvailabilityRequest
            {
                SlotId = draft!.SlotId!.Value,
                DoctorId = draft.DoctorId!.Value,
                SpecialtyId = draft.SpecialtyId!.Value,
                PatientId = patient?.Id,
                CheckAiEnabledSpecialty = true
            }, cancellationToken);
            if (!slotCheck.IsAvailable || !slotCheck.SlotDate.HasValue || !slotCheck.StartTime.HasValue || !slotCheck.EndTime.HasValue)
            {
                response.DialogueOutcome = "ClarificationRequired";
                response.Message = slotCheck.FailureReason ?? "Khung giờ đã thay đổi hoặc không còn khả dụng. Vui lòng chọn lại khung giờ mới.";
                response.MissingFields = new List<string> { "TimeSlot" };
                return WithDraftVersionSync(response, nextDraftVersion);
            }

            var snapshotId = request.ContextSnapshotId;
            if (string.IsNullOrWhiteSpace(snapshotId))
            {
                var snapshot = await _snapshotStore.CreateSnapshotAsync(new CreateSnapshotRequest
                {
                    UserId = currentUserId,
                    SessionId = request.SessionId,
                    DraftId = request.DraftId,
                    DraftVersion = nextDraftVersion,
                    SpecialtyId = draft.SpecialtyId,
                    DoctorId = draft.DoctorId,
                    SlotDate = slotCheck.SlotDate.Value.ToString("yyyy-MM-dd"),
                    DoctorIds = new List<long> { draft.DoctorId.Value },
                    SlotIds = new List<long> { draft.SlotId.Value }
                }, cancellationToken);
                snapshotId = snapshot.SnapshotId;
            }
            else
            {
                var snapshotValidation = await _snapshotStore.ValidateSnapshotAsync(new ValidateSnapshotRequest
                {
                    SnapshotId = snapshotId,
                    CurrentUserId = currentUserId,
                    CurrentSessionId = request.SessionId,
                    CurrentDraftId = request.DraftId,
                    CurrentDraftVersion = nextDraftVersion,
                    CurrentSpecialtyId = draft.SpecialtyId,
                    RequestedDoctorIds = new List<long> { draft.DoctorId.Value },
                    RequestedSlotIds = new List<long> { draft.SlotId.Value },
                    NowUtc = _dateTimeProvider.UtcNow
                }, cancellationToken);
                if (!snapshotValidation.IsValid)
                {
                    response.DialogueOutcome = "ClarificationRequired";
                    response.Message = snapshotValidation.ErrorMessage ?? "Danh sách lựa chọn đã thay đổi. Vui lòng chọn lại rồi xác nhận.";
                    response.MissingFields = new List<string> { "Selection" };
                    return WithDraftVersionSync(response, nextDraftVersion);
                }
            }

            var touch = await _snapshotStore.TouchSessionAsync(
                request.SessionId,
                currentUserId,
                request.DraftId,
                nextDraftVersion,
                facilityId: null,
                cancellationToken: cancellationToken);
            if (!touch.IsAccepted)
            {
                return BuildSessionFailureResponse(request, touch);
            }

            var confirmation = await _confirmationStore.CreateAsync(new CreateAiBookingConfirmationRequest
            {
                UserId = currentUserId.Value,
                SessionId = request.SessionId.Trim(),
                DraftId = request.DraftId.Trim(),
                DraftVersion = nextDraftVersion,
                ContextSnapshotId = snapshotId,
                SpecialtyId = draft.SpecialtyId.Value,
                DoctorId = draft.DoctorId.Value,
                SlotId = draft.SlotId.Value,
                SlotDate = slotCheck.SlotDate.Value,
                StartTime = slotCheck.StartTime.Value,
                EndTime = slotCheck.EndTime.Value,
                Reason = draft.Reason!
            }, cancellationToken);

            var confirmationId = confirmation.ConfirmationId;
            draft.ConfirmationId = confirmationId;
            draft.DraftId = request.DraftId.Trim();
            response.ContextSnapshotId = snapshotId;
            response.SessionId = request.SessionId.Trim();
            response.DraftId = request.DraftId.Trim();

            response.DialogueOutcome = "PendingConfirmation";
            response.Message = "Thông tin lịch khám đã đầy đủ và sẵn sàng xác nhận. Bạn vui lòng bấm nút \"Xác nhận đặt lịch\" bên dưới để hoàn tất nhé:";

            response.Actions.Add(new AiActionDto
            {
                Id = $"act-confirm-booking-{draft.SlotId}",
                Type = AiActionTypes.ConfirmBooking,
                Label = "Xác nhận đặt lịch",
                Description = $"Xác nhận khám với {draft.DoctorName} vào ngày {draft.SlotDate} ({draft.StartTime} - {draft.EndTime})",
                Style = "primary",
                RequiresAuthentication = true,
                RequiresConfirmation = true,
                DraftVersion = nextDraftVersion,
                Payload = new AiActionPayloadDto
                {
                    ConfirmationId = confirmationId,
                    SpecialtyId = draft.SpecialtyId,
                    SpecialtyName = draft.SpecialtyName,
                    DoctorId = draft.DoctorId,
                    DoctorName = draft.DoctorName,
                    SlotId = draft.SlotId,
                    SlotDate = draft.SlotDate,
                    StartTime = draft.StartTime,
                    EndTime = draft.EndTime,
                    Reason = draft.Reason,
                    DraftVersion = nextDraftVersion
                }
            });

            draft.IsComplete = true;
        }
        else
        {
            response.DialogueOutcome = "ClarificationRequired";
            var missingParts = new List<string>();
            if (!hasDoc) missingParts.Add("bác sĩ");
            if (!hasSlot) missingParts.Add("khung giờ khám");
            if (!hasValidReason)
            {
                if (draft?.Reason != null && draft.Reason.Length > 0)
                {
                    missingParts.Add($"bổ sung lý do khám từ 10 đến 500 ký tự (hiện có: \"{draft.Reason}\")");
                }
                else
                {
                    missingParts.Add("lý do khám từ 10 đến 500 ký tự");
                }
            }

            response.Message = $"Để chốt lịch khám, bạn vui lòng hoàn thiện thêm thông tin: {string.Join(", ", missingParts)} trước khi xác nhận nhé.";

            if (!hasDoc && draft?.SpecialtyId != null)
            {
                var activeDocs = await QueryActiveDoctorsAsync(cancellationToken);
                var docsInSpec = activeDocs.Where(d => d.Specialties.Any(s => s.Id == draft.SpecialtyId)).ToList();
                foreach (var doc in docsInSpec.Take(3))
                {
                    response.Actions.Add(new AiActionDto
                    {
                        Id = $"act-select-doc-{doc.DoctorId}",
                        Type = AiActionTypes.SelectDoctor,
                        Label = $"Chọn {doc.DisplayName}",
                        Style = "secondary",
                        RequiresAuthentication = false,
                        RequiresConfirmation = false,
                        Payload = new AiActionPayloadDto
                        {
                            SpecialtyId = draft.SpecialtyId,
                            SpecialtyName = draft.SpecialtyName,
                            DoctorId = doc.DoctorId,
                            DoctorName = doc.DisplayName,
                            AcademicTitle = doc.AcademicTitle
                        }
                    });
                }
            }
        }

        return WithDraftVersionSync(response, nextDraftVersion);
    }

    private static AiChatResponseDto BuildSessionFailureResponse(AiChatRequestDto request, AiSessionTouchResult result)
    {
        return new AiChatResponseDto
        {
            Message = result.ErrorMessage ?? "Phiên làm việc không còn hợp lệ. Vui lòng bắt đầu lại thao tác đặt lịch.",
            DialogueOutcome = "SessionRejected",
            PrimaryIntent = request.Intent,
            AssistantStatus = "Online",
            ProviderStatus = "NotCalled",
            SessionId = request.SessionId,
            DraftId = null,
            ManualSelectionRequired = true
        };
    }

    private async Task<AiChatResponseDto> HandleReviewDraftIntentAsync(
        AiChatRequestDto request,
        int nextDraftVersion,
        CancellationToken cancellationToken)
    {
        var response = new AiChatResponseDto
        {
            Urgency = "ROUTINE",
            PromptVersion = GeminiAiProvider.CurrentPromptVersion,
            PrimaryIntent = AiChatIntentTypes.ReviewDraft,
            DialogueOutcome = "Success",
            AssistantStatus = "Online",
            ProviderStatus = "NotCalled"
        };

        await PreserveExistingDraftAsync(request, response, cancellationToken);
        var draft = response.BookingDraft;

        if (draft == null || (draft.SpecialtyId == null && draft.DoctorId == null))
        {
            response.Message = "Hiện tại bạn chưa có bản nháp lịch khám nào. Bạn có muốn chọn chuyên khoa hoặc tìm bác sĩ để đặt lịch không?";
            response.Actions.Add(new AiActionDto
            {
                Id = "act-start-booking",
                Type = AiActionTypes.StartBooking,
                Label = "Bắt đầu đặt lịch",
                Style = "primary",
                RequiresAuthentication = false,
                RequiresConfirmation = false,
                Payload = new AiActionPayloadDto { TargetUrl = SafeRoutes.BookAppointment }
            });
        }
        else
        {
            var sb = new StringBuilder();
            sb.AppendLine("Thông tin tóm tắt lịch khám hiện tại của bạn:");
            if (draft.SpecialtyName != null) sb.AppendLine($"- **Chuyên khoa**: {draft.SpecialtyName}");
            if (draft.DoctorName != null) sb.AppendLine($"- **Bác sĩ**: {draft.DoctorName}");
            if (draft.SlotDate != null) sb.AppendLine($"- **Ngày khám**: {draft.SlotDate}");
            if (draft.StartTime != null && draft.EndTime != null) sb.AppendLine($"- **Khung giờ**: {draft.StartTime} - {draft.EndTime}");
            if (draft.Reason != null) sb.AppendLine($"- **Lý do khám**: {draft.Reason}");

            response.Message = sb.ToString();

            response.Actions.Add(new AiActionDto
            {
                Id = "act-review-booking",
                Type = AiActionTypes.ReviewBooking,
                Label = "Xem chi tiết bản nháp",
                Style = "primary",
                RequiresAuthentication = true,
                RequiresConfirmation = false,
                DraftVersion = nextDraftVersion,
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
                    Reason = draft.Reason,
                    DraftVersion = nextDraftVersion
                }
            });
        }

        return WithDraftVersionSync(response, nextDraftVersion);
    }

    private async Task<AiChatResponseDto> BuildDegradedResponseAsync(
        AiChatRequestDto request,
        string cleanMessage,
        string providerStatus,
        CancellationToken cancellationToken)
    {
        var nextDraftVersion = ResolveDraftVersion(request.DraftVersion);
        string userMessage = providerStatus switch
        {
            "Disabled" => "Tính năng trợ lý AI hiện đang tạm bảo trì hoặc chưa được cấu hình. Hệ thống đã chuyển sang chế độ hỗ trợ cơ bản để bạn có thể tra cứu và đặt lịch trực tiếp.",
            "RateLimited" => "Dịch vụ AI đang nhận quá nhiều yêu cầu vào thời điểm này. Hệ thống đã chuyển sang chế độ hỗ trợ cơ bản để bạn có thể tiếp tục tra cứu và đặt lịch.",
            "AuthFailure" or "InvalidModelOrEndpoint" => "Dịch vụ AI đang gặp sự cố cấu hình xác thực. Hệ thống chuyển sang chế độ hỗ trợ cơ bản để bạn tra cứu và đặt lịch trực tiếp.",
            _ => "Kết nối đến dịch vụ AI tạm thời bị gián đoạn. Hệ thống đã chuyển sang chế độ hỗ trợ cơ bản để bạn có thể tiếp tục tra cứu và đặt lịch trực tiếp."
        };

        var response = new AiChatResponseDto
        {
            Message = userMessage,
            Urgency = "ROUTINE",
            PromptVersion = GeminiAiProvider.CurrentPromptVersion,
            ManualSelectionRequired = true,
            AssistantStatus = "Degraded",
            ProviderStatus = providerStatus,
            SpecialtySuggestions = new List<AiSpecialtySuggestionDto>()
        };

        // Preserve validated user booking draft if present in request
        var recoveredReason = RecoverInitialReason(cleanMessage, request.Context, request.Reason, null);
        if (request.PendingSpecialtyId.HasValue)
        {
            var spec = await _dbContext.Specialties.FindAsync(new object[] { request.PendingSpecialtyId.Value }, cancellationToken);
            if (spec != null)
            {
                response.BookingDraft = new AiBookingDraftDto
                {
                    SpecialtyId = spec.Id,
                    SpecialtyName = spec.Name,
                    DoctorId = request.PendingDoctorId,
                    SlotId = request.PendingSlotId,
                    SlotDate = request.PendingSlotDate,
                    Reason = recoveredReason,
                    IsComplete = false,
                    Version = nextDraftVersion
                };
            }
        }
        else if (!string.IsNullOrWhiteSpace(recoveredReason))
        {
            response.BookingDraft = new AiBookingDraftDto
            {
                Reason = recoveredReason,
                IsComplete = false,
                Version = nextDraftVersion
            };
        }

        // Add safe independent actions
        response.Actions.Add(new AiActionDto
        {
            Id = "act-manual-spec",
            Type = AiActionTypes.ManualSpecialtySelection,
            Label = "Chọn chuyên khoa thủ công",
            Style = "primary",
            RequiresAuthentication = false,
            RequiresConfirmation = false,
            Payload = new AiActionPayloadDto { TargetUrl = SafeRoutes.BookAppointment }
        });

        response.Actions.Add(new AiActionDto
        {
            Id = "act-view-doctors",
            Type = AiActionTypes.ViewDoctors,
            Label = "Xem danh sách bác sĩ",
            Style = "secondary",
            RequiresAuthentication = false,
            RequiresConfirmation = false,
            Payload = new AiActionPayloadDto { TargetUrl = SafeRoutes.Doctors }
        });

        var contactAction = await CreateContactReceptionActionAsync(cancellationToken);
        response.Actions.Add(contactAction);

        response.Actions.Add(new AiActionDto
        {
            Id = "act-my-appointments",
            Type = AiActionTypes.ViewMyAppointments,
            Label = "Xem lịch hẹn của tôi",
            Style = "secondary",
            RequiresAuthentication = true,
            RequiresConfirmation = false,
            Payload = new AiActionPayloadDto { TargetUrl = SafeRoutes.Appointments }
        });

        // Ground doctor check in degraded mode if user mentioned a doctor
        var doctorNamePattern = new Regex(@"(?:bác sĩ|bac si|bs\.|bs|bác sỹ|bac sy)\s+([A-Za-z0-9À-ỹ\s]+)", RegexOptions.IgnoreCase);
        var docMatch = doctorNamePattern.Match(cleanMessage);
        if (docMatch.Success)
        {
            var rawName = docMatch.Groups[1].Value;
            var delimiterMatch = Regex.Match(rawName, @"^(.*?)(?:\s+(?:vào|từ|lúc|ngày|khoa|chuyên khoa|sáng|chiều|tối)\b|[,\.\?!])", RegexOptions.IgnoreCase);
            var candidateName = (delimiterMatch.Success ? delimiterMatch.Groups[1].Value : rawName).Trim();
            var normCandidate = NormalizeVietnamese(candidateName);
            var nonNames = new HashSet<string> { "", "oi", "a", "nhe", "cho", "hoi", "oi cho toi hoi", "giup", "tu van", "kham" };
            if (!string.IsNullOrWhiteSpace(normCandidate) && normCandidate.Length >= 2 && !nonNames.Contains(normCandidate))
            {
                var allDoctors = await QueryActiveDoctorsAsync(cancellationToken);
                var matches = MatchDoctors(candidateName, allDoctors);
                if (matches.Count == 1)
                {
                    var doc = matches[0];
                    var primarySpec = doc.Specialties.FirstOrDefault();
                    userMessage = $"{userMessage}\n\nTuy nhiên, hệ thống đã tìm thấy bác sĩ {doc.DisplayName}{(primarySpec != null ? $" thuộc chuyên khoa {primarySpec.Name}" : "")}. Bạn có thể bấm chọn bác sĩ bên dưới để tiếp tục:";
                    response.Message = userMessage;

                    response.Actions.Insert(0, new AiActionDto
                    {
                        Id = $"act-select-doc-{doc.DoctorId}",
                        Type = AiActionTypes.SelectDoctor,
                        Label = $"Chọn {doc.DisplayName}",
                        Description = primarySpec != null ? $"Khoa {primarySpec.Name}" : null,
                        Style = "primary",
                        RequiresAuthentication = false,
                        RequiresConfirmation = false,
                        Payload = new AiActionPayloadDto
                        {
                            SpecialtyId = primarySpec?.Id,
                            SpecialtyName = primarySpec?.Name,
                            DoctorId = doc.DoctorId,
                            DoctorName = doc.DisplayName,
                            AcademicTitle = doc.AcademicTitle
                        }
                    });
                }
                else if (matches.Count > 1)
                {
                    userMessage = $"{userMessage}\n\nHệ thống tìm thấy {matches.Count} bác sĩ phù hợp với tên \"{candidateName}\". Bạn có thể chọn bác sĩ bên dưới:";
                    response.Message = userMessage;

                    int insertIdx = 0;
                    foreach (var doc in matches.Take(3))
                    {
                        var primarySpec = doc.Specialties.FirstOrDefault();
                        response.Actions.Insert(insertIdx++, new AiActionDto
                        {
                            Id = $"act-select-doc-{doc.DoctorId}",
                            Type = AiActionTypes.SelectDoctor,
                            Label = $"Chọn {doc.DisplayName}",
                            Description = primarySpec != null ? $"Khoa {primarySpec.Name}" : null,
                            Style = "secondary",
                            RequiresAuthentication = false,
                            RequiresConfirmation = false,
                            Payload = new AiActionPayloadDto
                            {
                                SpecialtyId = primarySpec?.Id,
                                SpecialtyName = primarySpec?.Name,
                                DoctorId = doc.DoctorId,
                                DoctorName = doc.DisplayName,
                                AcademicTitle = doc.AcademicTitle
                            }
                        });
                    }
                }
                else if (normCandidate.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 2 || normCandidate.Length >= 4)
                {
                    userMessage = $"{userMessage}\n\nNgoài ra, hệ thống không tìm thấy bác sĩ nào có tên \"{candidateName}\" tại phòng khám ClinicCare. Bạn có thể xem danh sách bác sĩ của phòng khám hoặc chọn chuyên khoa bên dưới.";
                    response.Message = userMessage;
                }
            }
        }

        return WithDraftVersionSync(response, nextDraftVersion);
    }

    private async Task<AiActionDto> CreateContactReceptionActionAsync(CancellationToken cancellationToken)
    {
        var facility = await _dbContext.Facilities
            .AsNoTracking()
            .Where(f => f.IsActive)
            .OrderBy(f => f.Id)
            .FirstOrDefaultAsync(cancellationToken);

        string? phone = !string.IsNullOrWhiteSpace(facility?.Phone) ? facility.Phone.Trim() : null;
        string? address = !string.IsNullOrWhiteSpace(facility?.Address)
            ? (!string.IsNullOrWhiteSpace(facility.City) ? $"{facility.Address.Trim()}, {facility.City.Trim()}" : facility.Address.Trim())
            : null;

        return new AiActionDto
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
                PhoneNumber = phone,
                Address = address,
                FacilityName = facility?.Name,
                Reason = phone != null
                    ? $"Bàn tiếp đón tại: {address ?? "Phòng khám"}. Giờ làm việc: Thứ Hai - Thứ Bảy (07:30 - 17:00)."
                    : "Thông tin liên hệ lễ tân chưa được cấu hình trong hệ thống."
            }
        };
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

    public enum MessageIntentCategory
    {
        GreetingOrPleasantry,
        FacilityOrReceptionInquiry,
        NavigationInquiry,
        BookingActionOrSelection,
        ClinicalSymptomOrReason
    }

    public static MessageIntentCategory ClassifyIntent(string? message)
    {
        var classifier = new VietnameseIntentClassifier();
        var res = classifier.Classify(message);
        return res.Intent switch
        {
            AiChatIntentTypes.Greeting => MessageIntentCategory.GreetingOrPleasantry,
            AiChatIntentTypes.FacilityInquiry => MessageIntentCategory.FacilityOrReceptionInquiry,
            AiChatIntentTypes.PricingInquiry => MessageIntentCategory.NavigationInquiry,
            AiChatIntentTypes.ViewAppointments => MessageIntentCategory.NavigationInquiry,
            AiChatIntentTypes.ProvideReason => MessageIntentCategory.ClinicalSymptomOrReason,
            AiChatIntentTypes.UnclearOrOutOfScope => MessageIntentCategory.NavigationInquiry,
            _ => MessageIntentCategory.BookingActionOrSelection
        };
    }

    private static bool ContainsSymptomKeywords(string lower)
    {
        return VietnameseIntentClassifier.IsPlausibleClinicalReason(lower);
    }

    private static bool IsBookingActionPhrase(string message)
    {
        var classifier = new VietnameseIntentClassifier();
        var res = classifier.Classify(message);
        return res.Intent is AiChatIntentTypes.StartBooking
            or AiChatIntentTypes.SelectDoctor
            or AiChatIntentTypes.SelectSlot
            or AiChatIntentTypes.ReviewDraft
            or AiChatIntentTypes.ConfirmBooking
            or AiChatIntentTypes.ModifyDraft
            or AiChatIntentTypes.CancelDraft
            or AiChatIntentTypes.FindEarliestAvailableSlot;
    }

    private static string? RecoverInitialReason(string cleanMessage, List<ChatMessageDto>? context, string? existingReason, string? extractedReason)
    {
        // 0. If user explicitly modifies reason: "đổi lý do thành...", "sửa lý do:..."
        var modifyMatch = Regex.Match(cleanMessage, @"(?:đổi|sửa|thay đổi)\s+lý\s+do\s*(?:thành|là|:)?\s*(.+)$", RegexOptions.IgnoreCase);
        if (modifyMatch.Success)
        {
            var newReasonCandidate = VietnameseIntentClassifier.SanitizeClinicalReason(modifyMatch.Groups[1].Value.Trim());
            if (!VietnameseIntentClassifier.IsDisallowedReason(newReasonCandidate) && VietnameseIntentClassifier.IsPlausibleClinicalReason(newReasonCandidate))
            {
                return newReasonCandidate;
            }
        }

        // 1. If an existing reason is already preserved and not disallowed
        if (!string.IsNullOrWhiteSpace(existingReason) &&
            (!VietnameseIntentClassifier.IsDisallowedReason(existingReason) || AiActionValidator.IsValidBookingReason(existingReason)))
        {
            var sanitizedExisting = VietnameseIntentClassifier.SanitizeClinicalReason(existingReason.Trim());

            // Do not merge if message is a booking modification/correction (e.g. "đổi ngày khám...", "đổi sang bác sĩ...")
            bool isModificationCommand = Regex.IsMatch(cleanMessage, @"\b(?:đổi|sửa|thay\s+đổi|hủy|chọn|xem|khung\s+giờ|bác\s+sĩ)\b", RegexOptions.IgnoreCase);

            // If current message adds clinical evidence or duration/symptoms, merge them!
            if (!isModificationCommand && !VietnameseIntentClassifier.IsDisallowedReason(cleanMessage) &&
                (VietnameseIntentClassifier.IsClinicalComplaint(cleanMessage) ||
                 Regex.IsMatch(cleanMessage, @"\b(?:bị\s+\d+|được\s+\d+|\d+\s+ngày|kéo\s+dài|kèm|thêm|sốt|đau|mệt|ngứa|chóng mặt)\b", RegexOptions.IgnoreCase)))
            {
                return VietnameseIntentClassifier.MergeReasons(sanitizedExisting, cleanMessage);
            }
            if (!string.IsNullOrWhiteSpace(extractedReason) && !VietnameseIntentClassifier.IsDisallowedReason(extractedReason) &&
                (VietnameseIntentClassifier.IsClinicalComplaint(extractedReason) ||
                 Regex.IsMatch(extractedReason, @"\b(?:bị\s+\d+|được\s+\d+|\d+\s+ngày|kéo\s+dài|kèm|thêm|sốt|đau|mệt|ngứa|chóng mặt)\b", RegexOptions.IgnoreCase)))
            {
                return VietnameseIntentClassifier.MergeReasons(sanitizedExisting, extractedReason);
            }
            return sanitizedExisting;
        }

        // 2. If extractedReason from model is clinically plausible
        if (!string.IsNullOrWhiteSpace(extractedReason) && VietnameseIntentClassifier.IsPlausibleClinicalReason(extractedReason))
        {
            return VietnameseIntentClassifier.SanitizeClinicalReason(extractedReason.Trim());
        }

        // 3. If current message is clinically plausible and not disallowed
        if (!VietnameseIntentClassifier.IsDisallowedReason(cleanMessage) && VietnameseIntentClassifier.IsPlausibleClinicalReason(cleanMessage))
        {
            return VietnameseIntentClassifier.SanitizeClinicalReason(cleanMessage.Trim());
        }

        // 4. Look back in user context for prior clinical complaint
        if (context != null && context.Any())
        {
            var priorComplaint = context
                .Where(c => c.Role == "user" && !VietnameseIntentClassifier.IsDisallowedReason(c.Content) && VietnameseIntentClassifier.IsPlausibleClinicalReason(c.Content))
                .Select(c => VietnameseIntentClassifier.SanitizeClinicalReason(c.Content.Trim()))
                .LastOrDefault();

            if (!string.IsNullOrWhiteSpace(priorComplaint))
            {
                return priorComplaint;
            }
        }

        return null;
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

    private sealed class DoctorLookupItem
    {
        public long DoctorId { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string? AcademicTitle { get; set; }
        public string DisplayName => string.IsNullOrWhiteSpace(AcademicTitle) ? FullName : $"{AcademicTitle} {FullName}";
        public List<WhitelistItemDto> Specialties { get; set; } = new();
    }

    public static string NormalizeVietnamese(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var trimmed = text.Trim();
        var prefixRegex = new Regex(@"^(bác sĩ|bac si|bs\.cki|bs\.ckii|bs\.|ths\.bs|ts\.bs|pgs\.ts|gs\.ts)\s+", RegexOptions.IgnoreCase);
        trimmed = prefixRegex.Replace(trimmed, "").Trim();

        var normalized = trimmed.Normalize(NormalizationForm.FormD);
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

    private async Task<List<DoctorLookupItem>> QueryActiveDoctorsAsync(CancellationToken cancellationToken)
    {
        return await (from d in _dbContext.Doctors
                      join u in _dbContext.Users on d.UserId equals u.Id
                      where d.IsActive && u.IsActive
                      select new DoctorLookupItem
                      {
                          DoctorId = d.Id,
                          FullName = u.FullName,
                          AcademicTitle = d.AcademicTitle,
                          Specialties = (from ds in _dbContext.DoctorSpecialties
                                         join s in _dbContext.Specialties on ds.SpecialtyId equals s.Id
                                         where ds.DoctorId == d.Id && s.IsActive && s.AiEnabled
                                         select new WhitelistItemDto
                                         {
                                             Id = s.Id,
                                             Code = s.SpecialtyCode,
                                             Name = s.Name
                                         }).ToList()
                      }).AsNoTracking().OrderBy(d => d.DoctorId).ToListAsync(cancellationToken);
    }

    private static List<DoctorLookupItem> MatchDoctors(string searchName, List<DoctorLookupItem> doctors)
    {
        if (string.IsNullOrWhiteSpace(searchName)) return new List<DoctorLookupItem>();
        var normSearch = NormalizeVietnamese(searchName);
        if (string.IsNullOrWhiteSpace(normSearch) || normSearch.Length < 2) return new List<DoctorLookupItem>();

        // 1. Exact match on normalized full name
        var exactMatches = doctors.Where(d => NormalizeVietnamese(d.FullName) == normSearch).ToList();
        if (exactMatches.Count > 0) return exactMatches;

        // 2. Exact match on last token / given name (e.g. "Khải" matches "Nguyễn Minh Khải", "Hà" matches "Trần Thu Hà")
        var tokenMatches = doctors.Where(d =>
        {
            var tokens = NormalizeVietnamese(d.FullName).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return tokens.Length > 0 && tokens.Last() == normSearch;
        }).ToList();
        if (tokenMatches.Count > 0) return tokenMatches;

        // 3. Substring match
        var substringMatches = doctors.Where(d =>
        {
            var normDoc = NormalizeVietnamese(d.FullName);
            return normDoc.Contains(normSearch) || normSearch.Contains(normDoc);
        }).ToList();

        return substringMatches;
    }
}
