using System.Text.RegularExpressions;
using ClinicManagement.Application.AI.Conversation;

namespace ClinicManagement.Infrastructure.AI;

internal static class AiBookingWizardText
{
    private static readonly Regex Question = new(
        @"\b(?:o dau|gui xe|do xe|giu xe|mo cua|gio lam viec|bao nhieu|het nhiu|phi kham|gia thuoc|quy dinh|chinh sach|tai sao|vi sao|lam sao|the nao|khoa gi|chuyen khoa nao|can mang gi|thuoc gi|co (?:cho gui xe|khung gio|lich trong|nhan kham)|khi nao)\b",
        RegexOptions.CultureInvariant);

    internal static bool IsQuestion(string? input) =>
        input is not null && (PatientChatDoctorName.InformationName(input) is not null ||
            Question.IsMatch(AiTextNormalizer.NormalizeForComparison(input)));
}
