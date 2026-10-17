using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClinicManagement.Application.AI.Conversation;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.AI.Interfaces;
using ClinicManagement.Application.AI.Suggestions;
using ClinicManagement.Application.AI.Tools;
using ClinicManagement.Application.Appointments.Interfaces;
using ClinicManagement.Application.Authentication.Interfaces;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Doctors.DTOs;
using ClinicManagement.Application.Doctors.Interfaces;
using ClinicManagement.Application.Specialties.Interfaces;
using ClinicManagement.Infrastructure.Persistence;
using ClinicManagement.Infrastructure.Appointments;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.AI;

/// <summary>Patient-only local selection flow. No provider, tool executor or appointment writer.</summary>
public sealed class AiBookingWizardService(
    ISpecialtyService specialties, IDoctorService doctors, IAppointmentAvailabilityPolicy availability,
    ICurrentUserService currentUser, IDateTimeProvider clock, AppDbContext db,
    IAiSafetyGuard safety, IAiAuditService audit, AiBookingWizardTokens tokens, AiBookingReviewIssuer review)
{
    private static readonly string[] Reasons = { "Khám tổng quát", "Tái khám theo hẹn", "Tư vấn kết quả xét nghiệm" };
    private static readonly Regex HintPrefix = new(@"^(?:(?:bac si|bs|bsi|doctor|dr|chuyen khoa|khoa|kham)\s+)+", RegexOptions.CultureInvariant);
    private const int MaxListedOptions = 12;

    public async Task<AiBookingWizardResponseDto> StepAsync(AiBookingWizardRequestDto request, CancellationToken ct = default)
    {
        var user = currentUser.UserId;
        AiBookingWizardResponseDto response;
        if (!user.HasValue) response = Error("AUTHENTICATION_REQUIRED", "Bạn cần đăng nhập để đặt lịch.");
        else if (request.Step == "start")
            response = await StartAsync(request.Hint, user.Value, request.SessionId, ct);
        else
        {
            var selection = tokens.Read(request.OptionToken, user.Value, request.SessionId, request.Step);
            if (selection is null)
                response = Error("WIZARD_TOKEN_INVALID_OR_EXPIRED", "Lựa chọn không hợp lệ hoặc đã hết hạn. Vui lòng bắt đầu lại.");
            else if (request.Step == "text")
                response = await TextAsync(selection, request.Text, user.Value, request.SessionId, ct);
            else if (request.Step == "reason" || selection.Preset > 0)
                response = await ReasonAsync(selection, request.Step == "reason" ? request.Reason : Reasons[selection.Preset - 1], user.Value, request.SessionId, ct);
            else response = await RenderAsync(selection, user.Value, request.SessionId, ct);
        }

        if (request.Step == "text")
        {
            // The shared audit writer strips unknown fields. Keep this wizard-only schema
            // local: its only input-derived metadata is a number, never text or tokens.
            db.AiAuditLogs.Add(new ClinicManagement.Domain.Entities.AiAuditLog
            {
                UserId = user, SessionId = request.SessionId, ActionType = "BookingWizardStep",
                Outcome = response.AssistantMode, ErrorCode = response.ErrorCode, TimestampUtc = clock.UtcNow,
                MetadataJson = JsonSerializer.Serialize(new
                {
                    source = "booking-wizard", operation = "text", status = response.Step,
                    textLength = request.Text?.Length ?? 0, providerWasCalled = false
                })
            });
            await db.SaveChangesAsync(ct);
        }
        else await audit.LogActionAsync(new AiAuditLogEntry
        {
            UserId = user, SessionId = request.SessionId, ActionType = "BookingWizardStep",
            Outcome = response.AssistantMode, ErrorCode = response.ErrorCode,
            MetadataJson = JsonSerializer.Serialize(new
            {
                source = "booking-wizard", operation = request.Step, status = response.Step,
                reasonLength = request.Step == "reason" ? request.Reason?.Length ?? 0 : 0,
                providerWasCalled = false
            })
        }, ct);
        return response;
    }

    // A hint only chooses where a normal wizard begins. Any miss, ambiguity or
    // failure starts on the specialty step exactly as before, without an error.
    private async Task<AiBookingWizardResponseDto> StartAsync(string? hint, Guid user, string session, CancellationToken ct)
    {
        var fresh = new BookingWizardSelection(Guid.NewGuid());
        if (!string.IsNullOrWhiteSpace(hint))
        {
            try
            {
                var prefilled = await ResolveHintAsync(hint, fresh);
                if (prefilled is not null)
                {
                    var response = await RenderAsync(prefilled, user, session, ct);
                    if (response.ErrorCode is null) return response;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Fall through to the unchanged specialty start.
            }
        }
        return await RenderAsync(fresh, user, session, ct);
    }

    private async Task<BookingWizardSelection?> ResolveHintAsync(string hint, BookingWizardSelection fresh)
    {
        var text = NormalizeHint(hint);
        var name = HintPrefix.Replace(text, string.Empty).Trim();
        if (name.Length == 0) return null;

        // Same sources and visible lists as the specialty and doctor steps.
        var aiSpecialties = (await specialties.GetSpecialtiesAsync()).Where(x => x.AiEnabled).OrderBy(x => x.Id).Take(MaxListedOptions).ToList();
        var specialtyIds = aiSpecialties.Select(x => x.Id).ToHashSet();
        var listedDoctors = (await doctors.GetAllActiveDoctorsAsync())
            .Where(x => x.SpecialtyId.HasValue && specialtyIds.Contains(x.SpecialtyId.Value))
            .GroupBy(x => x.SpecialtyId!.Value)
            .SelectMany(group => group.OrderBy(x => x.Id).Take(MaxListedOptions))
            .ToList();
        var doctorMatches = listedDoctors.Where(doctor =>
        {
            var fullName = NormalizeHint(doctor.FullName);
            return fullName.Length > 0 && (name == fullName || name == fullName.Split(' ')[^1]);
        }).ToList();
        if (doctorMatches.Count == 1)
            return fresh with { Stage = BookingWizardStage.Day, SpecialtyId = doctorMatches[0].SpecialtyId!.Value, DoctorId = doctorMatches[0].Id, AnyDoctor = false };
        if (doctorMatches.Count > 1) return null;

        var specialtyMatches = aiSpecialties.Where(specialty =>
        {
            var specialtyName = NormalizeHint(specialty.SpecialtyName);
            var core = string.Join(' ', specialtyName.Split(' ').Where(word => word != "khoa"));
            return specialtyName.Length > 0 && (name == specialtyName || (core.Length > 0 && name == core));
        }).ToList();
        return specialtyMatches.Count == 1
            ? fresh with { Stage = BookingWizardStage.Doctor, SpecialtyId = specialtyMatches[0].Id }
            : null;
    }

    private static string NormalizeHint(string? value) =>
        Regex.Replace(Regex.Replace(AiTextNormalizer.NormalizeForComparison(value), @"[^a-z0-9\s]", " "), @"\s+", " ").Trim();

    private async Task<AiBookingWizardResponseDto> RenderAsync(BookingWizardSelection state, Guid user, string session, CancellationToken ct)
    {
        List<EligibleAppointmentFacility>? facilities = null;
        if (state.Stage >= BookingWizardStage.Facility)
        {
            if (!(await EvaluateAsync(state, user, ct)).IsAvailable) return await RefreshSlotsAsync(state, user, session, ct);
            facilities = await AppointmentFacilityResolver.GetEligibleAsync(db, state.DoctorId, state.SpecialtyId, ct);
            if (facilities.Count == 0) return Error("FACILITY_NOT_AVAILABLE", "Bác sĩ không có cơ sở phù hợp còn hoạt động cho chuyên khoa này. Vui lòng chọn bác sĩ khác.");
            if (state.FacilityId.HasValue && !facilities.Any(facility => facility.Id == state.FacilityId.Value))
                return Error("FACILITY_SCOPE_DENIED", "Cơ sở đã chọn không còn phù hợp. Vui lòng bắt đầu lại đặt lịch.");
            if (state.Stage == BookingWizardStage.Facility && facilities.Count == 1)
                state = state with { Stage = BookingWizardStage.Reason, FacilityId = facilities[0].Id };
        }
        var response = new AiBookingWizardResponseDto
        {
            Step = state.Stage.ToString().ToLowerInvariant(),
            Suggestions = AiSuggestionCatalog.ForRole(AiActorRole.Patient, false)
        };
        var specialtyList = await specialties.GetSpecialtiesAsync();
        var specialty = specialtyList.FirstOrDefault(x => x.Id == state.SpecialtyId && x.AiEnabled);
        if (state.Stage != BookingWizardStage.Specialty && specialty is null)
            return Error("SPECIALTY_NOT_AVAILABLE", "Chuyên khoa không còn nhận đặt lịch qua trợ lý. Vui lòng bắt đầu lại.");
        var doctorList = state.Stage >= BookingWizardStage.Day ? await doctors.GetAllActiveDoctorsAsync() : null;
        var doctor = doctorList?.FirstOrDefault(x => x.Id == state.DoctorId && x.SpecialtyId == state.SpecialtyId);
        if (state.Stage >= BookingWizardStage.Day && state.DoctorId > 0 && doctor is null)
            return Error("DOCTOR_NOT_AVAILABLE", "Bác sĩ không còn khả dụng. Vui lòng bắt đầu lại.");
        response.Summary = state.Stage == BookingWizardStage.Specialty ? null : new AiBookingWizardSummaryDto
        {
            SpecialtyName = specialty?.SpecialtyName, DoctorName = doctor?.FullName,
            FacilityName = facilities?.FirstOrDefault(facility => facility.Id == state.FacilityId)?.Name,
            RequiresFacilitySelection = facilities?.Count > 1,
            SlotDate = state.Day > 0 ? DateOnly.FromDayNumber(state.Day).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null
        };
        switch (state.Stage)
        {
            case BookingWizardStage.Specialty:
                response.Title = "Chọn chuyên khoa";
                response.Message = "Chọn chuyên khoa bạn muốn đặt lịch khám.";
                foreach (var item in specialtyList.Where(x => x.AiEnabled).OrderBy(x => x.Id).Take(12))
                    Add(response, state with { Stage = BookingWizardStage.Doctor, SpecialtyId = item.Id }, item.SpecialtyName, user, session);
                break;
            case BookingWizardStage.Doctor:
                response.Title = "Chọn bác sĩ";
                response.Message = "Chọn bác sĩ hoặc xem lịch trống gần nhất của chuyên khoa.";
                foreach (var item in (await doctors.GetAllActiveDoctorsAsync()).Where(x => x.SpecialtyId == state.SpecialtyId).OrderBy(x => x.Id).Take(12))
                    Add(response, state with { Stage = BookingWizardStage.Day, DoctorId = item.Id, AnyDoctor = false }, item.FullName, user, session);
                Add(response, state with { Stage = BookingWizardStage.Day, DoctorId = 0, AnyDoctor = true }, "Bác sĩ nào cũng được (lịch gần nhất)", user, session);
                Back(response, state with { Stage = BookingWizardStage.Specialty, SpecialtyId = 0 }, user, session);
                break;
            case BookingWizardStage.Day:
                response.Title = "Chọn ngày khám";
                var days = (await SlotsAsync(state, user, ct)).Select(x => x.SlotDate).Distinct().OrderBy(x => x).Take(7).ToList();
                response.Message = days.Count == 0 ? "Không có lịch trống trong 14 ngày tới. Bạn có thể quay lại chọn bác sĩ khác." : "Các ngày còn lịch trống trong 14 ngày tới.";
                foreach (var day in days)
                    Add(response, state with { Stage = BookingWizardStage.Slot, Day = day.DayNumber }, day.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture), user, session);
                Back(response, state with { Stage = BookingWizardStage.Doctor, DoctorId = 0, Day = 0, SlotId = 0 }, user, session);
                break;
            case BookingWizardStage.Slot:
                response.Title = "Chọn khung giờ";
                var slots = (await SlotsAsync(state, user, ct)).Where(x => x.SlotDate.DayNumber == state.Day).Take(12).ToList();
                response.Message = slots.Count == 0 ? "Ngày này không còn lịch trống. Vui lòng quay lại chọn ngày khác." : "Chọn khung giờ còn trống.";
                foreach (var slot in slots)
                {
                    var name = doctorList?.FirstOrDefault(x => x.Id == slot.DoctorId)?.FullName;
                    Add(response, state with { Stage = BookingWizardStage.Facility, DoctorId = slot.DoctorId, SlotId = slot.SlotId, FacilityId = null },
                        $"{slot.StartTime:HH:mm} – {slot.EndTime:HH:mm}", user, session, state.DoctorId == 0 ? name : null);
                }
                Back(response, state with { Stage = BookingWizardStage.Day, Day = 0, SlotId = 0 }, user, session);
                break;
            case BookingWizardStage.Facility:
                response.Title = "Chọn cơ sở";
                response.Message = "Bác sĩ có lịch khám tại nhiều cơ sở phù hợp. Vui lòng chọn cơ sở bạn muốn đến khám.";
                foreach (var facility in facilities!)
                    Add(response, state with { Stage = BookingWizardStage.Reason, FacilityId = facility.Id }, facility.Name, user, session);
                Back(response, state with { Stage = BookingWizardStage.Slot, DoctorId = state.AnyDoctor ? 0 : state.DoctorId, SlotId = 0, FacilityId = null }, user, session);
                break;
            case BookingWizardStage.Reason:
                response.Title = "Lý do khám";
                response.Message = "Chọn lý do có sẵn hoặc nhập lý do từ 10 đến 500 ký tự; không gửi thông tin định danh.";
                response.ReasonToken = tokens.Issue(user, session, state with { Preset = 0 }, BookingWizardOperation.Reason);
                for (byte i = 0; i < Reasons.Length; i++)
                    response.Options.Add(new(tokens.Issue(user, session, state with { Preset = (byte)(i + 1) }, BookingWizardOperation.Preset), Reasons[i]));
                var check = await EvaluateAsync(state, user, ct);
                if (!check.IsAvailable) return await RefreshSlotsAsync(state, user, session, ct);
                response.Summary!.StartTime = check.StartTime?.ToString("HH:mm", CultureInfo.InvariantCulture);
                response.Summary.EndTime = check.EndTime?.ToString("HH:mm", CultureInfo.InvariantCulture);
                Back(response, facilities?.Count > 1
                    ? state with { Stage = BookingWizardStage.Facility, FacilityId = null, Preset = 0 }
                    : state with { Stage = BookingWizardStage.Slot, DoctorId = state.AnyDoctor ? 0 : state.DoctorId, SlotId = 0, FacilityId = null, Preset = 0 }, user, session);
                break;
            default: return Error("WIZARD_TOKEN_INVALID_OR_EXPIRED", "Lựa chọn không hợp lệ. Vui lòng bắt đầu lại.");
        }
        if (state.Stage <= BookingWizardStage.Facility)
            response.TextToken = tokens.Issue(user, session, state with { Preset = 0 }, BookingWizardOperation.Text);
        return response;
    }

    private async Task<AiBookingWizardResponseDto> TextAsync(BookingWizardSelection state, string? input, Guid user, string session, CancellationToken ct)
    {
        // Resolve only against the currently rendered options, including their limits and availability checks.
        var response = await RenderAsync(state, user, session, ct);
        if (response.Step != state.Stage.ToString().ToLowerInvariant() || response.TextToken is null) return response;
        var text = AiTextNormalizer.NormalizeForComparison(input);
        var message = $"Mình chưa xác định được lựa chọn \"{AiTextNormalizer.Normalize(input)}\". Bạn chọn giúp một mục bên dưới nhé.";
        AiBookingWizardOptionDto? option = null;
        if (string.IsNullOrWhiteSpace(input) || input.Length > 200)
            message = "Vui lòng nhập lựa chọn từ 1 đến 200 ký tự hoặc chọn một mục bên dưới nhé.";
        else if (state.Stage == BookingWizardStage.Day && ParseDay(text, clock.VietnamToday) is { } day)
        {
            var label = day.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
            var remaining = string.Join(", ", response.Options.Select(item => item.Label));
            if (remaining.Length == 0) remaining = "không có";
            if (day < clock.VietnamToday)
                message = $"Ngày {label} đã qua. Các ngày còn trống: {remaining}.";
            else if (day > clock.VietnamToday.AddDays(13))
                message = "Trợ lý chỉ xem được lịch trong 14 ngày tới. Bạn chọn một ngày bên dưới hoặc đặt lịch trên trang Đặt lịch khám nhé.";
            else
            {
                option = response.Options.SingleOrDefault(item => item.Label == label);
                message = $"Ngày {label}{(response.Summary?.DoctorName is { Length: > 0 } name ? $" bác sĩ {name}" : "")} không còn lịch trống. Các ngày còn trống: {remaining}.";
            }
        }
        else if (state.Stage == BookingWizardStage.Slot && ParseTime(text) is { } time)
        {
            var label = time.ToString("HH:mm", CultureInfo.InvariantCulture);
            var matches = response.Options.Where(item => item.Label.StartsWith(label + " ", StringComparison.Ordinal)).ToList();
            if (matches.Count == 1) option = matches[0];
            message = matches.Count > 1
                ? $"Có nhiều khung giờ {label}. Bạn chọn giúp một mục bên dưới nhé."
                : $"Không có khung giờ {label}. Các khung còn trống: {string.Join(", ", response.Options.Select(item => item.Label))}.";
        }
        else if (state.Stage is BookingWizardStage.Specialty or BookingWizardStage.Doctor or BookingWizardStage.Facility)
        {
            var matches = response.Options.Where(item =>
            {
                var label = NormalizeHint(item.Label);
                var candidate = NormalizeHint(input);
                if (state.Stage is BookingWizardStage.Specialty or BookingWizardStage.Doctor)
                {
                    label = HintPrefix.Replace(label, string.Empty);
                    candidate = HintPrefix.Replace(candidate, string.Empty);
                }
                if (state.Stage == BookingWizardStage.Specialty)
                    label = string.Join(' ', label.Split(' ').Where(word => word != "khoa"));
                return label.Length > 0 && ($" {candidate} ").Contains($" {label} ", StringComparison.Ordinal);
            }).ToList();
            if (matches.Count == 1) option = matches[0];
        }
        if (option is not null && tokens.Read(option.Token, user, session, "pick") is { } selection)
            return await RenderAsync(selection, user, session, ct);
        response.Message = message;
        response.AssistantMode = "Clarifying";
        return response;
    }

    private static DateOnly? ParseDay(string text, DateOnly today)
    {
        var numeric = Regex.Match(text, @"(?<![\d/\-])(\d{1,2})([/\-])(\d{1,2})(?:\2(\d{4}))?(?![\d/\-])");
        if (numeric.Success)
        {
            var day = int.Parse(numeric.Groups[1].Value, CultureInfo.InvariantCulture);
            var month = int.Parse(numeric.Groups[3].Value, CultureInfo.InvariantCulture);
            var hasYear = numeric.Groups[4].Success;
            // A yearless dash date is deliberately not one of the accepted formats.
            if (!hasYear && numeric.Groups[2].Value != "/") return null;
            var year = hasYear ? int.Parse(numeric.Groups[4].Value, CultureInfo.InvariantCulture) : today.Year;
            var date = CalendarDay(year, month, day);
            if (!hasYear && date < today) date = CalendarDay(year + 1, month, day);
            return date;
        }
        if (Regex.IsMatch(text, @"\bhom nay\b")) return today;
        if (Regex.IsMatch(text, @"\b(ngay kia|mot)\b")) return today.AddDays(2);
        if (Regex.IsMatch(text, @"\bmai\b")) return today.AddDays(1);
        var weekday = Regex.Match(text, @"\b(?:thu\s+([2-7])|chu nhat)\b(?:\s+(tuan sau))?");
        if (weekday.Success)
        {
            var target = weekday.Groups[1].Success ? int.Parse(weekday.Groups[1].Value, CultureInfo.InvariantCulture) - 1 : 0;
            if (weekday.Groups[2].Success)
            {
                var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7)).AddDays(7);
                return monday.AddDays((target + 6) % 7);
            }
            var days = (target - (int)today.DayOfWeek + 7) % 7;
            return today.AddDays(days == 0 ? 7 : days);
        }
        var dayOnly = Regex.Match(text, @"\bngay\s+(\d{1,2})\b(?![/\d-])");
        if (!dayOnly.Success) return null;
        var requested = int.Parse(dayOnly.Groups[1].Value, CultureInfo.InvariantCulture);
        if (requested < today.Day)
        {
            var nextMonth = new DateOnly(today.Year, today.Month, 1).AddMonths(1);
            return CalendarDay(nextMonth.Year, nextMonth.Month, requested);
        }
        return CalendarDay(today.Year, today.Month, requested);
    }

    private static DateOnly? CalendarDay(int year, int month, int day) =>
        year is >= 1 and <= 9999 && month is >= 1 and <= 12 && day >= 1 && day <= DateTime.DaysInMonth(year, month)
            ? new DateOnly(year, month, day) : null;

    private static TimeOnly? ParseTime(string text)
    {
        var match = Regex.Match(text, @"(?<![\d:])(\d{1,2})\s*(?:(?:h|gio)\s*(?:(\d{1,2})|(?<half>ruoi))?|:\s*(\d{2}))(?!\d)");
        if (!match.Success) return null;
        var hour = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var minute = match.Groups["half"].Success ? 30 : match.Groups[2].Success
            ? int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) : match.Groups[3].Success
                ? int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture) : 0;
        if (hour > 23 || minute > 59) return null;
        var afternoon = Regex.IsMatch(text, @"\bchieu\b");
        var morning = Regex.IsMatch(text, @"\bsang\b");
        if ((morning && afternoon) || ((morning || afternoon) && hour is < 1 or > 12)) return null;
        if (afternoon && hour < 12) hour += 12;
        if (morning && hour == 12) hour = 0;
        return new TimeOnly(hour, minute);
    }

    private async Task<AiBookingWizardResponseDto> ReasonAsync(BookingWizardSelection state, string? reason, Guid user, string session, CancellationToken ct)
    {
        reason = reason?.Trim() ?? "";
        // Same emergency/injection guard and PII rules as the patient assistant.
        var guard = safety.Inspect(reason);
        if (guard.IsEmergency)
        {
            var emergency = Error("EMERGENCY", "Dấu hiệu có thể là tình huống cấp cứu. Hãy gọi ngay 115 hoặc đến cơ sở cấp cứu gần nhất.");
            emergency.Step = "stopped";
            emergency.AssistantMode = "SafetyBlocked";
            emergency.Actions.Add(new AiActionDto { Id = "act-emergency-115", Type = AiActionTypes.CallEmergency, Label = "Gọi cấp cứu 115", Style = "danger", Payload = new() { TargetUrl = "tel:115" } });
            return emergency;
        }
        if (guard.IsPromptInjection)
            return await ReasonErrorAsync("PROMPT_INJECTION", "Yêu cầu vượt phạm vi đã bị chặn. Vui lòng nhập nhu cầu khám.", state, user, session, ct);
        if (AiMedicalScopeGuard.IsPrescriptionRequest(reason))
            return await ReasonErrorAsync("MEDICAL_PRESCRIPTION_OUT_OF_SCOPE", "Tôi không thể kê đơn hoặc hướng dẫn liều dùng qua cuộc trò chuyện. Vui lòng nhập nhu cầu khám.", state, user, session, ct);
        if (AiPatientInputScreening.ContainsPii(reason))
            return await ReasonErrorAsync("PII_BLOCKED", "Vui lòng bỏ SĐT, Email hoặc CCCD khỏi lý do khám.", state, user, session, ct);
        if (AiPatientInputScreening.IsPromptInjection(reason))
            return await ReasonErrorAsync("PROMPT_INJECTION", "Yêu cầu vượt phạm vi đã bị chặn. Vui lòng nhập nhu cầu khám.", state, user, session, ct);
        if (!AiActionValidator.IsValidBookingReason(reason))
            return await ReasonErrorAsync("INVALID_REASON", "Lý do khám phải hợp lệ và dài từ 10 đến 500 ký tự.", state, user, session, ct);
        var slot = await EvaluateAsync(state, user, ct);
        if (!slot.IsAvailable) return await RefreshSlotsAsync(state, user, session, ct);
        var facilities = await AppointmentFacilityResolver.GetEligibleAsync(db, state.DoctorId, state.SpecialtyId, ct);
        if (facilities.Count == 0) return Error("FACILITY_NOT_AVAILABLE", "Bác sĩ không có cơ sở phù hợp còn hoạt động cho chuyên khoa này. Vui lòng chọn bác sĩ khác.");
        if (!state.FacilityId.HasValue)
        {
            if (facilities.Count > 1) return await RenderAsync(state with { Stage = BookingWizardStage.Facility }, user, session, ct);
            state = state with { FacilityId = facilities[0].Id };
        }
        var facility = facilities.FirstOrDefault(item => item.Id == state.FacilityId);
        if (facility is null) return Error("FACILITY_SCOPE_DENIED", "Cơ sở đã chọn không còn phù hợp. Vui lòng bắt đầu lại đặt lịch.");
        var draft = new AiBookingDraftDto
        {
            DraftId = $"draft_{state.DraftId:N}", SessionId = session, Version = 1,
            SpecialtyId = state.SpecialtyId, SpecialtyName = slot.SpecialtyName,
            DoctorId = state.DoctorId, DoctorName = slot.DoctorName, SlotId = state.SlotId,
            FacilityId = facility.Id, FacilityName = facility.Name,
            SlotDate = slot.SlotDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            StartTime = slot.StartTime?.ToString("HH:mm", CultureInfo.InvariantCulture),
            EndTime = slot.EndTime?.ToString("HH:mm", CultureInfo.InvariantCulture), Reason = reason, IsComplete = true
        };
        var chat = new AiChatResponseDto { BookingDraft = draft };
        if (!await review.IssueAsync(new AiChatRequestDto(), chat, draft, session, draft.DraftId, 1, null, ct, slot))
            return await ReasonErrorAsync("WIZARD_REVIEW_REJECTED", chat.Message ?? "Không thể xác nhận lựa chọn. Vui lòng bắt đầu lại.", state, user, session, ct);
        var response = new AiBookingWizardResponseDto
        {
            Step = "review", Title = "Xem lại thông tin khám", Message = chat.Message!,
            ReviewAction = chat.Actions.Single(x => x.Type == AiActionTypes.ReviewBooking),
            Summary = new() { SpecialtyName = draft.SpecialtyName, DoctorName = draft.DoctorName,
                FacilityName = facility.Name, RequiresFacilitySelection = facilities.Count > 1,
                SlotDate = draft.SlotDate, StartTime = draft.StartTime, EndTime = draft.EndTime, ReasonProvided = true }
        };
        Back(response, state with { Preset = 0 }, user, session);
        return response;
    }

    private async Task<AiBookingWizardResponseDto> ReasonErrorAsync(string code, string message, BookingWizardSelection state, Guid user, string session, CancellationToken ct)
    {
        var response = await RenderAsync(state with { Preset = 0 }, user, session, ct);
        response.ErrorCode = code; response.Message = message; response.AssistantMode = "Clarifying";
        return response;
    }

    private async Task<AiBookingWizardResponseDto> RefreshSlotsAsync(BookingWizardSelection state, Guid user, string session, CancellationToken ct)
    {
        var response = await RenderAsync(state with { Stage = BookingWizardStage.Slot, DoctorId = state.AnyDoctor ? 0 : state.DoctorId, SlotId = 0, FacilityId = null, Preset = 0 }, user, session, ct);
        response.Message = "Khung giờ này vừa không còn khả dụng. Vui lòng chọn giờ mới hoặc quay lại chọn ngày khác.";
        response.ErrorCode = "SLOT_UNAVAILABLE";
        return response;
    }

    private async Task<long?> PatientAsync(Guid user, CancellationToken ct) =>
        await db.Patients.AsNoTracking().Where(x => x.UserId == user).Select(x => (long?)x.Id).SingleOrDefaultAsync(ct);

    private async Task<SlotAvailabilityResult> EvaluateAsync(BookingWizardSelection state, Guid user, CancellationToken ct) =>
        await availability.EvaluateSlotAvailabilityAsync(new SlotAvailabilityRequest
        {
            SlotId = state.SlotId, DoctorId = state.DoctorId, SpecialtyId = state.SpecialtyId,
            PatientId = await PatientAsync(user, ct), CheckAiEnabledSpecialty = true
        }, ct);

    private async Task<List<AvailableSlotDto>> SlotsAsync(BookingWizardSelection state, Guid user, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(clock.UtcNow.AddHours(7));
        var active = (await doctors.GetAllActiveDoctorsAsync()).Where(x => x.SpecialtyId == state.SpecialtyId).Select(x => x.Id).ToHashSet();
        var slots = await availability.GetAvailableSlotsAsync(new BatchSlotAvailabilityRequest
        {
            SpecialtyId = state.SpecialtyId, DoctorId = state.DoctorId > 0 ? state.DoctorId : null,
            FromDate = today, ToDate = today.AddDays(13), PatientId = await PatientAsync(user, ct), CheckAiEnabledSpecialty = true
        }, ct);
        return slots.Where(x => active.Contains(x.DoctorId) && x.SlotDate.DayOfWeek != DayOfWeek.Sunday &&
            x.SlotDate >= today && x.SlotDate <= today.AddDays(13)).OrderBy(x => x.SlotDate).ThenBy(x => x.StartTime).ThenBy(x => x.DoctorId).ThenBy(x => x.SlotId).ToList();
    }

    private void Add(AiBookingWizardResponseDto response, BookingWizardSelection state, string label, Guid user, string session, string? hint = null) =>
        response.Options.Add(new(tokens.Issue(user, session, state, BookingWizardOperation.Pick), label, hint));
    private void Back(AiBookingWizardResponseDto response, BookingWizardSelection state, Guid user, string session)
    {
        response.CanGoBack = true;
        response.BackToken = tokens.Issue(user, session, state, BookingWizardOperation.Back);
    }
    private static AiBookingWizardResponseDto Error(string code, string message) => new()
    { Step = "error", Title = "Không thể tiếp tục", Message = message, AssistantMode = "Clarifying", ErrorCode = code };
}
