using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.AI.Interfaces;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Infrastructure.AI;

public class AiSpecialtyService : IAiSpecialtyService
{
    private readonly AppDbContext _dbContext;
    private readonly IAiSpecialtySuggestionProvider _aiProvider;
    private readonly IClinicAiContextService _clinicAiContextService;
    private readonly ILogger<AiSpecialtyService> _logger;

    public AiSpecialtyService(AppDbContext dbContext, IAiSpecialtySuggestionProvider aiProvider, IClinicAiContextService clinicAiContextService, ILogger<AiSpecialtyService> logger)
    {
        _dbContext = dbContext;
        _aiProvider = aiProvider;
        _clinicAiContextService = clinicAiContextService;
        _logger = logger;
    }

    public async Task<AiSuggestionResponseDto> GetSuggestionsAsync(AiSuggestionRequestDto request, CancellationToken cancellationToken = default)
    {
        var cleanInput = SanitizeInput(request.SymptomDescription);

        if (string.IsNullOrWhiteSpace(cleanInput) || cleanInput.Length < 10)
        {
            return new AiSuggestionResponseDto { Outcome = "INVALID_INPUT" };
        }

        // 1. Get Whitelist
        // Rule: Specialty.IsActive = true, Specialty.AiEnabled = true (if exists)
        // And has at least 1 active Doctor with active User
        var whitelistData = await (from s in _dbContext.Specialties
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

        // Post-check and Map
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

            if (validSuggestions.Count == 3) break; // Max 3
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
        var lowerMsg = request.Message.ToLowerInvariant();
        var emergencyKeywords = new[] { "đau ngực dữ dội", "khó thở nặng", "khó thở", "ngất", "lú lẫn", "đột quỵ", "co giật", "chảy máu", "nôn ra máu", "dị ứng nặng", "tự tử", "làm hại" };
        if (emergencyKeywords.Any(k => lowerMsg.Contains(k)))
        {
            return new AiChatResponseDto
            {
                Reply = "Đây có thể là tình huống khẩn cấp. Hãy gọi 115 hoặc đến cơ sở cấp cứu gần nhất ngay. Không chờ phản hồi từ chat.",
                Urgency = "EMERGENCY",
                SuggestedSpecialties = new List<AiSpecialtySuggestionDto>(),
                ManualSelectionRequired = false
            };
        }

        bool hasPhone = Regex.IsMatch(request.Message, @"(?:0|\+84)[3|5|7|8|9]\d{8}");
        bool hasEmail = Regex.IsMatch(request.Message, @"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}");
        bool hasId = Regex.IsMatch(request.Message, @"\b\d{9}\b|\b\d{12}\b");

        if (hasPhone || hasEmail || hasId)
        {
            return new AiChatResponseDto
            {
                Reply = "Hệ thống phát hiện thông tin cá nhân trong tin nhắn. Vui lòng không chia sẻ số điện thoại, email, hoặc số CCCD/CMND để bảo mật thông tin. Bạn hãy gửi lại triệu chứng mà không kèm thông tin cá nhân nhé.",
                Urgency = "ROUTINE",
                SuggestedSpecialties = new List<AiSpecialtySuggestionDto>(),
                ManualSelectionRequired = true
            };
        }

        var cleanMessage = SanitizeInput(request.Message);
        if (string.IsNullOrWhiteSpace(cleanMessage))
        {
            return new AiChatResponseDto { Reply = "Tin nhắn không hợp lệ.", ManualSelectionRequired = true };
        }

        var cleanContext = request.Context?.Select(c => new ChatMessageDto { Role = c.Role, Content = SanitizeInput(c.Content) }).ToList() ?? new List<ChatMessageDto>();

        var whitelistData = await (from s in _dbContext.Specialties
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

        var clinicContextJson = await _clinicAiContextService.GetClinicContextJsonAsync(cancellationToken);

        AiChatProviderResult aiResult;
        try
        {
            aiResult = await _aiProvider.ChatWithAiAsync(cleanMessage, cleanContext, whitelistData, clinicContextJson, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI Provider chat failed.");
            return new AiChatResponseDto { Reply = "Hệ thống AI đang quá tải. Vui lòng thử lại sau.", ManualSelectionRequired = true };
        }

        if (aiResult == null || string.IsNullOrWhiteSpace(aiResult.Reply))
        {
            return new AiChatResponseDto { Reply = "Không nhận được phản hồi từ AI.", ManualSelectionRequired = true };
        }

        var responseDto = new AiChatResponseDto
        {
            Reply = aiResult.Reply,
            Urgency = string.IsNullOrWhiteSpace(aiResult.Urgency) ? "ROUTINE" : aiResult.Urgency
        };

        if (aiResult.SuggestedSpecialtyCodes != null && aiResult.SuggestedSpecialtyCodes.Any())
        {
            var addedCodes = new HashSet<string>();
            int rank = 1;
            foreach (var code in aiResult.SuggestedSpecialtyCodes)
            {
                if (addedCodes.Contains(code)) continue;
                var match = whitelistData.FirstOrDefault(w => w.Code == code);
                if (match != null)
                {
                    responseDto.SuggestedSpecialties.Add(new AiSpecialtySuggestionDto
                    {
                        SpecialtyId = match.Id,
                        SpecialtyCode = match.Code,
                        SpecialtyName = match.Name,
                        Rank = rank++,
                        Reason = "Phù hợp theo thông tin bạn cung cấp."
                    });
                    addedCodes.Add(code);
                }
                if (responseDto.SuggestedSpecialties.Count == 3) break;
            }
        }

        if (!responseDto.SuggestedSpecialties.Any())
        {
            responseDto.ManualSelectionRequired = true;
        }

        return responseDto;
    }

    private string SanitizeInput(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var text = input.Trim();

        // Remove email addresses
        text = Regex.Replace(text, @"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b", "[EMAIL_REMOVED]");

        // Remove phone numbers (simple pattern for VN/International)
        text = Regex.Replace(text, @"\b(?:\+84|0)(?:\d[\s.-]?){8,10}\b", "[PHONE_REMOVED]");

        // Strip excessively long continuous numbers (likely IDs)
        text = Regex.Replace(text, @"\b\d{6,}\b", "[ID_REMOVED]");

        return text;
    }
}
