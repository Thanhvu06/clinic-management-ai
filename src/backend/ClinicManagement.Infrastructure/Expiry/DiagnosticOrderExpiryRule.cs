using ClinicManagement.Application.Common.Expiry;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ClinicManagement.Infrastructure.Expiry;

public sealed class DiagnosticOrderExpiryRule(AppDbContext db, IDateTimeProvider clock, IOptions<ExpiryRulesOptions> options)
    : ExpiryRule(db, clock, options)
{
    protected override async Task<int> ApplyBatchAsync(CancellationToken ct)
    {
        var cutoff = Clock.UtcNow.AddHours(-Options.DiagnosticHours);
        var items = await Db.DiagnosticOrders.Include(o => o.Items).Include(o => o.OrderingDoctor)
            .Where(o => o.Status == DiagnosticOrderStatus.Ordered && o.OrderedAtUtc <= cutoff && o.StartedAtUtc == null &&
                !o.Items.Any(i => i.Status == DiagnosticItemStatus.InProgress || i.Status == DiagnosticItemStatus.Completed ||
                    i.CollectedAtUtc != null || i.ReceivedAtUtc != null || i.SpecimenStatus == SpecimenStatus.Collected ||
                    i.SpecimenStatus == SpecimenStatus.Received || i.Result != null || i.Attachments.Any()))
            .OrderBy(o => o.Id).Take(Options.BatchSize).ToListAsync(ct);
        foreach (var o in items)
        {
            o.Status = DiagnosticOrderStatus.Cancelled;
            o.CancelledAtUtc = Clock.UtcNow;
            var previousNote = o.Note?.Trim();
            o.Note = string.IsNullOrEmpty(previousNote) ? "Quá hạn thực hiện"
                : $"{previousNote[..Math.Min(previousNote.Length, 980)]}\nQuá hạn thực hiện";
            foreach (var item in o.Items.Where(i => i.Status == DiagnosticItemStatus.Ordered)) item.Status = DiagnosticItemStatus.Cancelled;
            Audit(nameof(DiagnosticOrder), o.Id, o.OrderCode, "Quá hạn thực hiện");
            Notify(o.OrderingDoctor.UserId, NotificationType.Diagnostic, nameof(DiagnosticOrder), o.Id, o.OrderCode, "Quá hạn thực hiện", "/doctor/queue");
        }
        return items.Count;
    }
}
