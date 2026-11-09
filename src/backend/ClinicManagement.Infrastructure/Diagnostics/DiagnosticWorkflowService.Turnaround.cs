using ClinicManagement.Application.Common.Constants;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Diagnostics.DTOs;
using ClinicManagement.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Diagnostics;

public partial class DiagnosticWorkflowService
{
    private const int MaxTurnaroundRangeDays = 31;
    private const int MaxSlowestOrders = 20;
    private const string SegmentOrderToReceive = "OrderToReceive";
    private const string SegmentReceiveToResult = "ReceiveToResult";
    private const string SegmentResultToVerify = "ResultToVerify";
    private const string SegmentTotal = "Total";

    private sealed class TurnaroundOrderRow
    {
        public long Id { get; init; }
        public string OrderCode { get; init; } = string.Empty;
        public Guid? PatientUserId { get; init; }
        public string? PatientFullName { get; init; }
        public DateTime OrderedAtUtc { get; init; }
        public DateTime? CompletedAtUtc { get; init; }
        public DiagnosticVerificationStatus VerificationStatus { get; init; }
        public DateTime? SubmittedForVerificationAtUtc { get; init; }
        public DateTime? VerifiedAtUtc { get; init; }
        public List<TurnaroundItemRow> Items { get; init; } = new();
    }

    private sealed class TurnaroundItemRow
    {
        public long ServiceId { get; init; }
        public string ServiceCode { get; init; } = string.Empty;
        public string ServiceName { get; init; } = string.Empty;
        public DiagnosticCategory Category { get; init; }
        public DateTime? ReceivedAtUtc { get; init; }
        public DateTime? ResultedAtUtc { get; init; }
    }

    public async Task<DiagnosticTurnaroundReportDto> GetTurnaroundReportAsync(DateOnly from, DateOnly to, DiagnosticCategory? category)
    {
        var principal = _httpContextAccessor?.HttpContext?.User;
        var isAdmin = principal?.IsInRole(RoleNames.Admin) == true;
        if (!isAdmin && principal?.IsInRole(RoleNames.LabSupervisor) != true)
            throw new ForbiddenException("Chỉ quản trị viên hoặc trưởng khoa xét nghiệm được xem báo cáo thời gian trả kết quả.");
        if (from > to)
            throw new BusinessException("VALIDATION_ERROR", "Ngày bắt đầu phải trước hoặc bằng ngày kết thúc.");
        if (to.DayNumber - from.DayNumber + 1 > MaxTurnaroundRangeDays)
            throw new BusinessException("VALIDATION_ERROR", $"Khoảng thời gian tối đa là {MaxTurnaroundRangeDays} ngày.");

        var facilityIds = isAdmin
            ? await _dbContext.Facilities.AsNoTracking().Select(facility => facility.Id).ToListAsync()
            : await GetVerificationFacilityIdsAsync(RoleNames.LabSupervisor);
        var startUtc = _dateTimeProvider.ConvertVietnamToUtc(from.ToDateTime(TimeOnly.MinValue));
        var endUtc = _dateTimeProvider.ConvertVietnamToUtc(to.AddDays(1).ToDateTime(TimeOnly.MinValue));

        var query = WhereTechnicianFacilityScope(_dbContext.DiagnosticOrders.AsNoTracking(), facilityIds)
            .Where(order => order.Status == DiagnosticOrderStatus.Completed && order.CompletedAtUtc.HasValue &&
                            order.CompletedAtUtc.Value >= startUtc && order.CompletedAtUtc.Value < endUtc);
        if (category.HasValue)
            query = query.Where(order => order.Items.Any(item =>
                item.Status != DiagnosticItemStatus.Cancelled && item.DiagnosticService.Category == category.Value));

        var rows = await query
            .Select(order => new TurnaroundOrderRow
            {
                Id = order.Id,
                OrderCode = order.OrderCode,
                PatientUserId = order.Patient.UserId,
                PatientFullName = order.Patient.FullName,
                OrderedAtUtc = order.OrderedAtUtc,
                CompletedAtUtc = order.CompletedAtUtc,
                VerificationStatus = order.VerificationStatus,
                SubmittedForVerificationAtUtc = order.SubmittedForVerificationAtUtc,
                VerifiedAtUtc = order.VerifiedAtUtc,
                Items = order.Items
                    .Where(item => item.Status != DiagnosticItemStatus.Cancelled)
                    .Select(item => new TurnaroundItemRow
                    {
                        ServiceId = item.DiagnosticServiceId,
                        ServiceCode = item.DiagnosticService.Code,
                        ServiceName = item.DiagnosticService.Name,
                        Category = item.DiagnosticService.Category,
                        ReceivedAtUtc = item.ReceivedAtUtc,
                        ResultedAtUtc = item.Result != null ? item.Result.ResultedAtUtc : (DateTime?)null
                    }).ToList()
            })
            .ToListAsync();

        // Same patient name fallback as MapBatchedOrder: linked user account → patient record → generic label.
        var patientUserIds = rows.Where(row => row.PatientUserId.HasValue).Select(row => row.PatientUserId!.Value).Distinct().ToList();
        var patientUserNames = patientUserIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _dbContext.Users.AsNoTracking().Where(user => patientUserIds.Contains(user.Id))
                .Select(user => new { user.Id, user.FullName }).ToDictionaryAsync(user => user.Id, user => user.FullName);
        string PatientName(TurnaroundOrderRow row) =>
            (row.PatientUserId.HasValue && patientUserNames.TryGetValue(row.PatientUserId.Value, out var userName) ? userName : null)
            ?? row.PatientFullName ?? "Bệnh nhân";

        var orders = rows.Select(row =>
        {
            var firstReceived = row.Items.Where(item => item.ReceivedAtUtc.HasValue).Min(item => item.ReceivedAtUtc);
            var lastResulted = row.Items.Where(item => item.ResultedAtUtc.HasValue).Max(item => item.ResultedAtUtc);
            // An order's target is the most lenient target among the categories it actually contains.
            var target = row.Items.Count == 0
                ? _tatOptions.TargetFor(DiagnosticCategory.Other)
                : row.Items.Max(item => _tatOptions.TargetFor(item.Category));
            var total = ElapsedMinutes(row.OrderedAtUtc, row.CompletedAtUtc);
            return new
            {
                Row = row,
                OrderToReceive = ElapsedMinutes(row.OrderedAtUtc, firstReceived),
                ReceiveToResult = ElapsedMinutes(firstReceived, lastResulted),
                ResultToVerify = row.VerificationStatus == DiagnosticVerificationStatus.Verified
                    ? ElapsedMinutes(row.SubmittedForVerificationAtUtc, row.VerifiedAtUtc)
                    : null,
                Total = total,
                Target = target,
                WithinTarget = total.HasValue && total.Value <= target
            };
        }).ToList();

        // Only orders with a usable total can be judged against their target.
        var evaluatedCount = orders.Count(order => order.Total.HasValue);
        var withinTargetCount = orders.Count(order => order.WithinTarget);

        var byService = orders
            .SelectMany(order => order.Row.Items
                .Where(item => !category.HasValue || item.Category == category.Value)
                .DistinctBy(item => item.ServiceId)
                .Select(item => new { Item = item, order.Total }))
            .GroupBy(entry => entry.Item.ServiceId)
            .Select(group =>
            {
                var first = group.First().Item;
                var target = _tatOptions.TargetFor(first.Category);
                var totals = group.Where(entry => entry.Total.HasValue).Select(entry => entry.Total!.Value).ToList();
                return new DiagnosticTurnaroundServiceDto
                {
                    ServiceCode = first.ServiceCode,
                    ServiceName = first.ServiceName,
                    Category = first.Category.ToString(),
                    OrderCount = group.Count(),
                    MedianTotalMinutes = Median(totals),
                    P90TotalMinutes = NearestRankP90(totals),
                    TargetMinutes = target,
                    WithinTargetPercent = Percent(totals.Count(total => total <= target), totals.Count)
                };
            })
            .OrderByDescending(service => service.P90TotalMinutes ?? double.MinValue)
            .ThenBy(service => service.ServiceCode, StringComparer.Ordinal)
            .ToList();

        var slowestOrders = orders
            .Where(order => order.Total.HasValue && !order.WithinTarget)
            .OrderByDescending(order => order.Total!.Value)
            .ThenBy(order => order.Row.Id)
            .Take(MaxSlowestOrders)
            .Select(order => new DiagnosticTurnaroundOrderDto
            {
                OrderId = order.Row.Id,
                OrderCode = order.Row.OrderCode,
                PatientName = PatientName(order.Row),
                ServiceNames = order.Row.Items.Select(item => item.ServiceName).Distinct().ToList(),
                OrderedAtUtc = DateTime.SpecifyKind(order.Row.OrderedAtUtc, DateTimeKind.Utc),
                CompletedAtUtc = DateTime.SpecifyKind(order.Row.CompletedAtUtc!.Value, DateTimeKind.Utc),
                TotalMinutes = Math.Round(order.Total!.Value, 1, MidpointRounding.AwayFromZero),
                TargetMinutes = order.Target
            })
            .ToList();

        return new DiagnosticTurnaroundReportDto
        {
            From = from,
            To = to,
            Category = category?.ToString(),
            OrderCount = orders.Count,
            Segments = new List<DiagnosticTurnaroundSegmentDto>
            {
                Segment(SegmentOrderToReceive, orders.Select(order => order.OrderToReceive)),
                Segment(SegmentReceiveToResult, orders.Select(order => order.ReceiveToResult)),
                Segment(SegmentResultToVerify, orders.Select(order => order.ResultToVerify)),
                Segment(SegmentTotal, orders.Select(order => order.Total))
            },
            WithinTargetCount = withinTargetCount,
            WithinTargetPercent = Percent(withinTargetCount, evaluatedCount),
            ByService = byService,
            SlowestOrders = slowestOrders
        };
    }

    /// <summary>Minutes between two instants, or null when either is missing or the span is negative.</summary>
    private static double? ElapsedMinutes(DateTime? start, DateTime? end)
    {
        if (!start.HasValue || !end.HasValue) return null;
        var minutes = (end.Value - start.Value).TotalMinutes;
        return minutes < 0 ? null : minutes;
    }

    private static DiagnosticTurnaroundSegmentDto Segment(string key, IEnumerable<double?> values)
    {
        var present = values.Where(value => value.HasValue).Select(value => value!.Value).ToList();
        return new DiagnosticTurnaroundSegmentDto
        {
            Key = key,
            Count = present.Count,
            MedianMinutes = Median(present),
            P90Minutes = NearestRankP90(present)
        };
    }

    private static double? Median(IReadOnlyCollection<double> values)
    {
        if (values.Count == 0) return null;
        var sorted = values.OrderBy(value => value).ToList();
        var middle = sorted.Count / 2;
        var median = sorted.Count % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
        return Math.Round(median, 1, MidpointRounding.AwayFromZero);
    }

    /// <summary>Nearest-rank P90: the ceil(0.9 × n)-th smallest value (integer arithmetic avoids float drift).</summary>
    private static double? NearestRankP90(IReadOnlyCollection<double> values)
    {
        if (values.Count == 0) return null;
        var sorted = values.OrderBy(value => value).ToList();
        var rank = (9 * sorted.Count + 9) / 10;
        return Math.Round(sorted[rank - 1], 1, MidpointRounding.AwayFromZero);
    }

    private static double? Percent(int part, int whole) =>
        whole == 0 ? null : Math.Round(100.0 * part / whole, 1, MidpointRounding.AwayFromZero);

}
