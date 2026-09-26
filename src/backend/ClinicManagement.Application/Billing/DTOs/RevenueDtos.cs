using System;
using System.Collections.Generic;

namespace ClinicManagement.Application.Billing.DTOs;

public class DailyRevenueDto
{
    public DateOnly Date { get; set; }
    public decimal Revenue { get; set; }
    public int SucceededPaymentsCount { get; set; }
    public int PaidInvoicesCount { get; set; }
}

public class InvoiceStatusBreakdownDto
{
    public int UnpaidCount { get; set; }
    public decimal UnpaidAmount { get; set; }
    public int PaidCount { get; set; }
    public decimal PaidAmount { get; set; }
    public int CancelledCount { get; set; }
    public decimal CancelledAmount { get; set; }
}

public class RevenueReportDto
{
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
    public decimal TotalRevenue { get; set; }
    public int TotalSucceededTransactions { get; set; }
    public List<DailyRevenueDto> DailyBreakdown { get; set; } = new();
    public InvoiceStatusBreakdownDto StatusBreakdown { get; set; } = new();
}

public class BillingKpiDto
{
    public int TodayUnpaidInvoices { get; set; }
    public int TodayPaidInvoices { get; set; }
    public int TodayCancelledInvoices { get; set; }
    public decimal TodayRevenue { get; set; }
}
