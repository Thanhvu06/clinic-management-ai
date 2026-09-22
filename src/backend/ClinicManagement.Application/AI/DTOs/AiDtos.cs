using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ClinicManagement.Application.AI.DTOs;

public class AiSuggestionRequestDto
{
    [Required]
    [MinLength(10)]
    [MaxLength(1000)]
    public string SymptomDescription { get; set; } = string.Empty;
}

public class AiSuggestionResponseDto
{
    public string Outcome { get; set; } = string.Empty;
    public string Disclaimer { get; set; } = "Gợi ý chỉ mang tính tham khảo, không thay thế chẩn đoán của bác sĩ.";
    public List<AiSpecialtySuggestionDto> Suggestions { get; set; } = new();
}

public class AiSpecialtySuggestionDto
{
    public long SpecialtyId { get; set; }
    public string SpecialtyCode { get; set; } = string.Empty;
    public string SpecialtyName { get; set; } = string.Empty;
    public int Rank { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public class AiProviderSuggestionResult
{
    public string SpecialtyCode { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

public class WhitelistItemDto
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public class ChatMessageDto
{
    [Required]
    [RegularExpression("^(user|model)$", ErrorMessage = "Role chỉ được là user hoặc model")]
    public string Role { get; set; } = string.Empty; // "user" or "model"

    [Required]
    [MaxLength(500, ErrorMessage = "Nội dung tin nhắn vượt quá 500 ký tự.")]
    public string Content { get; set; } = string.Empty;
}

public static class AiActionTypes
{
    public const string ViewSpecialty = "ViewSpecialty";
    public const string ViewDoctors = "ViewDoctors";
    public const string ViewAvailableSlots = "ViewAvailableSlots";
    public const string StartBooking = "StartBooking";
    public const string SelectDoctor = "SelectDoctor";
    public const string SelectSlot = "SelectSlot";
    public const string ReviewBooking = "ReviewBooking";
    public const string ConfirmBooking = "ConfirmBooking";
    public const string ChangePreferredDate = "ChangePreferredDate";
    public const string ViewMyAppointments = "ViewMyAppointments";
    public const string OpenAppointmentDetail = "OpenAppointmentDetail";
    public const string RequestReschedule = "RequestReschedule";
    public const string RequestCancellation = "RequestCancellation";
    public const string ViewDiagnosticResults = "ViewDiagnosticResults";
    public const string ViewPrescriptions = "ViewPrescriptions";
    public const string ViewBills = "ViewBills";
    public const string ContactReception = "ContactReception";
    public const string ManualSpecialtySelection = "ManualSpecialtySelection";
    public const string CallEmergency = "CallEmergency";

    private static readonly HashSet<string> AllAllowed = new(StringComparer.OrdinalIgnoreCase)
    {
        ViewSpecialty, ViewDoctors, ViewAvailableSlots, StartBooking,
        SelectDoctor, SelectSlot, ReviewBooking, ConfirmBooking, ChangePreferredDate,
        ViewMyAppointments, OpenAppointmentDetail, RequestReschedule, RequestCancellation,
        ViewDiagnosticResults, ViewPrescriptions, ViewBills, ContactReception,
        ManualSpecialtySelection, CallEmergency
    };

    private static readonly HashSet<string> BookingActions = new(StringComparer.OrdinalIgnoreCase)
    {
        SelectDoctor, SelectSlot, ReviewBooking, ConfirmBooking, ChangePreferredDate
    };

    public static IReadOnlyCollection<string> All => AllAllowed;

    public static bool IsAllowed(string? actionType)
    {
        return !string.IsNullOrWhiteSpace(actionType) && AllAllowed.Contains(actionType);
    }

    public static bool IsBookingAction(string? actionType)
    {
        return !string.IsNullOrWhiteSpace(actionType) && BookingActions.Contains(actionType);
    }
}

/// <summary>
/// Conversational and operational intents recognized across frontend, backend, LLM, and intent classifier.
/// </summary>
public static class AiChatIntentTypes
{
    // 14 Canonical Conversational Intents
    public const string Greeting = "Greeting";
    public const string FacilityInquiry = "FacilityInquiry";
    public const string PricingInquiry = "PricingInquiry";
    public const string DoctorSearch = "DoctorSearch";
    public const string StartBooking = "StartBooking";
    public const string SelectDoctor = "SelectDoctor";
    public const string SelectSlot = "SelectSlot";
    public const string ProvideReason = "ProvideReason";
    public const string ReviewDraft = "ReviewDraft";
    public const string ConfirmBooking = "ConfirmBooking";
    public const string ModifyDraft = "ModifyDraft";
    public const string CancelDraft = "CancelDraft";
    public const string ViewAppointments = "ViewAppointments";
    public const string UnclearOrOutOfScope = "UnclearOrOutOfScope";

    // Operational Intents
    public const string FindEarliestAvailableSlot = "FindEarliestAvailableSlot";

    private static readonly HashSet<string> AllAllowed = new(StringComparer.OrdinalIgnoreCase)
    {
        Greeting,
        FacilityInquiry,
        PricingInquiry,
        DoctorSearch,
        StartBooking,
        SelectDoctor,
        SelectSlot,
        ProvideReason,
        ReviewDraft,
        ConfirmBooking,
        ModifyDraft,
        CancelDraft,
        ViewAppointments,
        UnclearOrOutOfScope,
        FindEarliestAvailableSlot
    };

    public static IReadOnlyCollection<string> All => AllAllowed;

    public static bool IsAllowed(string? intent)
    {
        return !string.IsNullOrWhiteSpace(intent) && AllAllowed.Contains(intent);
    }
}

public static class SafeRoutes
{
    public const string Invoices = "/patient/invoices";
    public const string Appointments = "/patient/appointments";
    public const string Prescriptions = "/patient/prescriptions";
    public const string DiagnosticResults = "/patient/diagnostic-results";
    public const string BookAppointment = "/patient/book";
    public const string Doctors = "/doctors";
    public const string Specialties = "/specialties";
    public const string EmergencyPhone = "tel:115";

    public static readonly HashSet<string> AllowedPrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        Invoices,
        Appointments,
        Prescriptions,
        DiagnosticResults,
        BookAppointment,
        Doctors,
        Specialties,
        EmergencyPhone
    };

    public static bool IsSafeRoute(string? route)
    {
        if (string.IsNullOrWhiteSpace(route)) return false;
        var clean = route.Trim();
        if (clean == EmergencyPhone) return true;
        if (!clean.StartsWith('/')) return false;

        var path = clean.Split('?')[0].Split('#')[0];
        return AllowedPrefixes.Contains(path) ||
               (path.StartsWith("/specialties/") && long.TryParse(path["/specialties/".Length..], out _)) ||
               (path.StartsWith("/doctors/") && long.TryParse(path["/doctors/".Length..], out _)) ||
               (path.StartsWith("/patient/appointments/") && long.TryParse(path["/patient/appointments/".Length..], out _));
    }
}

public static class AiActionValidator
{
    public static bool IsValidBookingReason(string? reason)
    {
        var normalized = reason?.Trim();
        return normalized is { Length: >= 10 and <= 500 };
    }

    private static readonly HashSet<string> AuthRequiredActions = new(StringComparer.OrdinalIgnoreCase)
    {
        AiActionTypes.ConfirmBooking,
        AiActionTypes.ReviewBooking,
        AiActionTypes.ViewMyAppointments,
        AiActionTypes.OpenAppointmentDetail,
        AiActionTypes.RequestReschedule,
        AiActionTypes.RequestCancellation,
        AiActionTypes.ViewDiagnosticResults,
        AiActionTypes.ViewPrescriptions,
        AiActionTypes.ViewBills
    };

    public static bool Validate(AiActionDto? action, out string? error)
    {
        error = null;
        if (action == null)
        {
            error = "Action cannot be null.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(action.Type) || !AiActionTypes.IsAllowed(action.Type))
        {
            error = $"Action type '{action.Type}' is not in allowlist.";
            return false;
        }

        if (action.Payload == null)
        {
            error = "Action payload cannot be null.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(action.Payload.TargetUrl) && !SafeRoutes.IsSafeRoute(action.Payload.TargetUrl))
        {
            error = $"TargetUrl '{action.Payload.TargetUrl}' is not an allowlisted safe route.";
            return false;
        }

        if (AuthRequiredActions.Contains(action.Type) && !action.RequiresAuthentication)
        {
            error = $"Action type '{action.Type}' requires authentication.";
            return false;
        }

        if (action.DraftVersion.HasValue && action.DraftVersion.Value < 1)
        {
            error = "Action DraftVersion must be a positive integer >= 1.";
            return false;
        }

        if (action.Payload.DraftVersion.HasValue && action.Payload.DraftVersion.Value < 1)
        {
            error = "Payload DraftVersion must be a positive integer >= 1.";
            return false;
        }

        // Per-action specific payload validation
        switch (action.Type)
        {
            case AiActionTypes.SelectDoctor:
                if (!action.Payload.SpecialtyId.HasValue || action.Payload.SpecialtyId.Value <= 0)
                {
                    error = "SelectDoctor action requires a valid SpecialtyId.";
                    return false;
                }
                if (!action.Payload.DoctorId.HasValue || action.Payload.DoctorId.Value <= 0)
                {
                    error = "SelectDoctor action requires a valid DoctorId.";
                    return false;
                }
                break;

            case AiActionTypes.SelectSlot:
                if (!action.Payload.DoctorId.HasValue || action.Payload.DoctorId.Value <= 0)
                {
                    error = "SelectSlot action requires a valid DoctorId.";
                    return false;
                }
                if (!action.Payload.SlotId.HasValue || action.Payload.SlotId.Value <= 0)
                {
                    error = "SelectSlot action requires a valid SlotId.";
                    return false;
                }
                if (string.IsNullOrWhiteSpace(action.Payload.SlotDate) ||
                    string.IsNullOrWhiteSpace(action.Payload.StartTime) ||
                    string.IsNullOrWhiteSpace(action.Payload.EndTime))
                {
                    error = "SelectSlot action requires SlotDate, StartTime, and EndTime.";
                    return false;
                }
                break;

            case AiActionTypes.ConfirmBooking:
                if (!action.RequiresConfirmation)
                {
                    error = "ConfirmBooking action MUST require confirmation.";
                    return false;
                }
                if (!action.Payload.SpecialtyId.HasValue || action.Payload.SpecialtyId.Value <= 0 ||
                    !action.Payload.DoctorId.HasValue || action.Payload.DoctorId.Value <= 0 ||
                    !action.Payload.SlotId.HasValue || action.Payload.SlotId.Value <= 0 ||
                    string.IsNullOrWhiteSpace(action.Payload.SlotDate) ||
                    string.IsNullOrWhiteSpace(action.Payload.StartTime) ||
                    string.IsNullOrWhiteSpace(action.Payload.EndTime) ||
                    !IsValidBookingReason(action.Payload.Reason))
                {
                    error = "ConfirmBooking action requires valid specialty, doctor, slot, date/time, and a 10-500 character reason.";
                    return false;
                }
                break;

            case AiActionTypes.ReviewBooking:
                if (!action.Payload.SpecialtyId.HasValue || action.Payload.SpecialtyId.Value <= 0 ||
                    !action.Payload.DoctorId.HasValue || action.Payload.DoctorId.Value <= 0 ||
                    !action.Payload.SlotId.HasValue || action.Payload.SlotId.Value <= 0 ||
                    string.IsNullOrWhiteSpace(action.Payload.SlotDate) ||
                    string.IsNullOrWhiteSpace(action.Payload.StartTime) ||
                    string.IsNullOrWhiteSpace(action.Payload.EndTime) ||
                    !IsValidBookingReason(action.Payload.Reason))
                {
                    error = "ReviewBooking action requires valid specialty, doctor, slot, date/time, and a 10-500 character reason.";
                    return false;
                }
                break;

            case AiActionTypes.ChangePreferredDate:
                if (string.IsNullOrWhiteSpace(action.Payload.SlotDate))
                {
                    error = "ChangePreferredDate action requires a valid SlotDate.";
                    return false;
                }
                break;

            case AiActionTypes.OpenAppointmentDetail:
                if (!action.Payload.AppointmentId.HasValue || action.Payload.AppointmentId.Value <= 0)
                {
                    error = "OpenAppointmentDetail action requires a valid AppointmentId > 0.";
                    return false;
                }
                if (!string.IsNullOrWhiteSpace(action.Payload.TargetUrl))
                {
                    var cleanUrl = action.Payload.TargetUrl.Split('?')[0].Split('#')[0];
                    if (cleanUrl.StartsWith("/patient/appointments/"))
                    {
                        var idStr = cleanUrl["/patient/appointments/".Length..];
                        if (long.TryParse(idStr, out var parsedId) && parsedId != action.Payload.AppointmentId.Value)
                        {
                            error = "TargetUrl appointment ID does not match payload AppointmentId.";
                            return false;
                        }
                    }
                }
                break;

            case AiActionTypes.RequestReschedule:
                if (!action.RequiresConfirmation)
                {
                    error = "RequestReschedule action MUST require confirmation.";
                    return false;
                }
                if (!action.Payload.AppointmentId.HasValue || action.Payload.AppointmentId.Value <= 0)
                {
                    error = "RequestReschedule action requires a valid AppointmentId > 0.";
                    return false;
                }
                break;

            case AiActionTypes.RequestCancellation:
                if (!action.RequiresConfirmation)
                {
                    error = "RequestCancellation action MUST require confirmation.";
                    return false;
                }
                if (!action.Payload.AppointmentId.HasValue || action.Payload.AppointmentId.Value <= 0)
                {
                    error = "RequestCancellation action requires a valid AppointmentId > 0.";
                    return false;
                }
                break;

            case AiActionTypes.ViewSpecialty:
                if (action.Payload.SpecialtyId.HasValue && action.Payload.SpecialtyId.Value <= 0)
                {
                    error = "ViewSpecialty SpecialtyId must be > 0.";
                    return false;
                }
                break;

            case AiActionTypes.ViewDoctors:
                if (action.Payload.SpecialtyId.HasValue && action.Payload.SpecialtyId.Value <= 0)
                {
                    error = "ViewDoctors SpecialtyId must be > 0.";
                    return false;
                }
                break;

            case AiActionTypes.ViewAvailableSlots:
                if (!action.Payload.DoctorId.HasValue || action.Payload.DoctorId.Value <= 0)
                {
                    error = "ViewAvailableSlots requires a valid DoctorId > 0.";
                    return false;
                }
                break;

            case AiActionTypes.StartBooking:
                if (action.Payload.SpecialtyId.HasValue && action.Payload.SpecialtyId.Value <= 0)
                {
                    error = "StartBooking SpecialtyId must be > 0.";
                    return false;
                }
                break;

            case AiActionTypes.CallEmergency:
                if (action.Payload.TargetUrl != SafeRoutes.EmergencyPhone)
                {
                    error = $"CallEmergency action must route to '{SafeRoutes.EmergencyPhone}'.";
                    return false;
                }
                break;
        }

        return true;
    }
}

public class AiActionPayloadDto
{
    public long? SpecialtyId { get; set; }
    public string? SpecialtyCode { get; set; }
    public string? SpecialtyName { get; set; }
    public long? DoctorId { get; set; }
    public string? DoctorName { get; set; }
    public string? AcademicTitle { get; set; }
    public long? SlotId { get; set; }
    public string? SlotDate { get; set; } // YYYY-MM-DD
    public string? StartTime { get; set; } // HH:mm
    public string? EndTime { get; set; } // HH:mm
    public long? AppointmentId { get; set; }
    public string? AppointmentCode { get; set; }
    public string? Reason { get; set; }
    public string? TargetUrl { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Address { get; set; }
    public string? FacilityName { get; set; }
    public int? DraftVersion { get; set; }
}

public class AiActionDto
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Style { get; set; } = "primary"; // primary, secondary, danger
    public bool RequiresAuthentication { get; set; }
    public bool RequiresConfirmation { get; set; }
    public int? DraftVersion { get; set; }
    public AiActionPayloadDto Payload { get; set; } = new();
}

public class AiBookingDraftDto
{
    public long? SpecialtyId { get; set; }
    public string? SpecialtyName { get; set; }
    public long? DoctorId { get; set; }
    public string? DoctorName { get; set; }
    public long? SlotId { get; set; }
    public string? SlotDate { get; set; }
    public string? StartTime { get; set; }
    public string? EndTime { get; set; }
    public string? Reason { get; set; }
    public string? RoomNumber { get; set; }
    public bool IsComplete { get; set; }
    public int Version { get; set; } = 1;
}

public class AiChatRequestDto
{
    [Required]
    [MaxLength(500, ErrorMessage = "Tin nhắn vượt quá 500 ký tự.")]
    public string Message { get; set; } = string.Empty;

    [MaxLength(10, ErrorMessage = "Lịch sử không được vượt quá 10 tin nhắn.")]
    public List<ChatMessageDto> Context { get; set; } = new();

    [MaxLength(64)]
    public string? Intent { get; set; }

    [Range(1, long.MaxValue)]
    public long? PendingSpecialtyId { get; set; }

    [Range(1, long.MaxValue)]
    public long? PendingDoctorId { get; set; }

    [Range(1, long.MaxValue)]
    public long? PendingSlotId { get; set; }

    [RegularExpression(@"^\d{4}-\d{2}-\d{2}$", ErrorMessage = "Ngày khám phải có định dạng yyyy-MM-dd.")]
    public string? PendingSlotDate { get; set; }

    [MaxLength(500)]
    public string? Reason { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Phiên bản thảo lịch không hợp lệ.")]
    public int? DraftVersion { get; set; }
}

public class AiChatResponseDto
{
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Backward-compatibility alias for Message.
    /// </summary>
    public string Reply
    {
        get => Message;
        set => Message = value;
    }

    public string Urgency { get; set; } = "ROUTINE"; // ROUTINE, SOON, EMERGENCY
    public string? SafetyNotice { get; set; }

    public List<AiSpecialtySuggestionDto> SpecialtySuggestions { get; set; } = new();

    /// <summary>
    /// Backward-compatibility alias for SpecialtySuggestions.
    /// </summary>
    public List<AiSpecialtySuggestionDto> SuggestedSpecialties
    {
        get => SpecialtySuggestions;
        set => SpecialtySuggestions = value;
    }

    public List<AiActionDto> Actions { get; set; } = new();
    public List<string> MissingFields { get; set; } = new();
    public AiBookingDraftDto? BookingDraft { get; set; }
    public string PromptVersion { get; set; } = "1.0.0";
    public bool ManualSelectionRequired { get; set; } = false;

    /// <summary>
    /// Operating mode: "Online" (Normal AI operating), "Degraded" (Basic support mode), "Offline".
    /// </summary>
    public string AssistantStatus { get; set; } = "Online";

    /// <summary>
    /// Technical provider status: "Healthy", "Disabled", "AuthFailure", "RateLimited", "Timeout", "NetworkError", "InvalidResponse", "Cancelled".
    /// </summary>
    public string ProviderStatus { get; set; } = "Healthy";

    /// <summary>
    /// Dialogue lifecycle outcome: "Success", "UnclearInput", "ClarificationRequired", "DraftModified", "DraftCancelled", "Confirmed", "NoMatchingDoctor", "NoAvailableSlots", "ProviderUnavailable".
    /// </summary>
    public string? DialogueOutcome { get; set; }

    /// <summary>
    /// Optional clarifying question if the intent was ambiguous or missing required details.
    /// </summary>
    public string? ClarificationPrompt { get; set; }

    /// <summary>
    /// Canonical primary intent identified for the user's turn.
    /// </summary>
    public string? PrimaryIntent { get; set; }
}

public class AiChatProviderResult
{
    public bool IsSuccess { get; set; } = true;
    public string Status { get; set; } = "Success"; // Success, Disabled, AuthFailure, RateLimited, Timeout, NetworkError, InvalidResponse, Cancelled
    public string? ErrorMessage { get; set; }
    public string Reply { get; set; } = string.Empty;
    public List<string> SuggestedSpecialtyCodes { get; set; } = new();
    public string Urgency { get; set; } = "ROUTINE";
    public string? PrimaryIntent { get; set; }
    public string? SecondaryIntent { get; set; }
    public bool IsClear { get; set; } = true;
    public string? ClarificationPrompt { get; set; }
    public string? ExtractedSpecialtyCode { get; set; }
    public string? ExtractedDoctorName { get; set; } // Verbatim as uttered by the user
    public string? ExtractedDate { get; set; }
    public string? ExtractedTimePreference { get; set; }
    public bool WantsEarliest { get; set; }
    public string? RequestedActionType { get; set; }
    public string? ExtractedReason { get; set; }
    public bool IsCorrection { get; set; }
    public string? NegatedDoctorName { get; set; }
    public string? NegatedSymptom { get; set; }
    public string? CorrectionTarget { get; set; }
}

public class SpecialtyClassificationResult
{
    public string SpecialtyCode { get; set; } = string.Empty;
    public float Confidence { get; set; }
    public bool IsClinicallyValidated { get; set; }
    public Dictionary<string, float> AllScores { get; set; } = new();
}
