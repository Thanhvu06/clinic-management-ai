using ClinicManagement.Application.Billing.DTOs;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Notifications.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Billing;

public partial class BillingService
{
    public async Task<InvoiceDetailDto> CancelInvoiceAsync(long invoiceId, string reason, Guid cancelledByUserId, CancellationToken cancellationToken = default)
    {
        await _facilityAuthService.ValidateInvoiceAccessAsync(cancelledByUserId, invoiceId, cancellationToken);
        if (string.IsNullOrWhiteSpace(reason))
            throw new BusinessException("REASON_REQUIRED", "Vui lòng nhập lý do hủy hóa đơn.");

        var invoice = await _dbContext.Invoices
            .Include(i => i.Patient)
            .Include(i => i.Payments)
            .Include(i => i.Items)
            .FirstOrDefaultAsync(i => i.Id == invoiceId, cancellationToken);

        if (invoice == null)
            throw new NotFoundException("Hóa đơn không tồn tại.");

        if (invoice.Status == InvoiceStatus.Paid)
            throw new BusinessException("CANNOT_CANCEL_PAID", "Không thể hủy hóa đơn đã thanh toán.");

        if (invoice.Status == InvoiceStatus.Cancelled)
            throw new BusinessException("ALREADY_CANCELLED", "Hóa đơn này đã bị hủy trước đó.");

        invoice.Status = InvoiceStatus.Cancelled;
        // Release the filtered unique charge index together with cancellation.
        foreach (var item in invoice.Items) item.IsCancelled = true;
        invoice.CancelledAtUtc = _dateTimeProvider.UtcNow;
        invoice.CancellationReason = reason.Trim();

        await CancelPackageRegistrationAsync(invoice, cancellationToken);
        await ReleaseCancelledInvoiceReservationsAsync(invoice, cancelledByUserId, cancellationToken);

        _dbContext.SystemAuditLogs.Add(new SystemAuditLog
        {
            UserId = cancelledByUserId,
            Action = "CANCEL_INVOICE",
            EntityName = "Invoice",
            EntityId = invoice.Id.ToString(),
            Description = $"Hủy hóa đơn #{invoice.InvoiceCode}. Lý do: {invoice.CancellationReason}",
            CreatedAt = _dateTimeProvider.UtcNow
        });

        if (invoice.Patient.UserId.HasValue)
        {
            await _notificationService.CreateNotificationAsync(new CreateNotificationRequest
            {
                UserId = invoice.Patient.UserId.Value,
                Type = NotificationType.Invoice,
                Title = "Hóa đơn đã bị hủy",
                Message = $"Hóa đơn #{invoice.InvoiceCode} đã bị hủy. Lý do: {invoice.CancellationReason}.",
                Route = "/patient/invoices",
                RelatedEntityType = "Invoice",
                RelatedEntityId = invoice.Id.ToString(),
                DedupeKey = $"inv_cancelled_{invoice.Id}"
            }, cancellationToken);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return await GetInvoiceDetailAsync(invoice.Id, cancellationToken);
    }

    private async Task CancelPackageRegistrationAsync(Invoice invoice, CancellationToken cancellationToken)
    {
        if (invoice.HealthPackageRegistrationId is not { } id) return;
        var registration = await _dbContext.HealthPackageRegistrations.SingleAsync(r => r.Id == id, cancellationToken);
        registration.Status = HealthPackageRegistrationStatus.Cancelled;
        registration.CancellationReason = invoice.CancellationReason;
        registration.UpdatedAt = _dateTimeProvider.UtcNow;
    }
}
