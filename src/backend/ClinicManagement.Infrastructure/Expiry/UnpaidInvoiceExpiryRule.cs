using ClinicManagement.Application.Common.Expiry;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Infrastructure.Expiry;

public static class UnpaidInvoiceExpiryRule
{
    public static bool IsOverdue(Invoice invoice, IDateTimeProvider clock, ExpiryRulesOptions options) =>
        invoice.Status == InvoiceStatus.Unpaid && invoice.CreatedAtUtc <= clock.UtcNow.AddDays(-options.InvoiceOverdueDays);
}
