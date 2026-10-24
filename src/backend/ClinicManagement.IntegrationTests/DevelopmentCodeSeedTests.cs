using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using ClinicManagement.Infrastructure.Pharmacy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ClinicManagement.IntegrationTests;

public class DevelopmentCodeSeedTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AppointmentSeed_UsesRealFormat_AndRepairsLegacyInPlace_WithoutDuplicates(bool legacy)
    {
        using var factory = new CustomWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<AppDbContext>();
        await DevelopmentDataSeeder.SeedDoctorSchedulesAndSlotsAsync(services, 30, true);
        var appointments = await db.Appointments.OrderBy(a => a.Id).ToListAsync();
        Assert.NotEmpty(appointments);
        var first = appointments[0];
        var actor = (await db.Users.SingleAsync(u => u.Email == "admin@cliniccare.local")).Id;
        if (legacy)
        {
            first.AppointmentCode = "DEMO-11111-01";
            first.Reason = "Khám theo lịch DEMO-11111-01";
            first.Status = AppointmentStatus.CheckedIn;
            db.SystemAuditLogs.RemoveRange(await db.SystemAuditLogs.Where(l => l.Action == "InitializeAppointments").ToListAsync());
            db.Notifications.Add(new Notification { UserId = actor, Type = NotificationType.Appointment, Title = first.AppointmentCode, Message = $"Đã tiếp nhận {first.AppointmentCode}" });
            db.AppointmentHistories.Add(new AppointmentHistory { AppointmentId = first.Id, Action = AppointmentHistoryAction.CheckedIn, PerformedByUserId = actor, Note = $"Tiếp nhận {first.AppointmentCode}" });
        }
        var facility = await db.Facilities.SingleAsync(f => f.Code == "CS01");
        var department = await db.Departments.FirstAsync(d => d.FacilityId == facility.Id);
        var visit = new PatientVisit
        {
            PatientId = first.PatientId, AppointmentId = first.Id, FacilityId = facility.Id, DepartmentId = department.Id,
            VisitDate = first.AppointmentDate, VisitCode = "VIS-260101-0001", CreatedByUserId = actor,
            ChiefComplaint = $"Khám theo lịch {first.AppointmentCode}", Status = VisitStatus.CheckedIn,
        };
        db.PatientVisits.Add(visit);
        var invoice = new Invoice
        {
            InvoiceCode = $"INV-{first.AppointmentCode}", AppointmentId = first.Id, PatientVisit = visit,
            PatientId = first.PatientId, CreatedByUserId = actor, SourceType = InvoiceSourceType.Appointment,
            Items = [new InvoiceItem { ItemCode = "CONSULT", Description = $"Khám theo lịch {first.AppointmentCode}", Quantity = 1 }],
        };
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();
        var snapshot = await db.Appointments.OrderBy(a => a.Id).Select(a => new { a.Id, a.PatientId, a.DoctorId, a.AppointmentSlotId, a.Status, a.AppointmentDate, a.StartTime, a.EndTime }).ToListAsync();
        var historyIds = await db.AppointmentHistories.OrderBy(h => h.Id).Select(h => h.Id).ToListAsync();
        var prescriptions = await db.Prescriptions.OrderBy(p => p.Id).Select(p => new { p.Id, p.AppointmentId }).ToListAsync();

        await DevelopmentDataSeeder.SeedMasterDataAsync(services);
        await DevelopmentDataSeeder.SeedDoctorSchedulesAndSlotsAsync(services, 30, true);
        await DevelopmentDataSeeder.SeedDoctorSchedulesAndSlotsAsync(services, 30, true);
        db.ChangeTracker.Clear();

        Assert.Equal(snapshot, await db.Appointments.OrderBy(a => a.Id).Select(a => new { a.Id, a.PatientId, a.DoctorId, a.AppointmentSlotId, a.Status, a.AppointmentDate, a.StartTime, a.EndTime }).ToListAsync());
        var codes = await db.Appointments.Select(a => a.AppointmentCode).ToListAsync();
        Assert.All(codes, code => Assert.Matches(@"^APT-\d{6}-[0-9A-F]{8}$", code));
        Assert.Equal(codes.Count, codes.Distinct().Count());
        Assert.Equal(historyIds, await db.AppointmentHistories.OrderBy(h => h.Id).Select(h => h.Id).ToListAsync());
        Assert.Equal(prescriptions, await db.Prescriptions.OrderBy(p => p.Id).Select(p => new { p.Id, p.AppointmentId }).ToListAsync());
        Assert.Equal(first.Id, (await db.PatientVisits.SingleAsync(v => v.Id == visit.Id)).AppointmentId);
        Assert.Equal(first.Id, (await db.Invoices.SingleAsync(i => i.Id == invoice.Id)).AppointmentId);
        Assert.Equal(visit.Id, (await db.Invoices.SingleAsync(i => i.Id == invoice.Id)).PatientVisitId);
        Assert.Single(await db.SystemAuditLogs.Where(l => l.Action == "InitializeAppointments").ToListAsync());
        var display = (await db.Appointments.Select(a => a.Reason).ToListAsync())
            .Concat(await db.AppointmentHistories.Select(h => h.Note).ToListAsync())
            .Concat(await db.Notifications.Select(n => n.Title + n.Message).ToListAsync())
            .Concat(await db.Invoices.Select(i => i.InvoiceCode).ToListAsync())
            .Concat(await db.InvoiceItems.Select(i => i.Description).ToListAsync())
            .Concat(await db.PatientVisits.Select(v => v.VisitCode + v.ChiefComplaint).ToListAsync());
        Assert.All(display, text => Assert.DoesNotContain("DEMO", (text ?? "").ToUpperInvariant()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BatchSeed_UsesManufacturerFormat_AndPreservesStockLinksAndFefo(bool legacy)
    {
        using var factory = new CustomWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<AppDbContext>();
        var clock = services.GetRequiredService<IDateTimeProvider>();
        await DevelopmentDataSeeder.SeedMasterDataAsync(services);
        var actor = (await db.Users.SingleAsync(u => u.Email == "pharmacist@cliniccare.local")).Id;
        await MedicineBatchDevelopmentSeed.SeedAsync(db, clock, actor, true);
        var batches = await db.MedicineBatches.OrderBy(b => b.Id).ToListAsync();
        if (legacy)
        {
            foreach (var batch in batches) batch.BatchNumber = $"DEMO-{batch.Id}-60";
            foreach (var entry in await db.MedicineStockTransactions.Where(t => t.MedicineBatchId != null).ToListAsync())
                entry.Reason = "Lô hết hạn minh họa dữ liệu demo";
            await db.SaveChangesAsync();
        }
        var snapshot = await db.MedicineBatches.OrderBy(b => b.Id).Select(b => new { b.Id, b.MedicineId, b.ExpiryDate, b.QuantityReceived, b.QuantityRemaining, b.UnitCost, b.ReceivedAt, b.DisposedAt }).ToListAsync();
        var stock = await db.Medicines.OrderBy(m => m.Id).Select(m => new { m.Id, m.StockQuantity }).ToListAsync();
        var history = await db.MedicineStockTransactions.OrderBy(t => t.Id).Select(t => new { t.Id, t.MedicineBatchId, t.PrescriptionId, t.QuantityChange, t.BalanceAfter, t.Type }).ToListAsync();
        var medicineId = batches[0].MedicineId;
        var medicine = await db.Medicines.SingleAsync(m => m.Id == medicineId);
        var before = await new MedicineBatchAllocator(db, clock).Deduct(medicine, 1, MedicineStockTransactionType.Dispense, null, "Cấp thuốc", actor);
        db.ChangeTracker.Clear();

        await MedicineBatchDevelopmentSeed.SeedAsync(db, clock, actor, true);
        await MedicineBatchDevelopmentSeed.SeedAsync(db, clock, actor, true);
        db.ChangeTracker.Clear();

        Assert.Equal(snapshot, await db.MedicineBatches.OrderBy(b => b.Id).Select(b => new { b.Id, b.MedicineId, b.ExpiryDate, b.QuantityReceived, b.QuantityRemaining, b.UnitCost, b.ReceivedAt, b.DisposedAt }).ToListAsync());
        Assert.Equal(stock, await db.Medicines.OrderBy(m => m.Id).Select(m => new { m.Id, m.StockQuantity }).ToListAsync());
        Assert.Equal(history, await db.MedicineStockTransactions.OrderBy(t => t.Id).Select(t => new { t.Id, t.MedicineBatchId, t.PrescriptionId, t.QuantityChange, t.BalanceAfter, t.Type }).ToListAsync());
        var numbers = await db.MedicineBatches.Select(b => b.BatchNumber).ToListAsync();
        Assert.All(numbers, number => Assert.Matches(@"^LOT-\d{4}-\d{3}$", number));
        Assert.Equal(numbers.Count, numbers.Distinct().Count());
        Assert.All(await db.MedicineStockTransactions.Select(t => t.Reason).ToListAsync(), reason => Assert.DoesNotContain("DEMO", (reason ?? "").ToUpperInvariant()));
        medicine = await db.Medicines.SingleAsync(m => m.Id == medicineId);
        var after = await new MedicineBatchAllocator(db, clock).Deduct(medicine, 1, MedicineStockTransactionType.Dispense, null, "Cấp thuốc", actor);
        Assert.Equal(before, after);
    }
}
