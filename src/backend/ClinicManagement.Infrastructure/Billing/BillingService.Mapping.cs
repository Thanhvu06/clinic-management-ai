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


using ClinicManagement.Infrastructure.Expiry;
using ClinicManagement.Application.Common.Expiry;
using Microsoft.Extensions.Options;
namespace ClinicManagement.Infrastructure.Billing;

public partial class BillingService
{
    private readonly ExpiryRulesOptions _expiryOptions;
    public BillingService(
        AppDbContext dbContext,
        INotificationService notificationService,
        IDateTimeProvider dateTimeProvider,
        ILogger<BillingService> logger,
        IFacilityAuthorizationService facilityAuthService,
        ICurrentUserService currentUserService, IOptions<ExpiryRulesOptions>? expiryOptions = null)
    {
        _expiryOptions = expiryOptions?.Value ?? new ExpiryRulesOptions();
        _dbContext = dbContext;
        _notificationService = notificationService;
        _dateTimeProvider = dateTimeProvider;
        _logger = logger;
        _facilityAuthService = facilityAuthService;
        _currentUserService = currentUserService;
    }


    private InvoiceDto MapToDto(Invoice i, Dictionary<Guid, UserInfo> userMap)
    {
        UserInfo? patUser = null;
        if (i.Patient.UserId.HasValue)
        {
            userMap.TryGetValue(i.Patient.UserId.Value, out patUser);
        }
        userMap.TryGetValue(i.CreatedByUserId, out var creatorUser);
        UserInfo? payerUser = null;
        if (i.PaidByUserId.HasValue) userMap.TryGetValue(i.PaidByUserId.Value, out payerUser);

        return new InvoiceDto
        {
            Id = i.Id,
            InvoiceCode = i.InvoiceCode,
            PatientId = i.PatientId,
            PatientName = patUser?.FullName ?? i.Patient.FullName ?? string.Empty,
            PatientPhone = patUser?.PhoneNumber ?? i.Patient.PhoneNumber ?? (i.HealthPackageRegistration?.ContactPhone ?? string.Empty),
            SourceType = i.SourceType,
            SourceTypeName = i.SourceType == InvoiceSourceType.Appointment ? "Khám bệnh" : "Gói khám sức khỏe",
            AppointmentId = i.AppointmentId,
            AppointmentCode = i.Appointment?.AppointmentCode ?? (i.PatientVisit != null ? i.PatientVisit.VisitCode : null),
            PatientVisitId = i.PatientVisitId,
            VisitCode = i.PatientVisit?.VisitCode,
            HealthPackageRegistrationId = i.HealthPackageRegistrationId,
            RegistrationCode = i.HealthPackageRegistration?.RegistrationCode,
            Status = i.Status,
            StatusName = FormatStatus(i.Status),
            IsOverdue = UnpaidInvoiceExpiryRule.IsOverdue(i, _dateTimeProvider, _expiryOptions),
            Subtotal = i.Subtotal,
            TotalAmount = i.TotalAmount,
            CreatedAtUtc = i.CreatedAtUtc,
            PaidAtUtc = i.PaidAtUtc,
            CancelledAtUtc = i.CancelledAtUtc,
            CancellationReason = i.CancellationReason,
            CreatedByUserName = creatorUser?.FullName,
            PaidByUserName = payerUser?.FullName
        };
    }

    private InvoiceDetailDto MapToDetailDto(Invoice i, Dictionary<Guid, UserInfo> userMap)
    {
        var baseDto = MapToDto(i, userMap);

        var items = i.Items.Select(item => new InvoiceItemDto
        {
            Id = item.Id,
            InvoiceId = item.InvoiceId,
            ItemCode = item.ItemCode,
            Description = item.Description,
            Quantity = item.Quantity,
            UnitPrice = item.UnitPrice,
            LineTotal = item.LineTotal,
            ReferenceType = item.ReferenceType.StartsWith("PrescriptionItem") ? "PrescriptionItem" : item.ReferenceType,
            ReferenceId = item.ReferenceId
        }).ToList();

        var payments = i.Payments.Select(p =>
        {
            userMap.TryGetValue(p.ReceivedByUserId, out var receiver);
            return new PaymentDto
            {
                Id = p.Id,
                PaymentCode = p.PaymentCode,
                InvoiceId = p.InvoiceId,
                InvoiceCode = i.InvoiceCode,
                Amount = p.Amount,
                Method = p.Method,
                MethodName = p.Method == PaymentMethod.Cash ? "Tiền mặt" : "Chuyển khoản",
                ReferenceCode = p.ReferenceCode,
                Note = p.Note,
                ReceivedByUserId = p.ReceivedByUserId,
                ReceivedByUserName = receiver?.FullName,
                ReceivedAtUtc = p.ReceivedAtUtc,
                Status = p.Status,
                StatusName = p.Status == PaymentStatus.Succeeded ? "Thành công" : "Đã hủy"
            };
        }).ToList();

        return new InvoiceDetailDto
        {
            Id = baseDto.Id,
            InvoiceCode = baseDto.InvoiceCode,
            PatientId = baseDto.PatientId,
            PatientName = baseDto.PatientName,
            PatientPhone = baseDto.PatientPhone,
            SourceType = baseDto.SourceType,
            SourceTypeName = baseDto.SourceTypeName,
            AppointmentId = baseDto.AppointmentId,
            AppointmentCode = baseDto.AppointmentCode,
            PatientVisitId = baseDto.PatientVisitId,
            VisitCode = baseDto.VisitCode,
            HealthPackageRegistrationId = baseDto.HealthPackageRegistrationId,
            RegistrationCode = baseDto.RegistrationCode,
            Status = baseDto.Status,
            StatusName = baseDto.StatusName,
            IsOverdue = baseDto.IsOverdue,
            Subtotal = baseDto.Subtotal,
            TotalAmount = baseDto.TotalAmount,
            CreatedAtUtc = baseDto.CreatedAtUtc,
            PaidAtUtc = baseDto.PaidAtUtc,
            CancelledAtUtc = baseDto.CancelledAtUtc,
            CancellationReason = baseDto.CancellationReason,
            CreatedByUserName = baseDto.CreatedByUserName,
            PaidByUserName = baseDto.PaidByUserName,
            Items = items,
            Payments = payments
        };
    }

}
