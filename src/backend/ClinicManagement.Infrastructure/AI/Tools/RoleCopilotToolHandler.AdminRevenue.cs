using System.Globalization;
using System.Text.Json;
using ClinicManagement.Application.AI.Tools;
using ClinicManagement.Infrastructure.AI.Planning;

namespace ClinicManagement.Infrastructure.AI.Tools;

public sealed partial class RoleCopilotToolHandler
{
    private async Task<AiToolExecutionResult> GetRevenueReportAsync(AiToolExecutionContext context, string json, CancellationToken ct)
    {
        if (!context.Roles.Contains(AiActorRole.Admin)) return ScopeDenied();
        if (_billing is null) return DataUnavailable();
        using var document = JsonDocument.Parse(json);
        var period = document.RootElement.GetProperty("period").GetString()!;
        if (!AdminRevenuePeriod.TryResolve(period, _clock.VietnamToday, out var from, out var to))
            return AiToolExecutionResult.Failed("INVALID_TOOL_ARGUMENTS", "Kỳ doanh thu không hợp lệ.");
        var report = await _billing.GetRevenueReportAsync(from, to, ct);
        var data = new { period, report.FromDate, report.ToDate, report.TotalRevenue, report.TotalSucceededTransactions, report.DailyBreakdown, report.StatusBreakdown };
        return Completed(data, "admin_revenue_report",
            $"Doanh thu {AdminRevenuePeriod.Label(report.FromDate, report.ToDate, period)}: {report.TotalRevenue.ToString("N0", CultureInfo.GetCultureInfo("vi-VN"))} đồng, {report.TotalSucceededTransactions} giao dịch thành công.");
    }
}
