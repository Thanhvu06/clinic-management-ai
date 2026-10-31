using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClinicManagement.Application.Billing.DTOs;
using ClinicManagement.Application.Billing.Interfaces;
using ClinicManagement.Application.Authentication.Interfaces;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Notifications.DTOs;
using ClinicManagement.Application.Notifications.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Common;
using ClinicManagement.Infrastructure.Persistence;
using ClinicManagement.Infrastructure.Pharmacy;
using ClinicManagement.Infrastructure.Visits;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Infrastructure.Billing;

public partial class BillingService
{
    public async Task<PaymentDto> ProcessPaymentAsync(long invoiceId, ProcessPaymentRequest request, Guid receivedByUserId, CancellationToken cancellationToken = default)
    {
        await _facilityAuthService.ValidateInvoiceAccessAsync(receivedByUserId, invoiceId, cancellationToken);
        if (!Enum.IsDefined(typeof(PaymentMethod), request.Method))
            throw new BusinessException("INVALID_PAYMENT_METHOD", "Phương thức thanh toán không hợp lệ.");

        if (request.Method == PaymentMethod.ManualBankTransfer && string.IsNullOrWhiteSpace(request.ReferenceCode))
            throw new BusinessException("REFERENCE_CODE_REQUIRED", "Chuyển khoản thủ công bắt buộc phải có mã tham chiếu.");

        var invoice = await _dbContext.Invoices
            .Include(i => i.Patient)
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == invoiceId, cancellationToken);

        if (invoice == null)
            throw new NotFoundException("Hóa đơn không tồn tại.");

        ValidatePayableInvoice(invoice, request.Amount);

        var executionStrategy = _dbContext.Database.CreateExecutionStrategy();

        return await executionStrategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

            try
            {
                var currentInvoice = await _dbContext.Invoices
                    .Include(i => i.Patient)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(i => i.Id == invoiceId, cancellationToken);

                if (currentInvoice == null)
                    throw new NotFoundException("Hóa đơn không tồn tại.");

                ValidatePayableInvoice(currentInvoice, request.Amount);

                var paidAtUtc = _dateTimeProvider.UtcNow;
                var updatedRows = await _dbContext.Invoices
                    .Where(i =>
                        i.Id == invoiceId &&
                        i.Status == InvoiceStatus.Unpaid &&
                        i.TotalAmount == request.Amount)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(i => i.Status, InvoiceStatus.Paid)
                        .SetProperty(i => i.PaidByUserId, (Guid?)receivedByUserId)
                        .SetProperty(i => i.PaidAtUtc, (DateTime?)paidAtUtc),
                        cancellationToken);

                if (updatedRows != 1)
                    throw new ConflictException("PAYMENT_CONFLICT", "Hóa đơn vừa được xử lý bởi một yêu cầu khác. Vui lòng tải lại dữ liệu.");

                if (currentInvoice.PatientVisitId.HasValue)
                {
                    await VisitCompletionCoordinator.TryUpdateVisitProgressAsync(
                        currentInvoice.PatientVisitId.Value,
                        _dbContext,
                        paidAtUtc,
                        cancellationToken);
                }
                else if (currentInvoice.AppointmentId.HasValue)
                {
                    var linkedVisit = await _dbContext.PatientVisits
                        .FirstOrDefaultAsync(v => v.AppointmentId == currentInvoice.AppointmentId.Value, cancellationToken);
                    if (linkedVisit != null)
                    {
                        await VisitCompletionCoordinator.TryUpdateVisitProgressAsync(
                            linkedVisit.Id,
                            _dbContext,
                            paidAtUtc,
                            cancellationToken);
                    }
                }

                await NotifyPaidPrescriptionsAsync(currentInvoice, cancellationToken);

                var paymentCode = GeneratePaymentCode();
                var payment = new Payment
                {
                    PaymentCode = paymentCode,
                    InvoiceId = currentInvoice.Id,
                    Amount = request.Amount,
                    Method = request.Method,
                    ReferenceCode = request.ReferenceCode?.Trim(),
                    Note = request.Note?.Trim(),
                    ReceivedByUserId = receivedByUserId,
                    ReceivedAtUtc = paidAtUtc,
                    Status = PaymentStatus.Succeeded
                };

                _dbContext.Payments.Add(payment);
                _dbContext.SystemAuditLogs.Add(new SystemAuditLog
                {
                    UserId = receivedByUserId,
                    Action = "PROCESS_PAYMENT",
                    EntityName = "Payment",
                    EntityId = paymentCode,
                    Description = $"Thu tiền hóa đơn #{currentInvoice.InvoiceCode}: {ClinicManagement.Infrastructure.Common.BusinessDisplay.Money(payment.Amount)} ({(payment.Method == PaymentMethod.Cash ? "Tiền mặt" : "Chuyển khoản")})",
                    CreatedAt = _dateTimeProvider.UtcNow
                });

                var methodLabel = payment.Method == PaymentMethod.Cash ? "Tiền mặt" : "Chuyển khoản";
                if (currentInvoice.Patient.UserId.HasValue)
                {
                    await _notificationService.CreateNotificationAsync(new CreateNotificationRequest
                    {
                        UserId = currentInvoice.Patient.UserId.Value,
                        Type = NotificationType.Payment,
                        Title = "Thanh toán thành công",
                        Message = $"Hóa đơn #{currentInvoice.InvoiceCode} đã được thanh toán thành công ({ClinicManagement.Infrastructure.Common.BusinessDisplay.Money(payment.Amount)} qua {methodLabel}).",
                        Route = "/patient/invoices",
                        RelatedEntityType = "Invoice",
                        RelatedEntityId = currentInvoice.Id.ToString(),
                        DedupeKey = $"pay_success_{paymentCode}"
                    }, cancellationToken);
                }

                await _dbContext.SaveChangesAsync(cancellationToken);

                var receiver = await _dbContext.Users.AsNoTracking()
                    .FirstOrDefaultAsync(u => u.Id == receivedByUserId, cancellationToken);

                await transaction.CommitAsync(cancellationToken);

                return new PaymentDto
                {
                    Id = payment.Id,
                    PaymentCode = payment.PaymentCode,
                    InvoiceId = payment.InvoiceId,
                    InvoiceCode = currentInvoice.InvoiceCode,
                    Amount = payment.Amount,
                    Method = payment.Method,
                    MethodName = methodLabel,
                    ReferenceCode = payment.ReferenceCode,
                    Note = payment.Note,
                    ReceivedByUserId = payment.ReceivedByUserId,
                    ReceivedByUserName = receiver?.FullName,
                    ReceivedAtUtc = payment.ReceivedAtUtc,
                    Status = payment.Status,
                    StatusName = "Thành công"
                };
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                _dbContext.ChangeTracker.Clear();
                throw;
            }
        });
    }
}
