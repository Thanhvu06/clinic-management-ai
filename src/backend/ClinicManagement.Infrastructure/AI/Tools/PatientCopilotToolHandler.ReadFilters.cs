using System.Globalization;
using System.Text.Json;
using ClinicManagement.Application.AI.Conversation;
using ClinicManagement.Application.AI.Tools;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Diagnostics.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Policies;
using ClinicManagement.Infrastructure.Common;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.AI.Tools;

public sealed partial class PatientCopilotToolHandler
{
    private async Task<AiToolExecutionResult> GetMyVisitsAsync(AiToolInvocation invocation, AiToolExecutionContext context, CancellationToken cancellationToken)
    {
        var patientId = await ResolvePatientIdAsync(context.ActorId, cancellationToken);
        if (!patientId.HasValue) return AiToolExecutionResult.Failed("PATIENT_SCOPE_REQUIRED", "Không xác định được tài khoản bệnh nhân hiện tại.");
        var args = Parse(invocation.ArgumentsJson);
        var page = Math.Clamp(GetInt(args, "page") ?? 1, 1, 100);
        var pageSize = Math.Clamp(GetInt(args, "pageSize") ?? 20, 1, 50);
        var filters = ReadFilters(args);
        var query = _db.PatientVisits.AsNoTracking().Where(v => v.PatientId == patientId.Value);
        if (filters.FromDate is { } from) query = query.Where(v => v.VisitDate >= from);
        if (filters.ToDate is { } to) query = query.Where(v => v.VisitDate <= to);
        var total = filters.Active && !filters.Latest ? await query.CountAsync(cancellationToken) : 0;
        var items = await query.OrderByDescending(v => v.VisitDate).ThenByDescending(v => v.Id)
            .Skip(filters.Offset(page, pageSize)).Take(filters.Limit(pageSize))
            .Select(v => new
            {
                v.Id, v.VisitCode, v.VisitDate, arrivalType = v.ArrivalType.ToString(), status = v.Status.ToString(),
                v.ChiefComplaint, v.AppointmentId,
                summary = v.VisitSummary == null ? null : new
                {
                    v.VisitSummary.Summary, v.VisitSummary.Diagnosis,
                    v.VisitSummary.TreatmentPlan, v.VisitSummary.CompletedAtUtc
                }
            }).ToListAsync(cancellationToken);
        if (filters.Active) return Completed(items, "patient_visits", filters.Summary(filters.Latest ? items.Count : total, "lượt khám"));
        return Completed(items, "patient_visits", items.Count == 0 ? "Bạn chưa có lượt khám nào." : $"Có {items.Count} lượt khám của bạn trong trang này.");
    }

    private async Task<AiToolExecutionResult> GetMyDiagnosticResultsAsync(AiToolInvocation invocation, AiToolExecutionContext context, CancellationToken cancellationToken)
    {
        var patientId = await ResolvePatientIdAsync(context.ActorId, cancellationToken);
        if (!patientId.HasValue) return AiToolExecutionResult.Failed("PATIENT_SCOPE_REQUIRED", "Không xác định được tài khoản bệnh nhân hiện tại.");
        var args = Parse(invocation.ArgumentsJson);
        var page = Math.Clamp(GetInt(args, "page") ?? 1, 1, 100);
        var pageSize = Math.Clamp(GetInt(args, "pageSize") ?? 20, 1, 50);
        // Reuse the patient API projection/redaction instead of maintaining a second
        // publication rule in the copilot. This guarantees that an unpublished result
        // never reaches the card, conversation memory, audit payload or provider prompt.
        var filters = ReadFilters(args);
        var (orders, total) = filters.Active
            ? await FindDiagnosticOrdersAsync(patientId.Value, filters, page, pageSize, cancellationToken)
            : ((await _diagnostics.GetPatientOrdersAsync(page, pageSize)).Items.AsEnumerable(), 0);
        var items = orders.Select(order =>
        {
            var published = Enum.TryParse<DiagnosticOrderStatus>(order.Status, out var status) &&
                            DiagnosticResultPublicationPolicy.IsPublishedToPatient(status, order.ReviewedAtUtc);
            return new
            {
                orderCode = order.OrderCode,
                orderStatus = order.Status,
                reviewedAtUtc = order.ReviewedAtUtc,
                publishedToPatient = published,
                items = order.Items.Select(item => new
                {
                    service = item.ServiceName,
                    status = item.Status,
                    result = published && item.Result != null ? new
                    {
                        item.Result.ResultText, item.Result.Conclusion,
                        item.Result.ReferenceRange, item.Result.Unit, item.Result.ResultedAtUtc
                    } : null
                }).ToList()
            };
        }).ToList();
        var hasUnpublished = items.Any(order => !order.publishedToPatient || order.items.Any(item => item.result is null));
        if (filters.Active)
        {
            var message = filters.Summary(filters.Latest ? items.Count : total, "chỉ định cận lâm sàng");
            if (hasUnpublished) message += " Một số kết quả chi tiết chưa được công bố.";
            return Completed(items, "patient_diagnostic_results", message);
        }
        return Completed(items, "patient_diagnostic_results", items.Count == 0
            ? "Bạn chưa có chỉ định cận lâm sàng."
            : hasUnpublished
                ? "Một số chỉ định đang xử lý hoặc chưa được bác sĩ duyệt; kết quả chi tiết chưa được công bố."
                : $"Có {items.Count} chỉ định/kết quả đã được bác sĩ duyệt của bạn.");
    }

    private async Task<AiToolExecutionResult> GetMyPrescriptionsAsync(AiToolInvocation invocation, AiToolExecutionContext context, CancellationToken cancellationToken)
    {
        var patientId = await ResolvePatientIdAsync(context.ActorId, cancellationToken);
        if (!patientId.HasValue) return AiToolExecutionResult.Failed("PATIENT_SCOPE_REQUIRED", "Không xác định được tài khoản bệnh nhân hiện tại.");
        var args = Parse(invocation.ArgumentsJson);
        var page = Math.Clamp(GetInt(args, "page") ?? 1, 1, 100);
        var pageSize = Math.Clamp(GetInt(args, "pageSize") ?? 20, 1, 50);
        var filters = ReadFilters(args);
        var query = _db.Prescriptions.AsNoTracking()
            .Where(p => p.PatientId == patientId.Value && p.Status != PrescriptionStatus.Draft && p.Status != PrescriptionStatus.Cancelled);
        if (filters.UtcStart(_dateTimeProvider) is { } from) query = query.Where(p => p.CreatedAt >= from);
        if (filters.UtcEnd(_dateTimeProvider) is { } to) query = query.Where(p => p.CreatedAt < to);
        var total = filters.Active && !filters.Latest ? await query.CountAsync(cancellationToken) : 0;
        var rows = await query.OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id)
            .Skip(filters.Offset(page, pageSize)).Take(filters.Limit(pageSize))
            .Select(p => new
            {
                p.Id, status = p.Status.ToString(), p.CreatedAt, p.DispensedAt, p.Notes,
                items = p.Items.Select(i => new { medicine = i.Medicine!.Name, i.Dosage, i.Frequency, i.DurationDays, i.Instructions }).ToList()
            }).ToListAsync(cancellationToken);
        var items = rows.Select(p => new
        {
            p.Id, p.status, p.CreatedAt, p.DispensedAt, p.Notes, p.items,
            prescriptionCode = BusinessDisplay.PrescriptionCode(p.Id, p.CreatedAt, _dateTimeProvider),
            prescribedDate = DateOnly.FromDateTime(_dateTimeProvider.ConvertUtcToVietnam(p.CreatedAt))
        }).ToList();
        if (filters.Active) return Completed(items, "patient_prescriptions", filters.Summary(filters.Latest ? items.Count : total, "đơn thuốc"));
        return Completed(items, "patient_prescriptions", items.Count == 0 ? "Bạn chưa có đơn thuốc đã phát hành." : $"Có {items.Count} đơn thuốc của bạn.");
    }

    private async Task<AiToolExecutionResult> GetMyBillsAsync(AiToolInvocation invocation, AiToolExecutionContext context, CancellationToken cancellationToken)
    {
        var patientId = await ResolvePatientIdAsync(context.ActorId, cancellationToken);
        if (!patientId.HasValue) return AiToolExecutionResult.Failed("PATIENT_SCOPE_REQUIRED", "Không xác định được tài khoản bệnh nhân hiện tại.");
        var args = Parse(invocation.ArgumentsJson);
        var page = Math.Clamp(GetInt(args, "page") ?? 1, 1, 100);
        var pageSize = Math.Clamp(GetInt(args, "pageSize") ?? 20, 1, 50);
        var filters = ReadFilters(args);
        var query = _db.Invoices.AsNoTracking().Where(i => i.PatientId == patientId.Value);
        if (filters.UtcStart(_dateTimeProvider) is { } from) query = query.Where(i => i.CreatedAtUtc >= from);
        if (filters.UtcEnd(_dateTimeProvider) is { } to) query = query.Where(i => i.CreatedAtUtc < to);
        if (Enum.TryParse<InvoiceStatus>(filters.Status, out var status)) query = query.Where(i => i.Status == status);
        var total = filters.Active && !filters.Latest ? await query.CountAsync(cancellationToken) : 0;
        var items = await query.OrderByDescending(i => i.CreatedAtUtc).ThenByDescending(i => i.Id)
            .Skip(filters.Offset(page, pageSize)).Take(filters.Limit(pageSize))
            .Select(i => new
            {
                i.Id, i.InvoiceCode, status = i.Status.ToString(), i.Subtotal, i.TotalAmount,
                i.CreatedAtUtc, i.PaidAtUtc,
                items = i.Items.Where(x => !x.IsCancelled).Select(x => new { x.Description, x.Quantity, x.LineTotal }).ToList()
            }).ToListAsync(cancellationToken);
        if (filters.Active) return Completed(items, "patient_bills", filters.Summary(filters.Latest ? items.Count : total, "hóa đơn"));
        return Completed(items, "patient_bills", items.Count == 0 ? "Bạn chưa có hóa đơn nào." : $"Có {items.Count} hóa đơn của bạn.");
    }

    private static PatientReadFilters ReadFilters(Dictionary<string, JsonElement> args) =>
        new(GetDate(args, "fromDate"), GetDate(args, "toDate"), GetBool(args, "latest"),
            GetString(args, "status"), GetString(args, "service"));

    private static AiToolArgumentValidationResult? ValidatePatientReadArguments(string name, JsonElement args)
    {
        if (name is not ("patient.get_my_bills" or "patient.get_my_prescriptions" or "patient.get_my_visits" or "patient.get_my_diagnostic_results"))
            return null;

        foreach (var dateKey in new[] { "fromDate", "toDate" })
        {
            if (args.TryGetProperty(dateKey, out var value) &&
                (value.ValueKind != JsonValueKind.String || !DateOnly.TryParseExact(value.GetString(), "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) || date == DateOnly.MinValue))
                return AiToolArgumentValidationResult.Invalid("INVALID_DATE", "Ngày phải có định dạng yyyy-MM-dd.");
        }
        if (args.TryGetProperty("latest", out var latest) && latest.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            return AiToolArgumentValidationResult.Invalid("INVALID_LATEST", "Tham số latest phải là giá trị đúng hoặc sai.");
        if (args.TryGetProperty("status", out var status) &&
            (status.ValueKind != JsonValueKind.String || status.GetString() is not ("Paid" or "Unpaid")))
            return AiToolArgumentValidationResult.Invalid("INVALID_STATUS", "Trạng thái hóa đơn phải là Paid hoặc Unpaid.");
        if (args.TryGetProperty("service", out var service) &&
            (service.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(service.GetString()) || service.GetString()!.Length > 100))
            return AiToolArgumentValidationResult.Invalid("INVALID_SERVICE", "Tên dịch vụ phải từ 1 đến 100 ký tự.");
        return null;
    }

    private async Task<(IEnumerable<DiagnosticOrderDto> Orders, int Total)> FindDiagnosticOrdersAsync(
        long patientId, PatientReadFilters filters, int page, int pageSize, CancellationToken ct)
    {
        var query = _db.DiagnosticOrders.AsNoTracking().Where(o => o.PatientId == patientId);
        if (filters.UtcStart(_dateTimeProvider) is { } from) query = query.Where(o => o.OrderedAtUtc >= from);
        if (filters.UtcEnd(_dateTimeProvider) is { } to) query = query.Where(o => o.OrderedAtUtc < to);
        if (filters.Service is { } service)
        {
            var codes = DiagnosticServiceAliases.Resolve(service).ToArray();
            if (codes.Length > 0)
                query = query.Where(o => o.Items.Any(i => codes.Contains(i.DiagnosticService.Code)));
            else if (service == "sieu am")
                query = query.Where(o => o.Items.Any(i => i.DiagnosticService.Code.StartsWith("US-") ||
                    i.DiagnosticService.Name.ToLower().Contains("siêu âm")));
            else if (service is "x-quang" or "x quang")
                query = query.Where(o => o.Items.Any(i => i.DiagnosticService.Code == "IMG-CXR" ||
                    i.DiagnosticService.Name.ToLower().Replace("-", " ").Contains("x quang")));
            else
            {
                var search = AiTextNormalizer.NormalizeForComparison(service);
                var catalog = await _db.DiagnosticServices.AsNoTracking().Select(s => new { s.Id, s.Name }).ToListAsync(ct);
                var matchingIds = catalog.Where(s => AiTextNormalizer.NormalizeForComparison(s.Name).Contains(search, StringComparison.Ordinal))
                    .Select(s => s.Id).ToArray();
                query = query.Where(o => o.Items.Any(i => matchingIds.Contains(i.DiagnosticServiceId)));
            }
        }
        var total = filters.Latest ? 0 : await query.CountAsync(ct);
        var ids = await query.OrderByDescending(o => o.OrderedAtUtc).ThenByDescending(o => o.Id)
            .Skip(filters.Offset(page, pageSize)).Take(filters.Limit(pageSize)).Select(o => o.Id).ToListAsync(ct);
        var orders = new List<DiagnosticOrderDto>();
        foreach (var id in ids)
            orders.Add(await _diagnostics.GetPatientOrderByIdAsync(id));
        return (orders, total);
    }

    internal static IQueryable<Appointment> FutureAppointments(IQueryable<Appointment> appointments, IDateTimeProvider clock)
    {
        var now = clock.VietnamNow;
        var date = DateOnly.FromDateTime(now);
        var time = TimeOnly.FromDateTime(now);
        return appointments.Where(a => (a.AppointmentDate > date || (a.AppointmentDate == date && a.StartTime >= time)) &&
            (a.Status == AppointmentStatus.Pending || a.Status == AppointmentStatus.Confirmed ||
             a.Status == AppointmentStatus.PendingReschedule || a.Status == AppointmentStatus.PendingCancellation));
    }

    private async Task<AiToolExecutionResult> GetFutureAppointmentsAsync(int page, int pageSize, CancellationToken ct)
    {
        var patientId = await ResolvePatientIdAsync(_currentUser.UserId, ct);
        if (!patientId.HasValue) return AiToolExecutionResult.Failed("PATIENT_SCOPE_REQUIRED", "Không xác định được tài khoản bệnh nhân hiện tại.");
        var query = FutureAppointments(_db.Appointments.AsNoTracking().Where(a => a.PatientId == patientId.Value), _dateTimeProvider);
        var totalItems = await query.CountAsync(ct);
        var items = await query.OrderBy(a => a.AppointmentDate).ThenBy(a => a.StartTime).ThenBy(a => a.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(a => new
            {
                a.Id, a.AppointmentCode, a.AppointmentDate, a.StartTime, a.EndTime,
                status = a.Status.ToString(), doctorName = _db.Users.Where(u => u.Id == a.Doctor.UserId).Select(u => u.FullName).FirstOrDefault(),
                specialtyName = a.Specialty.Name, facilityName = a.Facility == null ? null : a.Facility.Name, a.Reason
            }).ToListAsync(ct);
        return Completed(new { items, totalItems, page, pageSize }, "appointments",
            totalItems == 0 ? "Bạn hiện chưa có lịch hẹn sắp tới." : $"Tìm thấy {totalItems} lịch hẹn sắp tới của bạn.");
    }
}
