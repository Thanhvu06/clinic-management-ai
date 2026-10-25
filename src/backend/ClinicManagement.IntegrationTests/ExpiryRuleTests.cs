using ClinicManagement.Application.Common.Expiry;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Expiry;
using ClinicManagement.Infrastructure.Identity;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ClinicManagement.IntegrationTests;

public sealed class ExpiryRuleTests
{
    [Theory]
    [InlineData("pending", -1, false)] [InlineData("pending", 0, true)] [InlineData("pending", 1, true)]
    [InlineData("confirmed", -1, false)] [InlineData("confirmed", 0, true)] [InlineData("confirmed", 1, true)]
    [InlineData("visit", -1, false)] [InlineData("visit", 0, true)] [InlineData("visit", 1, true)]
    [InlineData("diagnostic", -1, false)] [InlineData("diagnostic", 0, true)] [InlineData("diagnostic", 1, true)]
    [InlineData("prescription", -1, false)] [InlineData("prescription", 0, true)] [InlineData("prescription", 1, true)]
    [InlineData("package", -1, false)] [InlineData("package", 0, true)] [InlineData("package", 1, true)]
    [InlineData("change", -1, false)] [InlineData("change", 0, true)] [InlineData("change", 1, true)]
    [InlineData("revisit", -1, false)] [InlineData("revisit", 0, true)] [InlineData("revisit", 1, true)]
    [InlineData("leave", -1, false)] [InlineData("leave", 0, true)] [InlineData("leave", 1, true)]
    public async Task Boundary_is_atomic_idempotent_and_notifies_expected_actors(string kind, int seconds, bool expires)
    {
        await using var f = await Fixture.CreateAsync();
        var due = new DateTime(2026, 10, 20, 9, 0, 0);
        if (kind is "visit" or "package" or "revisit" or "leave") due = due.Date;
        var a = f.Appointment(kind == "confirmed" ? AppointmentStatus.Confirmed : AppointmentStatus.Pending);
        a.AppointmentDate = DateOnly.FromDateTime(due);
        a.StartTime = TimeOnly.FromDateTime(kind == "confirmed" ? due.AddMinutes(-30) : due);
        a.EndTime = a.StartTime.AddMinutes(30);
        object subject = a;
        IExpiryRule rule;
        switch (kind)
        {
            case "pending": rule = new PendingAppointmentExpiryRule(f.Db, f.Clock, f.Options); break;
            case "confirmed": rule = new ConfirmedAppointmentExpiryRule(f.Db, f.Clock, f.Options); break;
            case "visit":
                subject = f.Visit(VisitStatus.WaitingForDoctor, DateOnly.FromDateTime(due.AddDays(-1)));
                rule = new WaitingVisitExpiryRule(f.Db, f.Clock, f.Options); break;
            case "diagnostic":
                subject = f.Order(f.Clock.ConvertVietnamToUtc(due.AddHours(-24)));
                rule = new DiagnosticOrderExpiryRule(f.Db, f.Clock, f.Options); break;
            case "prescription":
                subject = new Prescription { PatientId = 1, DoctorId = 1, Appointment = a, Status = PrescriptionStatus.Issued, CreatedAt = f.Clock.ConvertVietnamToUtc(due.AddDays(-5)) };
                f.Db.Add(subject);
                rule = new PrescriptionExpiryRule(f.Db, f.Clock, f.Options); break;
            case "package":
                subject = new HealthPackageRegistration { PatientId = 1, HealthPackageId = 1, RegistrationCode = "PKG-expiry", ContactPhone = "0900000000", PreferredDate = DateOnly.FromDateTime(due.AddDays(-1)) };
                f.Db.Add(subject);
                rule = new PackageRegistrationExpiryRule(f.Db, f.Clock, f.Options); break;
            case "change":
                a.Status = AppointmentStatus.PendingCancellation;
                subject = new AppointmentChangeRequest { Appointment = a, RequestedByUserId = f.PatientUser, OriginalAppointmentStatus = AppointmentStatus.Pending, RequestType = AppointmentChangeRequestType.Cancellation };
                f.Db.Add(subject);
                rule = new ChangeRequestExpiryRule(f.Db, f.Clock, f.Options); break;
            case "revisit":
                a.Status = AppointmentStatus.Completed;
                subject = new RevisitRequest { OriginalAppointment = a, DoctorId = 1, PatientId = 1, SuggestedDate = DateOnly.FromDateTime(due.AddDays(-7)) };
                f.Db.Add(subject);
                rule = new RevisitExpiryRule(f.Db, f.Clock, f.Options); break;
            default:
                subject = new DoctorLeaveRequest { DoctorId = 1, StartDateTime = due.AddDays(-1).AddHours(7), EndDateTime = due.AddDays(-1).AddHours(11), Reason = "Nghỉ phép" };
                f.Db.Add(subject);
                rule = new DoctorLeaveExpiryRule(f.Db, f.Clock, f.Options); break;
        }
        await f.Db.SaveChangesAsync();
        var id = (long)f.Db.Entry(subject).Property("Id").CurrentValue!;
        f.Db.ChangeTracker.Clear();
        f.Clock.Now = due.AddSeconds(seconds);
        Assert.Equal(expires ? 1 : 0, await rule.RunAsync());
        Assert.Equal(0, await rule.RunAsync());
        var audit = await f.Db.SystemAuditLogs.ToListAsync();
        Assert.Equal(expires ? kind == "change" ? 2 : 1 : 0, audit.Count);
        Assert.All(audit, log => { Assert.Null(log.UserId); Assert.Contains("Hệ thống", log.Description); });
        var notifications = await f.Db.Notifications.ToListAsync();
        if (!expires) { Assert.Empty(notifications); return; }
        var expected = kind switch
        {
            "pending" or "confirmed" or "visit" or "change" => new[] { f.PatientUser, f.DoctorUser, f.ReceptionUser },
            "diagnostic" or "leave" => [f.DoctorUser],
            "revisit" => [f.PatientUser, f.DoctorUser],
            _ => [f.PatientUser]
        };
        if (kind == "change") expected = [f.PatientUser, f.ReceptionUser];
        Assert.Equal(expected.Order(), notifications.Select(n => n.UserId).Order());
        Assert.All(notifications, n => Assert.Equal(f.Clock.UtcNow, n.CreatedAtUtc));
        if (kind == "package") Assert.All(notifications, n => Assert.Equal("/patient/health-package-registrations", n.Route));
        if (kind == "change") Assert.Equal("/reception/change-requests", notifications.Single(n => n.UserId == f.ReceptionUser).Route);
        Assert.All(await f.Db.AppointmentHistories.ToListAsync(), h => Assert.Null(h.PerformedByUserId));
        var entity = await f.Db.FindAsync(subject.GetType(), id);
        var status = f.Db.Entry(entity!).Property("Status").CurrentValue!.ToString();
        Assert.Equal(kind switch
        {
            "pending" or "diagnostic" or "package" => "Cancelled", "confirmed" or "visit" => "NoShow",
            "change" or "revisit" or "leave" or "prescription" => "Expired", _ => ""
        }, status);
        if (kind is "pending" or "confirmed") Assert.False((await f.Db.AppointmentSlots.SingleAsync()).IsBooked);
        if (kind == "prescription") Assert.Empty(await f.Db.MedicineStockTransactions.ToListAsync());
    }

    [Theory]
    [InlineData("InProgress")] [InlineData("Completed")] [InlineData("result")]
    [InlineData("collected")] [InlineData("received")] [InlineData("started")]
    public async Task Diagnostic_with_work_or_results_is_never_cancelled(string protection)
    {
        await using var f = await Fixture.CreateAsync();
        var order = f.Order(f.Clock.UtcNow.AddDays(-10));
        var item = order.Items.Single();
        if (protection is "InProgress" or "Completed") order.Status = Enum.Parse<DiagnosticOrderStatus>(protection);
        if (protection == "result") item.Result = new DiagnosticResult { ResultText = "Đã có kết quả", ResultedByUserId = f.DoctorUser };
        if (protection == "collected") { item.CollectedAtUtc = f.Clock.UtcNow.AddDays(-9); item.SpecimenStatus = SpecimenStatus.Collected; }
        if (protection == "received") item.ReceivedAtUtc = f.Clock.UtcNow.AddDays(-9);
        if (protection == "started") order.StartedAtUtc = f.Clock.UtcNow.AddDays(-9);
        await f.Db.SaveChangesAsync();
        Assert.Equal(0, await new DiagnosticOrderExpiryRule(f.Db, f.Clock, f.Options).RunAsync());
        Assert.Empty(await f.Db.SystemAuditLogs.ToListAsync());
    }

    [Fact]
    public async Task Checked_in_appointment_and_final_states_are_preserved_and_batches_release_slots()
    {
        await using var f = await Fixture.CreateAsync();
        foreach (var status in Enum.GetValues<AppointmentStatus>().Where(s => s != AppointmentStatus.Pending && s != AppointmentStatus.Confirmed))
            f.Appointment(status);
        var checkedIn = f.Appointment(AppointmentStatus.Confirmed);
        var visit = f.Visit(VisitStatus.CheckedIn, f.Clock.VietnamToday);
        visit.Appointment = checkedIn;
        for (var i = 0; i < 5; i++) f.Appointment(AppointmentStatus.Pending);
        await f.Db.SaveChangesAsync();
        Assert.Equal(5, await new PendingAppointmentExpiryRule(f.Db, f.Clock, f.Options).RunAsync());
        Assert.Equal(0, await new ConfirmedAppointmentExpiryRule(f.Db, f.Clock, f.Options).RunAsync());
        Assert.Equal(5, await f.Db.AppointmentSlots.CountAsync(s => !s.IsBooked));
        Assert.Equal(5, await f.Db.SystemAuditLogs.CountAsync());
        Assert.Equal(AppointmentStatus.Confirmed, (await f.Db.Appointments.FindAsync(checkedIn.Id))!.Status);
    }

    [Fact]
    public async Task Expired_change_restores_original_then_applies_appointment_expiry()
    {
        await using var f = await Fixture.CreateAsync();
        foreach (var original in new[] { AppointmentStatus.Pending, AppointmentStatus.Confirmed })
        {
            var a = f.Appointment(AppointmentStatus.PendingReschedule);
            f.Db.AppointmentChangeRequests.Add(new AppointmentChangeRequest { Appointment = a, OriginalAppointmentStatus = original, RequestedByUserId = f.PatientUser });
        }
        await f.Db.SaveChangesAsync();
        Assert.Equal(2, await new ChangeRequestExpiryRule(f.Db, f.Clock, f.Options).RunAsync());
        Assert.Equal(1, await new PendingAppointmentExpiryRule(f.Db, f.Clock, f.Options).RunAsync());
        Assert.Equal(1, await new ConfirmedAppointmentExpiryRule(f.Db, f.Clock, f.Options).RunAsync());
        Assert.All(await f.Db.AppointmentSlots.ToListAsync(), s => Assert.False(s.IsBooked));
        Assert.Equal(6, await f.Db.SystemAuditLogs.CountAsync());
    }

    [Theory]
    [InlineData(-1, true)] [InlineData(0, false)] [InlineData(1, false)]
    public void No_show_block_expires_at_30_days_and_fourth_does_not_extend_it(int seconds, bool blocked)
    {
        var third = new DateTime(2026, 10, 1, 8, 0, 0);
        DateTime[] events = [third.AddDays(-89), third.AddDays(-2), third, third.AddDays(1)];
        var until = NoShowRestrictionRule.CalculateBlockedUntil(events, third.AddDays(30).AddSeconds(seconds), new());
        Assert.Equal(blocked, until.HasValue);
        if (blocked) Assert.Equal(third.AddDays(30), until);
        Assert.Null(NoShowRestrictionRule.CalculateBlockedUntil([third.AddDays(-91), third.AddDays(-1), third], third, new()));
    }

    [Theory]
    [InlineData(-1, false)] [InlineData(0, true)] [InlineData(1, true)]
    public void Third_no_show_starts_the_block_at_the_threshold(int seconds, bool blocked)
    {
        var third = new DateTime(2026, 10, 20, 9, 0, 0);
        Assert.Equal(blocked, NoShowRestrictionRule.CalculateBlockedUntil([third.AddDays(-90), third.AddDays(-1), third], third.AddSeconds(seconds), new()).HasValue);
    }

    [Fact]
    public async Task Final_and_other_active_states_are_not_changed_by_expiry()
    {
        await using var f = await Fixture.CreateAsync();
        foreach (var status in new[] { VisitStatus.InConsultation, VisitStatus.InDiagnostics, VisitStatus.ResultsReady, VisitStatus.Completed, VisitStatus.Cancelled, VisitStatus.NoShow })
        {
            var visit = f.Visit(status, f.Clock.VietnamToday.AddDays(-10));
            visit.QueueNumber = (int)status;
        }
        foreach (var status in new[] { PrescriptionStatus.Draft, PrescriptionStatus.Dispensed, PrescriptionStatus.Cancelled, PrescriptionStatus.ReservedForPurchase, PrescriptionStatus.Expired })
            f.Db.Prescriptions.Add(new Prescription { PatientId = 1, DoctorId = 1, Appointment = f.Appointment(AppointmentStatus.Completed), Status = status, CreatedAt = f.Clock.UtcNow.AddDays(-10) });
        foreach (var status in new[] { HealthPackageRegistrationStatus.Confirmed, HealthPackageRegistrationStatus.Cancelled })
            f.Db.HealthPackageRegistrations.Add(new HealthPackageRegistration { PatientId = 1, HealthPackageId = 1, RegistrationCode = "PKG-" + status, ContactPhone = "0900000000", PreferredDate = f.Clock.VietnamToday.AddDays(-10), Status = status });
        foreach (var status in Enum.GetValues<RevisitRequestStatus>().Where(s => s != RevisitRequestStatus.PendingPatientResponse))
            f.Db.RevisitRequests.Add(new RevisitRequest { OriginalAppointment = f.Appointment(AppointmentStatus.Completed), PatientId = 1, DoctorId = 1, SuggestedDate = f.Clock.VietnamToday.AddDays(-10), Status = status });
        foreach (var status in Enum.GetValues<AppointmentChangeRequestStatus>().Where(s => s != AppointmentChangeRequestStatus.Pending))
            f.Db.AppointmentChangeRequests.Add(new AppointmentChangeRequest { Appointment = f.Appointment(AppointmentStatus.Completed), RequestedByUserId = f.PatientUser, Status = status });
        foreach (var status in Enum.GetValues<DoctorLeaveRequestStatus>().Where(s => s != DoctorLeaveRequestStatus.Pending))
            f.Db.DoctorLeaveRequests.Add(new DoctorLeaveRequest { DoctorId = 1, StartDateTime = f.Clock.VietnamNow.AddDays(-10), EndDateTime = f.Clock.VietnamNow.AddDays(-9), Reason = "Nghỉ phép", Status = status });
        await f.Db.SaveChangesAsync();
        foreach (var type in ExpiryWorker.RuleTypes)
        {
            var rule = (IExpiryRule)Activator.CreateInstance(type, f.Db, f.Clock, f.Options)!;
            Assert.Equal(0, await rule.RunAsync());
            Assert.Equal(0, await rule.RunAsync());
        }
        Assert.Empty(await f.Db.SystemAuditLogs.ToListAsync());
        Assert.Empty(await f.Db.Notifications.ToListAsync());
    }

    [Fact]
    public async Task Failed_batch_rolls_back_status_history_audit_and_notification_together()
    {
        await using var f = await Fixture.CreateAsync();
        var appointment = f.Appointment(AppointmentStatus.Pending);
        await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new RollbackRule(f.Db, f.Clock, f.Options).RunAsync());
        f.Db.ChangeTracker.Clear();
        Assert.Equal(AppointmentStatus.Pending, (await f.Db.Appointments.FindAsync(appointment.Id))!.Status);
        Assert.True((await f.Db.AppointmentSlots.SingleAsync()).IsBooked);
        Assert.Empty(await f.Db.SystemAuditLogs.ToListAsync());
        Assert.Empty(await f.Db.AppointmentHistories.ToListAsync());
        Assert.Empty(await f.Db.Notifications.ToListAsync());
    }

    [Fact]
    public async Task Arrival_today_is_preserved_when_legacy_visit_date_is_in_the_past()
    {
        await using var f = await Fixture.CreateAsync();
        var visit = f.Visit(VisitStatus.CheckedIn, f.Clock.VietnamToday.AddDays(-1));
        visit.CheckedInAtUtc = f.Clock.UtcNow;
        await f.Db.SaveChangesAsync();
        Assert.Equal(0, await new WaitingVisitExpiryRule(f.Db, f.Clock, f.Options).RunAsync());
        Assert.Empty(await f.Db.SystemAuditLogs.ToListAsync());
    }

    private sealed class RollbackRule(AppDbContext db, IDateTimeProvider clock, IOptions<ExpiryRulesOptions> options) : ExpiryRule(db, clock, options)
    {
        protected override async Task<int> ApplyBatchAsync(CancellationToken ct)
        {
            await ChangeAppointmentAsync(await Db.Appointments.FirstAsync(ct), AppointmentStatus.Cancelled, "Quá hạn xác nhận", ct);
            await Db.SaveChangesAsync(ct);
            throw new InvalidOperationException("Lỗi sau khi lưu lô");
        }
    }

    [Theory]
    [InlineData(-1, false)] [InlineData(0, true)] [InlineData(1, true)]
    public async Task Calculated_flags_do_not_change_consultation_or_unpaid_invoice(int seconds, bool overdue)
    {
        await using var f = await Fixture.CreateAsync();
        var midnight = new DateTime(2026, 10, 20);
        f.Clock.Now = midnight.AddSeconds(seconds);
        var visitDate = DateOnly.FromDateTime(midnight.AddDays(-1));
        Assert.Equal(overdue, ConsultationCompletionExpiryRule.IsOverdue("InConsultation", visitDate, f.Clock));
        Assert.False(ConsultationCompletionExpiryRule.IsOverdue("Completed", visitDate, f.Clock));
        var invoice = new Invoice { Status = InvoiceStatus.Unpaid, CreatedAtUtc = f.Clock.ConvertVietnamToUtc(midnight.AddDays(-3)) };
        Assert.Equal(overdue, UnpaidInvoiceExpiryRule.IsOverdue(invoice, f.Clock, f.Options.Value));
        Assert.Equal(InvoiceStatus.Unpaid, invoice.Status);
        invoice.Status = InvoiceStatus.Paid;
        Assert.False(UnpaidInvoiceExpiryRule.IsOverdue(invoice, f.Clock, f.Options.Value));
        var visit = f.Visit(VisitStatus.InConsultation, visitDate);
        await f.Db.SaveChangesAsync();
        Assert.Equal(0, await new WaitingVisitExpiryRule(f.Db, f.Clock, f.Options).RunAsync());
        Assert.Equal(VisitStatus.InConsultation, (await f.Db.PatientVisits.FindAsync(visit.Id))!.Status);
    }

    internal sealed class FakeClock : IDateTimeProvider
    {
        public DateTime Now { get; set; } = new(2026, 10, 20, 10, 0, 0);
        public DateTime UtcNow => ConvertVietnamToUtc(Now);
        public DateTime VietnamNow => Now;
        public DateOnly VietnamToday => DateOnly.FromDateTime(Now);
        public TimeOnly VietnamTime => TimeOnly.FromDateTime(Now);
        public TimeZoneInfo VietnamTimeZone => TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
        public DateTime ConvertUtcToVietnam(DateTime time) => DateTime.SpecifyKind(time, DateTimeKind.Unspecified).AddHours(7);
        public DateTime ConvertVietnamToUtc(DateTime time) => DateTime.SpecifyKind(time.AddHours(-7), DateTimeKind.Utc);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        public AppDbContext Db { get; }
        public FakeClock Clock { get; } = new();
        public IOptions<ExpiryRulesOptions> Options { get; } = Microsoft.Extensions.Options.Options.Create(new ExpiryRulesOptions { BatchSize = 2 });
        public Guid PatientUser { get; } = Guid.NewGuid();
        public Guid DoctorUser { get; } = Guid.NewGuid();
        public Guid ReceptionUser { get; } = Guid.NewGuid();
        private Fixture(SqliteConnection connection, AppDbContext db) { this.connection = connection; Db = db; }
        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            var f = new Fixture(connection, db);
            foreach (var id in new[] { f.PatientUser, f.DoctorUser, f.ReceptionUser })
                db.Users.Add(new ApplicationUser { Id = id, UserName = id.ToString(), Email = id.ToString()+"@test.invalid", PhoneNumber = "090" + (id == f.PatientUser ? "0000001" : id == f.DoctorUser ? "0000002" : "0000003"), FullName = "Người dùng", IsActive = true });
            var role = new IdentityRole<Guid> { Id = Guid.NewGuid(), Name = "Receptionist" };
            db.Roles.Add(role);
            db.UserRoles.Add(new IdentityUserRole<Guid> { RoleId = role.Id, UserId = f.ReceptionUser });
            db.Patients.Add(new Patient { Id = 1, UserId = f.PatientUser, MedicalRecordNumber = "MRN-expiry", FullName = "Bệnh nhân" });
            db.Doctors.Add(new Doctor { Id = 1, UserId = f.DoctorUser, IsActive = true });
            db.Specialties.Add(new Specialty { Id = 1, SpecialtyCode = "EXP", Name = "Chuyên khoa" });
            db.Facilities.Add(new Facility { Id = 1, Code = "EXP", Name = "Cơ sở" });
            db.Departments.Add(new Department { Id = 1, FacilityId = 1, Code = "EXP", Name = "Khoa" });
            db.HealthPackages.Add(new HealthPackage { Id = 1, Code = "EXP", Name = "Gói" });
            db.DiagnosticServices.Add(new DiagnosticService { Id = 1, Code = "EXP", Name = "Xét nghiệm" });
            db.StaffFacilityAssignments.Add(new StaffFacilityAssignment { UserId = f.ReceptionUser, FacilityId = 1, Role = "Receptionist" });
            await db.SaveChangesAsync();
            return f;
        }
        public Appointment Appointment(AppointmentStatus status)
        {
            var a = new Appointment
            {
                AppointmentCode = "APT-" + Guid.NewGuid().ToString("N"), PatientId = 1, DoctorId = 1, SpecialtyId = 1,
                AppointmentDate = Clock.VietnamToday.AddDays(-1), StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(8, 15),
                Status = status, AppointmentSlot = new AppointmentSlot { DoctorId = 1, SlotDate = Clock.VietnamToday.AddDays(-1), StartTime = new TimeOnly(8, Db.Appointments.Local.Count), EndTime = new TimeOnly(8, 30), IsBooked = true }
            };
            Db.Appointments.Add(a);
            return a;
        }
        public PatientVisit Visit(VisitStatus status, DateOnly date)
        {
            var v = new PatientVisit { VisitCode = "VIS-" + Guid.NewGuid().ToString("N"), PatientId = 1, FacilityId = 1, DepartmentId = 1, AssignedDoctorId = 1, VisitDate = date, Status = status, CreatedByUserId = ReceptionUser, CheckedInAtUtc = Clock.ConvertVietnamToUtc(date.ToDateTime(new TimeOnly(9, 0))) };
            Db.PatientVisits.Add(v);
            return v;
        }
        public DiagnosticOrder Order(DateTime at)
        {
            var o = new DiagnosticOrder { OrderCode = "DX-expiry", PatientId = 1, OrderingDoctorId = 1, OrderedAtUtc = at, Items = [new DiagnosticOrderItem { DiagnosticServiceId = 1 }] };
            Db.DiagnosticOrders.Add(o);
            return o;
        }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await connection.DisposeAsync(); }
    }
}
