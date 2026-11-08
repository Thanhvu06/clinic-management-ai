using ClinicManagement.Application.Billing.Interfaces;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using ClinicManagement.Infrastructure.Schedules;
using ClinicManagement.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ClinicManagement.IntegrationTests;

public sealed class BillingQueueAndDemoCleanupTests
{
    [Fact]
    public async Task GetUnbilledVisits_ReturnsPendingInvoiceId_WhenUnpaidInvoiceExists()
    {
        using var fixture = await Fixture.CreateAsync("2026-11-08T08:00:00Z");
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var billing = scope.ServiceProvider.GetRequiredService<IBillingService>();

        var receptionistId = await db.Users
            .Where(u => u.Email == "reception@cliniccare.local")
            .Select(u => u.Id)
            .SingleAsync();
        var doctor = await db.Doctors
            .Include(d => d.DoctorSpecialties)
            .ThenInclude(ds => ds.Specialty)
            .SingleAsync(d => db.Users.Any(u => u.Id == d.UserId && u.Email == "doctor@cliniccare.local"));
        var specialty = doctor.DoctorSpecialties.Single(ds => ds.IsPrimary).Specialty;
        var department = await db.Departments.FirstAsync(d => d.SpecialtyId == specialty.Id && d.IsActive);
        var patients = await db.Patients.OrderBy(p => p.Id).Take(3).ToListAsync();
        var service = await db.DiagnosticServices.FirstAsync(s => s.IsActive && s.Price > 0);

        var visitPendingOnly = new PatientVisit
        {
            VisitCode = "VIS-20261108-9001",
            PatientId = patients[0].Id,
            FacilityId = department.FacilityId,
            DepartmentId = department.Id,
            AssignedDoctorId = doctor.Id,
            VisitDate = fixture.Clock.VietnamToday,
            QueueNumber = 9001,
            Status = VisitStatus.Completed,
            CheckedInAtUtc = fixture.Clock.UtcNow.AddHours(-2),
            CompletedAtUtc = fixture.Clock.UtcNow.AddHours(-1),
            CreatedAtUtc = fixture.Clock.UtcNow.AddHours(-2),
            CreatedByUserId = receptionistId
        };
        var visitMixed = new PatientVisit
        {
            VisitCode = "VIS-20261108-9002",
            PatientId = patients[1].Id,
            FacilityId = department.FacilityId,
            DepartmentId = department.Id,
            AssignedDoctorId = doctor.Id,
            VisitDate = fixture.Clock.VietnamToday,
            QueueNumber = 9002,
            Status = VisitStatus.InBilling,
            CheckedInAtUtc = fixture.Clock.UtcNow.AddHours(-2),
            CreatedAtUtc = fixture.Clock.UtcNow.AddHours(-2),
            CreatedByUserId = receptionistId
        };
        var visitNoInvoice = new PatientVisit
        {
            VisitCode = "VIS-20261108-9003",
            PatientId = patients[2].Id,
            FacilityId = department.FacilityId,
            DepartmentId = department.Id,
            AssignedDoctorId = doctor.Id,
            VisitDate = fixture.Clock.VietnamToday,
            QueueNumber = 9003,
            Status = VisitStatus.InBilling,
            CheckedInAtUtc = fixture.Clock.UtcNow.AddHours(-1),
            CreatedAtUtc = fixture.Clock.UtcNow.AddHours(-1),
            CreatedByUserId = receptionistId
        };
        db.PatientVisits.AddRange(visitPendingOnly, visitMixed, visitNoInvoice);
        await db.SaveChangesAsync();

        var extraOrder = new DiagnosticOrder
        {
            OrderCode = "DX-20261108-9002",
            PatientVisitId = visitMixed.Id,
            PatientId = visitMixed.PatientId,
            OrderingDoctorId = doctor.Id,
            FacilityId = department.FacilityId,
            ClinicalIndication = "Kiểm tra cận lâm sàng phát sinh",
            Status = DiagnosticOrderStatus.Completed,
            OrderedAtUtc = fixture.Clock.UtcNow.AddMinutes(-30),
            Items = [new DiagnosticOrderItem { DiagnosticServiceId = service.Id, Status = DiagnosticItemStatus.Completed }]
        };
        db.DiagnosticOrders.Add(extraOrder);

        var invoice1 = new Invoice
        {
            InvoiceCode = "INV-261108-PEND0001",
            PatientId = visitPendingOnly.PatientId,
            PatientVisitId = visitPendingOnly.Id,
            SourceType = InvoiceSourceType.Appointment,
            Status = InvoiceStatus.Unpaid,
            Subtotal = specialty.ConsultationFee,
            TotalAmount = specialty.ConsultationFee,
            CreatedByUserId = receptionistId,
            CreatedAtUtc = fixture.Clock.UtcNow.AddMinutes(-45),
            Items =
            [
                new InvoiceItem
                {
                    ItemCode = specialty.SpecialtyCode,
                    Description = "Phí khám",
                    Quantity = 1,
                    UnitPrice = specialty.ConsultationFee,
                    LineTotal = specialty.ConsultationFee,
                    ReferenceType = "Consultation",
                    ReferenceId = visitPendingOnly.Id
                }
            ]
        };
        var invoice2 = new Invoice
        {
            InvoiceCode = "INV-261108-PEND0002",
            PatientId = visitMixed.PatientId,
            PatientVisitId = visitMixed.Id,
            SourceType = InvoiceSourceType.Appointment,
            Status = InvoiceStatus.Unpaid,
            Subtotal = specialty.ConsultationFee,
            TotalAmount = specialty.ConsultationFee,
            CreatedByUserId = receptionistId,
            CreatedAtUtc = fixture.Clock.UtcNow.AddMinutes(-40),
            Items =
            [
                new InvoiceItem
                {
                    ItemCode = specialty.SpecialtyCode,
                    Description = "Phí khám",
                    Quantity = 1,
                    UnitPrice = specialty.ConsultationFee,
                    LineTotal = specialty.ConsultationFee,
                    ReferenceType = "Consultation",
                    ReferenceId = visitMixed.Id
                }
            ]
        };
        db.Invoices.AddRange(invoice1, invoice2);
        await db.SaveChangesAsync();

        var page = await billing.GetUnbilledVisitsAsync(department.FacilityId, receptionistId, 1, 20);

        var dtoPendingOnly = Assert.Single(page.Items, x => x.VisitId == visitPendingOnly.Id);
        Assert.Equal(invoice1.Id, dtoPendingOnly.PendingInvoiceId);
        Assert.Equal("INV-261108-PEND0001", dtoPendingOnly.PendingInvoiceCode);
        Assert.False(dtoPendingOnly.HasUnbilledItems);
        Assert.True(dtoPendingOnly.HasPendingPayment);

        var dtoMixed = Assert.Single(page.Items, x => x.VisitId == visitMixed.Id);
        Assert.Equal(invoice2.Id, dtoMixed.PendingInvoiceId);
        Assert.Equal("INV-261108-PEND0002", dtoMixed.PendingInvoiceCode);
        Assert.True(dtoMixed.HasUnbilledItems);
        Assert.True(dtoMixed.HasPendingPayment);

        var dtoNoInvoice = Assert.Single(page.Items, x => x.VisitId == visitNoInvoice.Id);
        Assert.Null(dtoNoInvoice.PendingInvoiceId);
        Assert.Null(dtoNoInvoice.PendingInvoiceCode);
        Assert.True(dtoNoInvoice.HasUnbilledItems);
    }

    [Fact]
    public async Task StaleDemoCleanup_ClosesOnlyOpenRecordsOlderThan48Hours_AndPreservesRecentAndCompleted()
    {
        using var fixture = await Fixture.CreateAsync("2026-11-08T10:00:00Z");
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var receptionistId = await db.Users
            .Where(u => u.Email == "reception@cliniccare.local")
            .Select(u => u.Id)
            .SingleAsync();
        var doctor = await db.Doctors
            .Include(d => d.DoctorSpecialties)
            .ThenInclude(ds => ds.Specialty)
            .SingleAsync(d => db.Users.Any(u => u.Id == d.UserId && u.Email == "doctor@cliniccare.local"));
        var specialty = doctor.DoctorSpecialties.Single(ds => ds.IsPrimary).Specialty;
        var department = await db.Departments.FirstAsync(d => d.SpecialtyId == specialty.Id && d.IsActive);
        var patients = await db.Patients.OrderBy(p => p.Id).Take(6).ToListAsync();
        var service = await db.DiagnosticServices.FirstAsync(s => s.IsActive);
        var medicine = await db.Medicines.FirstAsync(m => m.IsActive);

        var staleTime = fixture.Clock.UtcNow.AddHours(-72);
        var recentTime = fixture.Clock.UtcNow.AddHours(-12);

        var staleSlot = new AppointmentSlot
        {
            DoctorId = doctor.Id,
            SlotDate = DateOnly.FromDateTime(fixture.Clock.ConvertUtcToVietnam(staleTime)),
            StartTime = new TimeOnly(8, 0),
            EndTime = new TimeOnly(8, 30),
            IsBooked = true
        };
        db.AppointmentSlots.Add(staleSlot);
        await db.SaveChangesAsync();

        var staleAppointment = new Appointment
        {
            AppointmentCode = "APT-STALE-0001",
            PatientId = patients[0].Id,
            DoctorId = doctor.Id,
            SpecialtyId = specialty.Id,
            FacilityId = department.FacilityId,
            AppointmentSlotId = staleSlot.Id,
            AppointmentDate = staleSlot.SlotDate,
            StartTime = staleSlot.StartTime,
            EndTime = staleSlot.EndTime,
            Status = AppointmentStatus.CheckedIn,
            Reason = "Khám cũ quá 48 giờ"
        };
        db.Appointments.Add(staleAppointment);
        await db.SaveChangesAsync();

        var staleOpenVisit = new PatientVisit
        {
            VisitCode = "VIS-STALE-OPEN",
            PatientId = patients[0].Id,
            AppointmentId = staleAppointment.Id,
            FacilityId = department.FacilityId,
            DepartmentId = department.Id,
            AssignedDoctorId = doctor.Id,
            VisitDate = staleSlot.SlotDate,
            QueueNumber = 801,
            Status = VisitStatus.WaitingForDiagnostics,
            CheckedInAtUtc = staleTime,
            CreatedAtUtc = staleTime,
            CreatedByUserId = receptionistId
        };
        var recentOpenVisit = new PatientVisit
        {
            VisitCode = "VIS-RECENT-OPEN",
            PatientId = patients[1].Id,
            FacilityId = department.FacilityId,
            DepartmentId = department.Id,
            AssignedDoctorId = doctor.Id,
            VisitDate = fixture.Clock.VietnamToday,
            QueueNumber = 802,
            Status = VisitStatus.WaitingForDiagnostics,
            CheckedInAtUtc = recentTime,
            CreatedAtUtc = recentTime,
            CreatedByUserId = receptionistId
        };
        var staleCompletedVisit = new PatientVisit
        {
            VisitCode = "VIS-STALE-DONE",
            PatientId = patients[2].Id,
            FacilityId = department.FacilityId,
            DepartmentId = department.Id,
            AssignedDoctorId = doctor.Id,
            VisitDate = staleSlot.SlotDate,
            QueueNumber = 803,
            Status = VisitStatus.Completed,
            CheckedInAtUtc = staleTime,
            CompletedAtUtc = staleTime.AddMinutes(30),
            CreatedAtUtc = staleTime,
            CreatedByUserId = receptionistId
        };
        db.PatientVisits.AddRange(staleOpenVisit, recentOpenVisit, staleCompletedVisit);
        await db.SaveChangesAsync();

        var staleOrder = new DiagnosticOrder
        {
            OrderCode = "DX-STALE-2002",
            PatientVisitId = staleOpenVisit.Id,
            PatientId = patients[0].Id,
            OrderingDoctorId = doctor.Id,
            FacilityId = department.FacilityId,
            ClinicalIndication = "Chỉ định cũ",
            Status = DiagnosticOrderStatus.InProgress,
            OrderedAtUtc = staleTime,
            Items = [new DiagnosticOrderItem { DiagnosticServiceId = service.Id, Status = DiagnosticItemStatus.InProgress }]
        };
        var recentOrder = new DiagnosticOrder
        {
            OrderCode = "DX-RECENT-2003",
            PatientVisitId = recentOpenVisit.Id,
            PatientId = patients[1].Id,
            OrderingDoctorId = doctor.Id,
            FacilityId = department.FacilityId,
            ClinicalIndication = "Chỉ định mới",
            Status = DiagnosticOrderStatus.InProgress,
            OrderedAtUtc = recentTime,
            Items = [new DiagnosticOrderItem { DiagnosticServiceId = service.Id, Status = DiagnosticItemStatus.InProgress }]
        };
        var completedOldOrder = new DiagnosticOrder
        {
            OrderCode = "DX-DONE-2004",
            PatientVisitId = staleCompletedVisit.Id,
            PatientId = patients[2].Id,
            OrderingDoctorId = doctor.Id,
            FacilityId = department.FacilityId,
            ClinicalIndication = "Chỉ định đã xong",
            Status = DiagnosticOrderStatus.Completed,
            OrderedAtUtc = staleTime,
            CompletedAtUtc = staleTime.AddMinutes(20),
            Items = [new DiagnosticOrderItem { DiagnosticServiceId = service.Id, Status = DiagnosticItemStatus.Completed }]
        };
        db.DiagnosticOrders.AddRange(staleOrder, recentOrder, completedOldOrder);

        var stalePrescription = new Prescription
        {
            PatientVisitId = staleCompletedVisit.Id,
            PatientId = patients[0].Id,
            DoctorId = doctor.Id,
            Status = PrescriptionStatus.Issued,
            CreatedAt = staleTime,
            Items = [new PrescriptionItem { MedicineId = medicine.Id, Quantity = 2, Dosage = "1 viên", Frequency = "2 lần/ngày", DurationDays = 1 }]
        };
        var recentPrescription = new Prescription
        {
            PatientVisitId = recentOpenVisit.Id,
            PatientId = patients[1].Id,
            DoctorId = doctor.Id,
            Status = PrescriptionStatus.Issued,
            CreatedAt = recentTime,
            Items = [new PrescriptionItem { MedicineId = medicine.Id, Quantity = 2, Dosage = "1 viên", Frequency = "2 lần/ngày", DurationDays = 1 }]
        };
        var dispensedOldPrescription = new Prescription
        {
            PatientVisitId = staleCompletedVisit.Id,
            PatientId = patients[2].Id,
            DoctorId = doctor.Id,
            Status = PrescriptionStatus.Dispensed,
            CreatedAt = staleTime,
            DispensedAt = staleTime.AddMinutes(30),
            Items = [new PrescriptionItem { MedicineId = medicine.Id, Quantity = 2, Dosage = "1 viên", Frequency = "2 lần/ngày", DurationDays = 1 }]
        };
        db.Prescriptions.AddRange(stalePrescription, recentPrescription, dispensedOldPrescription);

        var staleUnpaidInvoice = new Invoice
        {
            InvoiceCode = "INV-STALE-UNPAID",
            PatientId = patients[0].Id,
            PatientVisitId = staleCompletedVisit.Id,
            SourceType = InvoiceSourceType.Appointment,
            Status = InvoiceStatus.Unpaid,
            Subtotal = 150000m,
            TotalAmount = 150000m,
            CreatedByUserId = receptionistId,
            CreatedAtUtc = staleTime,
            Items =
            [
                new InvoiceItem
                {
                    ItemCode = "SP01",
                    Description = "Phí khám cũ",
                    Quantity = 1,
                    UnitPrice = 150000m,
                    LineTotal = 150000m,
                    ReferenceType = "Consultation",
                    ReferenceId = staleCompletedVisit.Id
                }
            ]
        };
        var recentUnpaidInvoice = new Invoice
        {
            InvoiceCode = "INV-RECENT-UNPAID",
            PatientId = patients[1].Id,
            PatientVisitId = recentOpenVisit.Id,
            SourceType = InvoiceSourceType.Appointment,
            Status = InvoiceStatus.Unpaid,
            Subtotal = 150000m,
            TotalAmount = 150000m,
            CreatedByUserId = receptionistId,
            CreatedAtUtc = recentTime
        };
        var paidOldInvoice = new Invoice
        {
            InvoiceCode = "INV-OLD-PAID",
            PatientId = patients[2].Id,
            PatientVisitId = staleCompletedVisit.Id,
            SourceType = InvoiceSourceType.Appointment,
            Status = InvoiceStatus.Paid,
            Subtotal = 150000m,
            TotalAmount = 150000m,
            CreatedByUserId = receptionistId,
            PaidByUserId = receptionistId,
            CreatedAtUtc = staleTime,
            PaidAtUtc = staleTime.AddMinutes(15)
        };
        db.Invoices.AddRange(staleUnpaidInvoice, recentUnpaidInvoice, paidOldInvoice);
        await db.SaveChangesAsync();

        var scopeFactory = fixture.Factory.Services.GetRequiredService<IServiceScopeFactory>();
        using (var nonDevWorker = new DevelopmentDemoTopUpWorker(
            scopeFactory,
            new EnvironmentStub("Production"),
            Options.Create(new DemoTopUpOptions { Enabled = true, StaleAfterHours = 48 }),
            NullLogger<DevelopmentDemoTopUpWorker>.Instance))
        {
            await nonDevWorker.RunOnceAsync();
        }
        using (var disabledWorker = new DevelopmentDemoTopUpWorker(
            scopeFactory,
            new EnvironmentStub("Development"),
            Options.Create(new DemoTopUpOptions { Enabled = false, StaleAfterHours = 48 }),
            NullLogger<DevelopmentDemoTopUpWorker>.Instance))
        {
            await disabledWorker.RunOnceAsync();
        }

        db.ChangeTracker.Clear();
        Assert.Equal(DiagnosticOrderStatus.InProgress, (await db.DiagnosticOrders.FindAsync(staleOrder.Id))!.Status);
        Assert.Equal(PrescriptionStatus.Issued, (await db.Prescriptions.FindAsync(stalePrescription.Id))!.Status);
        Assert.Equal(InvoiceStatus.Unpaid, (await db.Invoices.FindAsync(staleUnpaidInvoice.Id))!.Status);
        Assert.Equal(VisitStatus.WaitingForDiagnostics, (await db.PatientVisits.FindAsync(staleOpenVisit.Id))!.Status);

        await fixture.Worker.RunOnceAsync();
        db.ChangeTracker.Clear();

        var reloadedStaleOrder = await db.DiagnosticOrders.Include(o => o.Items).SingleAsync(o => o.Id == staleOrder.Id);
        Assert.Equal(DiagnosticOrderStatus.Cancelled, reloadedStaleOrder.Status);
        Assert.Contains("Dọn dữ liệu demo cũ", reloadedStaleOrder.Note);
        Assert.All(reloadedStaleOrder.Items, item => Assert.Equal(DiagnosticItemStatus.Cancelled, item.Status));

        var reloadedStalePrescription = await db.Prescriptions.SingleAsync(p => p.Id == stalePrescription.Id);
        Assert.Equal(PrescriptionStatus.Expired, reloadedStalePrescription.Status);

        var reloadedStaleInvoice = await db.Invoices.Include(i => i.Items).SingleAsync(i => i.Id == staleUnpaidInvoice.Id);
        Assert.Equal(InvoiceStatus.Unpaid, reloadedStaleInvoice.Status);
        Assert.Null(reloadedStaleInvoice.CancelledAtUtc);
        Assert.Null(reloadedStaleInvoice.CancellationReason);
        Assert.All(reloadedStaleInvoice.Items, item => Assert.False(item.IsCancelled));

        var reloadedStaleVisit = await db.PatientVisits.SingleAsync(v => v.Id == staleOpenVisit.Id);
        Assert.Equal(VisitStatus.Cancelled, reloadedStaleVisit.Status);
        Assert.Equal("Dọn dữ liệu demo cũ", reloadedStaleVisit.CancellationReason);

        var reloadedStaleAppointment = await db.Appointments.Include(a => a.Histories).SingleAsync(a => a.Id == staleAppointment.Id);
        Assert.Equal(AppointmentStatus.Cancelled, reloadedStaleAppointment.Status);
        Assert.Contains(reloadedStaleAppointment.Histories, h => h.Note == "Dọn dữ liệu demo cũ" && h.NewStatus == AppointmentStatus.Cancelled);

        Assert.Equal(DiagnosticOrderStatus.InProgress, (await db.DiagnosticOrders.FindAsync(recentOrder.Id))!.Status);
        Assert.Equal(DiagnosticOrderStatus.Completed, (await db.DiagnosticOrders.FindAsync(completedOldOrder.Id))!.Status);
        Assert.Equal(PrescriptionStatus.Issued, (await db.Prescriptions.FindAsync(recentPrescription.Id))!.Status);
        Assert.Equal(PrescriptionStatus.Dispensed, (await db.Prescriptions.FindAsync(dispensedOldPrescription.Id))!.Status);
        Assert.Equal(InvoiceStatus.Unpaid, (await db.Invoices.FindAsync(recentUnpaidInvoice.Id))!.Status);
        Assert.Equal(InvoiceStatus.Paid, (await db.Invoices.FindAsync(paidOldInvoice.Id))!.Status);
        Assert.Equal(VisitStatus.WaitingForDiagnostics, (await db.PatientVisits.FindAsync(recentOpenVisit.Id))!.Status);
        Assert.Equal(VisitStatus.Completed, (await db.PatientVisits.FindAsync(staleCompletedVisit.Id))!.Status);

        var logs = await db.SystemAuditLogs.Where(l => l.Action == "AutomaticExpiry").ToListAsync();
        Assert.Contains(logs, l => l.EntityName == nameof(DiagnosticOrder) && l.EntityId == staleOrder.Id.ToString() && l.Description.Contains("Dọn dữ liệu demo cũ"));
        Assert.Contains(logs, l => l.EntityName == nameof(Prescription) && l.EntityId == stalePrescription.Id.ToString() && l.Description.Contains("Dọn dữ liệu demo cũ"));
        Assert.DoesNotContain(logs, l => l.EntityName == nameof(Invoice) && l.EntityId == staleUnpaidInvoice.Id.ToString());
        Assert.Contains(logs, l => l.EntityName == nameof(PatientVisit) && l.EntityId == staleOpenVisit.Id.ToString() && l.Description.Contains("Dọn dữ liệu demo cũ"));
    }

    private sealed class Fixture(WebApplicationFactory<Program> factory, MutableClock clock, CustomWebApplicationFactory parent) : IDisposable
    {
        public WebApplicationFactory<Program> Factory { get; } = factory;
        public MutableClock Clock { get; } = clock;
        public DevelopmentDemoTopUpWorker Worker { get; } = new(
            factory.Services.GetRequiredService<IServiceScopeFactory>(),
            new EnvironmentStub("Development"),
            Options.Create(new DemoTopUpOptions { Enabled = true, StaleAfterHours = 48 }),
            NullLogger<DevelopmentDemoTopUpWorker>.Instance);

        public static async Task<Fixture> CreateAsync(string utc)
        {
            var clock = new MutableClock(DateTime.Parse(utc).ToUniversalTime());
            var parent = new FixedSeedWebApplicationFactory();
            var factory = parent.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IDateTimeProvider>(clock);
                services.Configure<ClinicOperatingHoursOptions>(x => x.Mode = "TwentyFourSeven");
            }));
            using var scope = factory.Services.CreateScope();
            await DevelopmentDataSeeder.SeedMasterDataAsync(scope.ServiceProvider);
            return new Fixture(factory, clock, parent);
        }

        public void Dispose()
        {
            Worker.Dispose();
            Factory.Dispose();
            parent.Dispose();
        }
    }

    private sealed class MutableClock(DateTime utc) : IDateTimeProvider
    {
        private readonly DateTimeProvider converter = new();
        public DateTime Utc { get; set; } = utc;
        public DateTime UtcNow => Utc;
        public DateTime VietnamNow => ConvertUtcToVietnam(Utc);
        public DateOnly VietnamToday => DateOnly.FromDateTime(VietnamNow);
        public TimeOnly VietnamTime => TimeOnly.FromDateTime(VietnamNow);
        public TimeZoneInfo VietnamTimeZone => converter.VietnamTimeZone;
        public DateTime ConvertUtcToVietnam(DateTime value) => converter.ConvertUtcToVietnam(value);
        public DateTime ConvertVietnamToUtc(DateTime value) => converter.ConvertVietnamToUtc(value);
    }

    private sealed class EnvironmentStub(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "BillingQueueAndDemoCleanupTests";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
