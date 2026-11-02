using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClinicManagement.Application.AI.Conversation;
using ClinicManagement.Application.AI.Tools;
using ClinicManagement.Application.Appointments.Interfaces;
using ClinicManagement.Application.Billing.DTOs;
using ClinicManagement.Application.Billing.Interfaces;
using ClinicManagement.Application.Leaves.Interfaces;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Pharmacy;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.AI.Tools;

public sealed partial class RoleCopilotToolHandler
{
    private readonly IBillingService? _billing;
    private readonly IChangeRequestService? _changeRequests;
    private readonly IAdminLeaveService? _leaves;
    private readonly MedicineBatchService? _batches;
    private static readonly IReadOnlyDictionary<string, string[]> AccuracyArguments = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["reception.search_patients"] = ["query"], ["doctor.get_next_patient"] = [],
        ["reception.get_pending_change_requests"] = [], ["reception.get_unpaid_invoices"] = [],
        ["technician.get_service_guidance"] = ["query"], ["pharmacist.get_medicine_stock"] = ["query"],
        ["pharmacist.get_medicine_batches"] = ["status"], ["admin.get_revenue_report"] = ["period"],
        ["admin.get_active_doctors"] = [], ["admin.get_pending_leave_requests"] = []
    };

    private static AiToolArgumentValidationResult? ValidateAccuracyArguments(string tool, JsonElement root)
    {
        if (!AccuracyArguments.TryGetValue(tool, out var allowed)) return null;
        foreach (var property in root.EnumerateObject())
        {
            if (IsAuthorityProperty(property.Name)) return AiToolArgumentValidationResult.Invalid("FORBIDDEN_TOOL_ARGUMENT", "Phạm vi quyền chỉ do server xác định.");
            if (!allowed.Contains(property.Name, StringComparer.Ordinal)) return AiToolArgumentValidationResult.Invalid("UNKNOWN_TOOL_ARGUMENT", "Tham số ngoài schema công cụ đọc.");
        }
        foreach (var name in allowed)
        {
            if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()) || value.GetString()!.Length > 300)
                return AiToolArgumentValidationResult.Invalid("INVALID_TOOL_ARGUMENTS", "Thiếu hoặc sai tham số công cụ đọc.");
            if (name == "status" && value.GetString() is not ("expiring" or "expired") || name == "period" && !IsAccuracyRevenuePeriod(value.GetString()!))
                return AiToolArgumentValidationResult.Invalid("INVALID_TOOL_ARGUMENTS", "Bộ lọc công cụ đọc không hợp lệ.");
        }
        return AiToolArgumentValidationResult.Valid();
    }

    private static bool IsAccuracyRevenuePeriod(string period)
    {
        if (RevenuePeriods.Contains(period)) return true;
        var days = Regex.Match(period, @"^last_(\d+)_days$");
        return days.Success && int.TryParse(days.Groups[1].Value, out var count) && count is >= 1 and <= 366;
    }

    private async Task<AiToolExecutionResult> GetTechnicianWorklistAsync(AiToolExecutionContext context, CancellationToken ct)
    {
        var facilities = await ResolveFacilityScopeAsync(context, nameof(AiActorRole.DiagnosticTechnician), ct);
        if (facilities.Count == 0) return ScopeDenied();
        var query = Diagnostics.DiagnosticWorkflowService.WhereTechnicianFacilityScope(_db.DiagnosticOrders.AsNoTracking(), facilities.ToList())
            .Where(o => o.Status == DiagnosticOrderStatus.Ordered || o.Status == DiagnosticOrderStatus.InProgress);
        var count = await query.CountAsync(ct);
        var items = await query.OrderBy(o => o.OrderedAtUtc).ThenBy(o => o.Id).Take(100).Select(o => new
        {
            o.Id, o.OrderCode, patientName = o.Patient.FullName, status = o.Status.ToString(), o.OrderedAtUtc, o.PerformingDepartmentId,
            items = o.Items.OrderBy(i => i.Id).Select(i => new { service = i.DiagnosticService.Name, status = i.Status.ToString() }).ToList()
        }).ToListAsync(ct);
        return Completed(items, "technician_worklist", $"Có {count} chỉ định đang chờ xử lý." + LimitNotice(count, items.Count));
    }

    private async Task<AiToolExecutionResult> GetPendingChangeRequestsAsync(AiToolExecutionContext context, CancellationToken ct)
    {
        if ((await ResolveFacilityScopeAsync(context, nameof(AiActorRole.Receptionist), ct)).Count == 0) return ScopeDenied();
        if (_changeRequests is null) return DataUnavailable();
        var page = await _changeRequests.GetAllChangeRequestsAsync(null, "Pending", 1, 50);
        var items = page.Items.Select(r => new { r.Id, r.AppointmentCode, r.PatientName, r.RequestType, r.Status, r.CreatedAt }).ToList();
        return Completed(new { page.TotalItems, items }, "reception_pending_change_requests", $"Có {page.TotalItems} yêu cầu đổi/hủy lịch đang chờ xử lý." + LimitNotice(page.TotalItems, items.Count) + " " +
            string.Join("; ", items.Select(r => $"{r.AppointmentCode} — {r.PatientName} ({r.RequestType}, {r.Status})")));
    }

    private async Task<AiToolExecutionResult> GetUnpaidInvoicesAsync(AiToolExecutionContext context, CancellationToken ct)
    {
        if ((await ResolveFacilityScopeAsync(context, nameof(AiActorRole.Receptionist), ct)).Count == 0) return ScopeDenied();
        if (_billing is null) return DataUnavailable();
        var page = await _billing.GetReceptionInvoicesAsync(new InvoiceFilterParams { Status = InvoiceStatus.Unpaid, Page = 1, PageSize = 50 }, ct);
        var items = page.Items.Select(i => new { i.InvoiceCode, i.PatientName, i.TotalAmount, status = i.Status.ToString() }).ToList();
        return Completed(new { page.TotalItems, items }, "reception_unpaid_invoices", $"Có {page.TotalItems} hóa đơn chưa thanh toán." + LimitNotice(page.TotalItems, items.Count) + " " +
            string.Join("; ", items.Select(i => $"{i.InvoiceCode} — {i.PatientName}: {i.TotalAmount.ToString("N0", CultureInfo.GetCultureInfo("vi-VN"))} đồng")));
    }

    private async Task<AiToolExecutionResult> GetServiceGuidanceAsync(AiToolExecutionContext context, string json, CancellationToken ct)
    {
        if ((await ResolveFacilityScopeAsync(context, nameof(AiActorRole.DiagnosticTechnician), ct)).Count == 0) return ScopeDenied();
        using var document = JsonDocument.Parse(json);
        var query = document.RootElement.GetProperty("query").GetString()!;
        var key = Regex.Replace(AiTextNormalizer.NormalizeForComparison(query), @"\b(?:cach|huong dan|quy trinh|chuan bi|lay mau|xet nghiem|benh pham|cho toi|xem|giup|voi)\b", " ");
        var terms = key.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var services = await _db.DiagnosticServices.AsNoTracking().Where(s => s.IsActive).ToListAsync(ct);
        var aliases = DiagnosticServiceAliases.Resolve(query);
        var items = services.Where(s => aliases.Count > 0 ? aliases.Contains(s.Code) : terms.Length > 0 && terms.All(term => (AiTextNormalizer.NormalizeForComparison(s.Name + " " + s.Code)).Contains(term, StringComparison.Ordinal)))
            .Take(10).Select(s => new { s.Code, s.Name, s.PreparationInstructions, s.SpecimenType }).ToList();
        var guidance = items.Where(s => !string.IsNullOrWhiteSpace(s.PreparationInstructions) || !string.IsNullOrWhiteSpace(s.SpecimenType)).ToList();
        return Completed(items, "technician_service_guidance", guidance.Count == 0
            ? "Dịch vụ chưa có hướng dẫn chuẩn bị/lấy mẫu phù hợp trong dữ liệu đã cấu hình. Vui lòng kiểm tra quy trình được cơ sở phê duyệt; tôi không tự suy diễn hướng dẫn lấy mẫu."
            : string.Join("\n", guidance.Select(s => $"{s.Code} — {s.Name}: {GuidanceSentence(s.PreparationInstructions, "Chưa cấu hình hướng dẫn chuẩn bị")} Loại bệnh phẩm: {GuidanceSentence(s.SpecimenType, "chưa cấu hình")}")));
    }

    private static string GuidanceSentence(string? value, string fallback)
    {
        var text = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim().TrimEnd(';').TrimEnd();
        return text.EndsWith('.') || text.EndsWith('!') || text.EndsWith('?') ? text : text + ".";
    }

    private async Task<AiToolExecutionResult> GetLowStockAsync(AiToolExecutionContext context, CancellationToken ct)
    {
        if ((await ResolveFacilityScopeAsync(context, nameof(AiActorRole.Pharmacist), ct)).Count == 0) return ScopeDenied();
        var query = _db.Medicines.AsNoTracking().Where(m => m.IsActive).WithUsableStock(_db, _clock.VietnamToday).Where(s => s.Quantity <= s.Medicine.ReorderLevel);
        var count = await query.CountAsync(ct);
        var items = await query.OrderBy(s => s.Quantity).ThenBy(s => s.Medicine.Id).Take(100)
            .Select(s => new { s.Medicine.Code, s.Medicine.Name, s.Medicine.Unit, StockQuantity = s.Quantity, s.Medicine.ReorderLevel }).ToListAsync(ct);
        return Completed(items, "pharmacy_low_stock", $"Có {count} thuốc có tồn khả dụng không vượt mức đặt lại." + LimitNotice(count, items.Count) +
            " Tồn kho toàn hệ thống, chưa phân tách tồn kho theo cơ sở.");
    }

    private async Task<AiToolExecutionResult> GetMedicineStockAsync(AiToolExecutionContext context, string json, CancellationToken ct)
    {
        if ((await ResolveFacilityScopeAsync(context, nameof(AiActorRole.Pharmacist), ct)).Count == 0) return ScopeDenied();
        using var document = JsonDocument.Parse(json);
        var key = AiTextNormalizer.NormalizeForComparison(document.RootElement.GetProperty("query").GetString()!);
        var medicines = await _db.Medicines.AsNoTracking().Where(m => m.IsActive).WithUsableStock(_db, _clock.VietnamToday)
            .Select(s => new { s.Medicine.Code, s.Medicine.Name, s.Medicine.Unit, AvailableStock = s.Quantity }).ToListAsync(ct);
        var items = medicines.Where(m => AiTextNormalizer.NormalizeForComparison(m.Name).Contains(key, StringComparison.Ordinal) || AiTextNormalizer.NormalizeForComparison(m.Code).Contains(key, StringComparison.Ordinal)).Take(100).ToList();
        return Completed(items, "pharmacy_medicine_stock", items.Count == 0 ? "Không tìm thấy thuốc khớp tên/mã được hỏi." :
            string.Join("; ", items.Select(m => $"{m.Code} — {m.Name}: còn {m.AvailableStock} {m.Unit} khả dụng (không tính lô hết hạn).")) + " Tồn kho toàn hệ thống, chưa phân tách theo cơ sở.");
    }

    private async Task<AiToolExecutionResult> GetMedicineBatchesAsync(AiToolExecutionContext context, string json, CancellationToken ct)
    {
        if ((await ResolveFacilityScopeAsync(context, nameof(AiActorRole.Pharmacist), ct)).Count == 0) return ScopeDenied();
        if (_batches is null) return DataUnavailable();
        using var document = JsonDocument.Parse(json);
        var status = document.RootElement.GetProperty("status").GetString()!;
        var page = await _batches.ListAsync(status, null, 1, 100);
        return Completed(new { page.TotalItems, items = page.Items }, "pharmacy_medicine_batches", $"Có {page.TotalItems} lô thuốc {(status == "expired" ? "đã hết hạn" : "sắp hết hạn trong 90 ngày")}." + LimitNotice(page.TotalItems, page.Items.Count) + " " +
            string.Join("; ", page.Items.Select(b => $"{b.BatchNumber} — {b.MedicineName}: {b.QuantityRemaining} {b.Unit}, hạn {b.ExpiryDate:dd/MM/yyyy}")));
    }

    private async Task<AiToolExecutionResult> GetRevenueReportAsync(AiToolExecutionContext context, string json, CancellationToken ct)
    {
        if (!context.Roles.Contains(AiActorRole.Admin)) return ScopeDenied();
        if (_billing is null) return DataUnavailable();
        using var document = JsonDocument.Parse(json);
        var period = document.RootElement.GetProperty("period").GetString()!;
        var today = _clock.VietnamToday;
        var first = new DateOnly(today.Year, today.Month, 1);
        var days = Regex.Match(period, @"^last_(\d+)_days$");
        var (from, to) = days.Success ? (today.AddDays(1 - int.Parse(days.Groups[1].Value)), today) : period switch
        {
            "last_7_days" => (today.AddDays(-6), today), "yesterday" => (today.AddDays(-1), today.AddDays(-1)),
            "this_month" => (first, first.AddMonths(1).AddDays(-1)), "last_month" => (first.AddMonths(-1), first.AddDays(-1)), _ => (today, today)
        };
        var report = await _billing.GetRevenueReportAsync(from, to, ct);
        return Completed(report, "admin_revenue_report", $"Doanh thu từ {from:dd/MM/yyyy} đến {to:dd/MM/yyyy}: {report.TotalRevenue.ToString("N0", CultureInfo.GetCultureInfo("vi-VN"))} đồng, {report.TotalSucceededTransactions} giao dịch thành công.");
    }

    private async Task<AiToolExecutionResult> GetActiveDoctorsAsync(AiToolExecutionContext context, CancellationToken ct)
    {
        if (!context.Roles.Contains(AiActorRole.Admin)) return ScopeDenied();
        var query = _db.Doctors.AsNoTracking().Where(d => d.IsActive);
        if (context.FacilityId.HasValue) query = query.Where(d => _db.StaffFacilityAssignments.Any(a => a.UserId == d.UserId && a.Role == "Doctor" && a.IsActive && a.FacilityId == context.FacilityId.Value));
        var count = await query.CountAsync(ct);
        var items = await (from d in query join u in _db.Users on d.UserId equals u.Id orderby u.FullName select new { d.Id, u.FullName }).Take(100).ToListAsync(ct);
        return Completed(new { totalItems = count, items }, "admin_active_doctors", $"Có {count} bác sĩ đang hoạt động." + LimitNotice(count, items.Count));
    }

    private async Task<AiToolExecutionResult> GetPendingLeaveRequestsAsync(AiToolExecutionContext context, CancellationToken ct)
    {
        if (!context.Roles.Contains(AiActorRole.Admin)) return ScopeDenied();
        if (_leaves is null) return DataUnavailable();
        var page = await _leaves.GetLeaveRequestsAsync(null, "Pending", 1, 50);
        var items = page.Items.Select(l => new { l.Id, l.DoctorName, l.StartDateTime, l.EndDateTime, l.Status }).ToList();
        return Completed(new { page.TotalItems, items }, "admin_pending_leave_requests", $"Có {page.TotalItems} đơn nghỉ phép đang chờ duyệt." + LimitNotice(page.TotalItems, items.Count) + " " +
            string.Join("; ", items.Select(l => $"#{l.Id} — {l.DoctorName}: {l.StartDateTime:dd/MM/yyyy HH:mm}–{l.EndDateTime:dd/MM/yyyy HH:mm}")));
    }

    private static AiToolExecutionResult DataUnavailable() => AiToolExecutionResult.Failed("DATA_UNAVAILABLE", "Chưa thể truy cập service dữ liệu của màn hình.");
    private static string LimitNotice(int total, int shown) => total > shown ? $" Hiển thị {shown} mục đầu tiên." : "";
}
