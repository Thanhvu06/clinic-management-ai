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

    public static bool IsAllowed(string? actionType)
    {
        return !string.IsNullOrWhiteSpace(actionType) && AllAllowed.Contains(actionType);
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
               (path.StartsWith("/doctors/") && long.TryParse(path["/doctors/".Length..], out _));
    }
}

public static class AiActionValidator
{
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

        if (action.Type == AiActionTypes.ConfirmBooking && !action.RequiresConfirmation)
        {
            error = "ConfirmBooking action MUST require confirmation.";
            return false;
        }

        if (AuthRequiredActions.Contains(action.Type) && !action.RequiresAuthentication)
        {
            error = $"Action type '{action.Type}' requires authentication.";
            return false;
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
}

public class AiChatRequestDto
{
    [Required]
    [MaxLength(500, ErrorMessage = "Tin nhắn vượt quá 500 ký tự.")]
    public string Message { get; set; } = string.Empty;

    [MaxLength(10, ErrorMessage = "Lịch sử không được vượt quá 10 tin nhắn.")]
    public List<ChatMessageDto> Context { get; set; } = new();

    public long? PendingSpecialtyId { get; set; }
    public long? PendingDoctorId { get; set; }
    public long? PendingSlotId { get; set; }
    public string? PendingSlotDate { get; set; }
    public string? Reason { get; set; }
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
}

public class AiChatProviderResult
{
    public string Reply { get; set; } = string.Empty;
    public List<string> SuggestedSpecialtyCodes { get; set; } = new();
    public string Urgency { get; set; } = "ROUTINE";
    public string? ExtractedSpecialtyCode { get; set; }
    public string? ExtractedDoctorName { get; set; }
    public string? ExtractedDate { get; set; }
    public string? ExtractedTimePreference { get; set; }
    public bool WantsEarliest { get; set; }
    public string? RequestedActionType { get; set; }
    public string? ExtractedReason { get; set; }
}

public class SpecialtyClassificationResult
{
    public string SpecialtyCode { get; set; } = string.Empty;
    public float Confidence { get; set; }
    public bool IsClinicallyValidated { get; set; }
    public Dictionary<string, float> AllScores { get; set; } = new();
}
