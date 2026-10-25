using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ClinicManagement.IntegrationTests;

public class DemoDataRealismTests
{
    private static async Task<(CustomWebApplicationFactory Factory, IServiceScope Scope, AppDbContext Db, IDateTimeProvider Clock)> SeedAsync()
    {
        var factory = new CustomWebApplicationFactory();
        var scope = factory.Services.CreateScope();
        await DevelopmentDataSeeder.SeedDoctorSchedulesAndSlotsAsync(scope.ServiceProvider, 7, true);
        return (factory, scope, scope.ServiceProvider.GetRequiredService<AppDbContext>(), scope.ServiceProvider.GetRequiredService<IDateTimeProvider>());
    }

    [Fact]
    public async Task EveryDoctor_HasTwoToFourUpcomingAppointments_InsideTheirShifts()
    {
        var (factory, scope, db, clock) = await SeedAsync();
        using var _ = factory; using var __ = scope;
        var today = clock.VietnamToday;
        var doctors = await db.Doctors.Include(d => d.DoctorSpecialties).ThenInclude(ds => ds.Specialty).ToListAsync();
        var upcoming = await db.Appointments.Include(a => a.Patient).Include(a => a.Specialty)
            .Where(a => a.AppointmentDate > today && a.AppointmentDate <= today.AddDays(7)).ToListAsync();
        var schedules = await db.DoctorWorkSchedules.Where(s => s.IsActive && s.WorkDate > today).ToListAsync();
        var leaves = await db.DoctorLeaveRequests.Where(l => l.Status == DoctorLeaveRequestStatus.Approved).ToListAsync();

        foreach (var doctor in doctors)
        {
            var mine = upcoming.Where(a => a.DoctorId == doctor.Id).ToList();
            Assert.InRange(mine.Count, 2, 4);
            Assert.All(mine, a =>
            {
                Assert.Contains(a.Status, new[] { AppointmentStatus.Pending, AppointmentStatus.Confirmed });
                Assert.Contains(schedules, s => s.DoctorId == doctor.Id && s.WorkDate == a.AppointmentDate && s.StartTime <= a.StartTime && s.EndTime >= a.EndTime);
                Assert.DoesNotContain(leaves, l => l.DoctorId == doctor.Id && DateOnly.FromDateTime(l.StartDateTime) == a.AppointmentDate);
                Assert.Contains(a.Reason, new[] { RealisticDevelopmentSeed.Reason(a.Specialty.SpecialtyCode, 0), RealisticDevelopmentSeed.Reason(a.Specialty.SpecialtyCode, 1) }
                    .Concat(Enumerable.Range(0, 8).Select(i => RealisticDevelopmentSeed.Reason("SP01", i))));
                if (a.Specialty.SpecialtyCode == "SP03") Assert.Equal(Gender.Female, a.Patient.Gender);
            });
        }
        Assert.DoesNotContain(schedules, s => s.WorkDate.DayOfWeek == DayOfWeek.Saturday && s.StartTime >= new TimeOnly(12, 0));
        Assert.DoesNotContain(await db.AppointmentSlots.Where(s => s.SlotDate >= today).ToListAsync(),
            s => s.SlotDate.DayOfWeek == DayOfWeek.Saturday && s.StartTime >= new TimeOnly(12, 0));
    }

    [Fact]
    public async Task Patient_HasAtMostOneAppointmentOrVisit_PerDoctorPerDay()
    {
        var (factory, scope, db, _) = await SeedAsync();
        using var f = factory; using var s = scope;
        var appointments = await db.Appointments.Select(a => new { a.DoctorId, Date = a.AppointmentDate, a.PatientId }).ToListAsync();
        var walkIns = await db.PatientVisits.Where(v => v.AppointmentId == null && v.AssignedDoctorId != null)
            .Select(v => new { DoctorId = v.AssignedDoctorId!.Value, Date = v.VisitDate, v.PatientId }).ToListAsync();
        var duplicates = appointments.Concat(walkIns).GroupBy(x => x).Where(g => g.Count() > 1).ToList();
        Assert.Empty(duplicates);
        Assert.True(appointments.Concat(walkIns).Select(x => x.PatientId).Distinct().Count() >= 12);
    }

    [Fact]
    public async Task TodayDiagnosticOrders_ComeFromDifferentDoctors_WithFittingServicesAndRelativeTimes()
    {
        var (factory, scope, db, clock) = await SeedAsync();
        using var f = factory; using var s = scope;
        var now = clock.UtcNow;
        var orders = await db.DiagnosticOrders.Include(o => o.Patient).Include(o => o.OrderingDoctor).ThenInclude(d => d.DoctorSpecialties).ThenInclude(ds => ds.Specialty)
            .Include(o => o.Items).ThenInclude(i => i.DiagnosticService).Include(o => o.Items).ThenInclude(i => i.Result)
            .Where(o => o.PatientVisit != null && o.PatientVisit.QueueNumber >= 200 && o.PatientVisit.QueueNumber < 205).ToListAsync();

        Assert.Equal(5, orders.Count);
        Assert.Equal(5, orders.Select(o => o.OrderingDoctorId).Distinct().Count());
        var services = orders.SelectMany(o => o.Items).Select(i => i.DiagnosticService).ToList();
        Assert.True(services.Select(x => x.Code).Distinct().Count() >= 5);
        foreach (var category in new[] { DiagnosticCategory.Laboratory, DiagnosticCategory.Ultrasound, DiagnosticCategory.Imaging, DiagnosticCategory.Other })
            Assert.Contains(services, x => x.Category == category);
        Assert.All(orders, o =>
        {
            var specialty = o.OrderingDoctor.DoctorSpecialties.Single(ds => ds.IsPrimary).Specialty.SpecialtyCode;
            if (specialty == "SP03") Assert.Equal(Gender.Female, o.Patient!.Gender);
            Assert.InRange((now - o.OrderedAtUtc).TotalMinutes, 9.5, 45.5);
            Assert.All(o.Items, i => Assert.Equal(i.DiagnosticService.SpecimenType == null, i.SpecimenStatus == SpecimenStatus.NotRequired));
            if (o.VerifiedByUserId.HasValue)
                Assert.All(o.Items, i => Assert.NotEqual(i.Result!.ResultedByUserId, o.VerifiedByUserId));
        });
        Assert.Single(orders, o => (now - o.OrderedAtUtc).TotalMinutes > 30);
        Assert.Contains(orders, o => o.Status == DiagnosticOrderStatus.Ordered);
        Assert.Contains(orders, o => o.Status == DiagnosticOrderStatus.InProgress && o.VerificationStatus == DiagnosticVerificationStatus.NotRequired);
        Assert.Contains(orders, o => o.VerificationStatus == DiagnosticVerificationStatus.Pending);
        Assert.Contains(orders, o => o.Status == DiagnosticOrderStatus.Completed && o.VerificationStatus == DiagnosticVerificationStatus.Verified);
    }

    [Fact]
    public async Task OlderPrescriptions_AreDispensedWithMatchingStock_AndOnlyRecentOnesWait()
    {
        var (factory, scope, db, clock) = await SeedAsync();
        using var f = factory; using var s = scope;
        var actor = (await db.Users.SingleAsync(u => u.Email == "pharmacist@cliniccare.local")).Id;
        await MedicineBatchDevelopmentSeed.SeedAsync(db, clock, actor, true);
        var today = clock.VietnamToday;
        var prescriptions = await db.Prescriptions.Include(p => p.Items).ToListAsync();
        DateOnly CreatedOn(DateTime value) => DateOnly.FromDateTime(clock.ConvertUtcToVietnam(value));

        var waiting = prescriptions.Where(p => p.Status == PrescriptionStatus.Issued).ToList();
        Assert.InRange(waiting.Count, 2, 3);
        Assert.All(waiting, p => Assert.True(CreatedOn(p.CreatedAt) >= today.AddDays(-1)));
        Assert.All(prescriptions.Where(p => CreatedOn(p.CreatedAt) < today.AddDays(-1)), p =>
            Assert.Contains(p.Status, new[] { PrescriptionStatus.Dispensed, PrescriptionStatus.Expired }));
        Assert.True(prescriptions.Select(p => p.DoctorId).Distinct().Count() >= 3);

        var transactions = await db.MedicineStockTransactions.ToListAsync();
        Assert.All(prescriptions.Where(p => p.Status == PrescriptionStatus.Expired), p =>
        {
            Assert.True(p.CreatedAt <= clock.UtcNow.AddDays(-5));
            Assert.Null(p.DispensedAt);
            Assert.DoesNotContain(transactions, t => t.PrescriptionId == p.Id);
        });
        foreach (var dispensed in prescriptions.Where(p => p.Status == PrescriptionStatus.Dispensed))
        {
            Assert.NotNull(dispensed.DispensedAt);
            foreach (var item in dispensed.Items)
                Assert.Equal(-item.Quantity, transactions.Where(t => t.PrescriptionId == dispensed.Id && t.MedicineId == item.MedicineId && t.Type == MedicineStockTransactionType.Dispense).Sum(t => t.QuantityChange));
            Assert.All(transactions.Where(t => t.PrescriptionId == dispensed.Id), t => Assert.Equal(dispensed.DispensedAt, t.CreatedAt));
            if (dispensed.PatientVisitId.HasValue)
                Assert.Equal(InvoiceStatus.Paid, (await db.Invoices.SingleAsync(i => i.PatientVisitId == dispensed.PatientVisitId)).Status);
        }
        var batches = await db.MedicineBatches.ToListAsync();
        foreach (var medicine in await db.Medicines.ToListAsync())
        {
            var history = transactions.Where(t => t.MedicineId == medicine.Id).ToList();
            Assert.Equal(medicine.StockQuantity, history.Sum(t => t.QuantityChange));
            Assert.True(batches.Where(b => b.MedicineId == medicine.Id).Sum(b => b.QuantityRemaining) <= medicine.StockQuantity);
            var initial = history.Where(t => t.Type == MedicineStockTransactionType.Initial).Select(t => t.CreatedAt).DefaultIfEmpty(DateTime.MinValue).Min();
            Assert.All(history.Where(t => t.Type == MedicineStockTransactionType.Dispense), t => Assert.True(t.CreatedAt > initial));
        }
    }
}
