using System.Text.Json;
using System.Text.RegularExpressions;
using ClinicManagement.Application.AI.Conversation;
using ClinicManagement.Application.AI.Tools;
using ClinicManagement.Application.Mpi.DTOs;
using ClinicManagement.Application.Mpi.Interfaces;
using ClinicManagement.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.AI.Tools;

internal sealed record CopilotNextPatientData(object[] Items, string? NavigationRoute);

public sealed partial class RoleCopilotToolHandler
{
    private readonly IMpiPatientService? _patients;

    private async Task<AiToolExecutionResult> GetTechnicianCompletedTodayAsync(AiToolExecutionContext context, CancellationToken ct)
    {
        var facilities = await ResolveFacilityScopeAsync(context, nameof(AiActorRole.DiagnosticTechnician), ct);
        if (facilities.Count == 0) return ScopeDenied();
        var start = _clock.ConvertVietnamToUtc(_clock.VietnamToday.ToDateTime(TimeOnly.MinValue));
        var end = _clock.ConvertVietnamToUtc(_clock.VietnamToday.AddDays(1).ToDateTime(TimeOnly.MinValue));
        var query = Diagnostics.DiagnosticWorkflowService.WhereTechnicianFacilityScope(_db.DiagnosticOrders.AsNoTracking(), facilities.ToList())
            .Where(o => o.Status == DiagnosticOrderStatus.Completed && o.CompletedAtUtc >= start && o.CompletedAtUtc < end);
        var count = await query.CountAsync(ct);
        var items = await query.OrderByDescending(o => o.CompletedAtUtc).ThenByDescending(o => o.Id).Take(100)
            .Select(o => new { o.OrderCode, status = o.Status.ToString(), o.CompletedAtUtc,
                services = o.Items.OrderBy(i => i.Id).Select(i => i.DiagnosticService.Name).ToList() }).ToListAsync(ct);
        return Completed(items, "technician_completed_today", $"Có {count} chỉ định hoàn tất hôm nay trong phạm vi cơ sở của màn hình cận lâm sàng." + LimitNotice(count, items.Count));
    }

    private async Task<AiToolExecutionResult> SearchReceptionPatientsAsync(AiToolExecutionContext context, string json, CancellationToken ct)
    {
        if (!context.Roles.Contains(AiActorRole.Receptionist) || context.ActorId != _currentUser.UserId) return ScopeDenied();
        var facilities = await ResolveFacilityScopeAsync(context, nameof(AiActorRole.Receptionist), ct);
        if (facilities.Count == 0) return ScopeDenied();
        if (_patients is null) return DataUnavailable();
        using var document = JsonDocument.Parse(json);
        var query = document.RootElement.GetProperty("query").GetString()!.Trim();
        query = Regex.Replace(query, @"^(?:(?:theo|ten|so dien thoai|sdt|ma benh an|ma ho so|mrn)\s+)+", "", RegexOptions.IgnoreCase);
        var key = AiTextNormalizer.NormalizeForComparison(query);
        if (string.IsNullOrWhiteSpace(key)) return AiToolExecutionResult.Failed("INVALID_TOOL_ARGUMENTS", "Vui lòng nhập tên, số điện thoại hoặc mã bệnh án cần tìm.");
        var compact = Regex.Replace(key, @"[\s.-]", "");
        var scopedIds = (await _db.Patients.AsNoTracking().Where(p =>
            p.PrimaryFacilityId.HasValue && facilities.Contains(p.PrimaryFacilityId.Value) ||
            _db.Appointments.Any(a => a.PatientId == p.Id && a.FacilityId.HasValue && facilities.Contains(a.FacilityId.Value)) ||
            _db.PatientVisits.Any(v => v.PatientId == p.Id && facilities.Contains(v.FacilityId)))
            .Select(p => p.Id).ToListAsync(ct)).ToHashSet();
        var matches = new List<MpiPatientDto>();
        var pageNumber = 1;
        while (matches.Count < 5)
        {
            var page = await _patients.SearchPatientsAsync(new PatientSearchQuery { Page = pageNumber, PageSize = 100 }, ct);
            matches.AddRange(page.Items.Where(p => scopedIds.Contains(p.Id) &&
                (AiTextNormalizer.NormalizeForComparison(p.FullName).Contains(key, StringComparison.Ordinal) ||
                 AiTextNormalizer.NormalizeForComparison(p.MedicalRecordNumber).Replace("-", "").Contains(compact, StringComparison.Ordinal) ||
                 Regex.IsMatch(compact, @"^\+?\d+$") && Regex.Replace(p.PhoneNumber ?? "", @"\D", "").Contains(compact, StringComparison.Ordinal)))
                .Take(5 - matches.Count));
            if (pageNumber * 100 >= page.TotalItems || !page.Items.Any()) break;
            pageNumber++;
        }
        var rows = new List<object>();
        var lines = new List<string>();
        foreach (var patient in matches)
        {
            var next = await _db.Appointments.AsNoTracking().Where(a => a.PatientId == patient.Id && a.FacilityId.HasValue && facilities.Contains(a.FacilityId.Value) &&
                    AppointmentStatusExtensions.HoldingSlotStatuses.Contains(a.Status) &&
                    (a.AppointmentDate > _clock.VietnamToday || a.AppointmentDate == _clock.VietnamToday && a.StartTime >= _clock.VietnamTime))
                .OrderBy(a => a.AppointmentDate).ThenBy(a => a.StartTime).ThenBy(a => a.Id)
                .Select(a => new { a.AppointmentCode, a.AppointmentDate, a.StartTime }).FirstOrDefaultAsync(ct);
            var phone = MaskPatientPhone(patient.PhoneNumber);
            rows.Add(new { patient.FullName, patient.MedicalRecordNumber, birthYear = patient.DateOfBirth?.Year, phone,
                upcomingAppointment = next });
            lines.Add($"{patient.FullName} — {patient.MedicalRecordNumber}, năm sinh {patient.DateOfBirth?.Year.ToString() ?? "chưa có"}, SĐT {phone}" +
                (next is null ? ", chưa có lịch hẹn sắp tới." : $", lịch hẹn {next.AppointmentCode} ngày {next.AppointmentDate:dd/MM/yyyy} lúc {next.StartTime:HH:mm}."));
        }
        return Completed(rows, "reception_patient_lookup", matches.Count == 0 ? "Không tìm thấy bệnh nhân phù hợp trong phạm vi cơ sở." : string.Join("\n", lines));
    }

    private static string MaskPatientPhone(string? value)
    {
        var digits = Regex.Replace(value ?? "", @"\D", "");
        return digits.Length > 6 ? digits[..4] + "***" + digits[^3..] : digits.Length > 0 ? "***" : "Chưa có";
    }

    private async Task<AiToolExecutionResult> GetNextDoctorPatientAsync(AiToolExecutionContext context, CancellationToken ct)
    {
        if (!context.Roles.Contains(AiActorRole.Doctor) || context.ActorId != _currentUser.UserId) return ScopeDenied();
        var facilities = await ResolveFacilityScopeAsync(context, nameof(AiActorRole.Doctor), ct);
        if (facilities.Count == 0) return ScopeDenied();
        if (_doctorAppointments is null) return DataUnavailable();
        var next = (await _doctorAppointments.GetDoctorDashboardAsync(_clock.VietnamToday)).NextPatient;
        if (next is null) return Completed(new CopilotNextPatientData([], null), "doctor_next_patient", "Hiện chưa có bệnh nhân tiếp theo trong hàng đợi của bạn.");
        var allowed = next.PatientVisitId.HasValue
            ? await _db.PatientVisits.AnyAsync(v => v.Id == next.PatientVisitId && facilities.Contains(v.FacilityId), ct)
            : await _db.Appointments.AnyAsync(a => a.Id == next.AppointmentId && a.FacilityId.HasValue && facilities.Contains(a.FacilityId.Value), ct);
        if (!allowed) return ScopeDenied();
        var route = next.PatientVisitId.HasValue ? $"/doctor/visits/{next.PatientVisitId}/examination" : $"/doctor/appointments/{next.AppointmentId}/examination";
        var code = string.IsNullOrWhiteSpace(next.VisitCode) ? next.AppointmentCode : next.VisitCode;
        var reason = next.ChiefComplaint ?? next.Reason ?? "Chưa ghi nhận lý do khám";
        var row = new { next.PatientName, visitCode = code, next.AppointmentCode, next.AppointmentId, next.PatientVisitId,
            startTime = next.StartTime.ToString("HH:mm"), reason, status = CopilotDisplayText.DoctorStatus(next.Status) };
        return Completed(new CopilotNextPatientData([row], route), "doctor_next_patient",
            $"Bệnh nhân tiếp theo: {next.PatientName}, mã lượt {code}, giờ {next.StartTime:HH:mm}. Lý do khám: {reason}.");
    }
}
