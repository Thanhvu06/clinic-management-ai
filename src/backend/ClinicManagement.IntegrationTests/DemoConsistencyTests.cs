using ClinicManagement.Api.Controllers;
using ClinicManagement.Application.Admin.DTOs;
using ClinicManagement.Application.Authentication.Interfaces;
using ClinicManagement.Application.Appointments.Interfaces;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Pharmacy.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Common;
using ClinicManagement.Infrastructure.Persistence;
using ClinicManagement.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;

namespace ClinicManagement.IntegrationTests;

public class DemoConsistencyTests
{
    [Theory]
    [InlineData("2026-10-25T02:00:00Z")]
    [InlineData("2027-01-04T02:00:00Z")]
    [InlineData("2026-10-27T02:00:00Z")]
    public async Task DemoRespectsSpecialtyCodesRevenueAuditAndDoctorOrdering(string utc)
    {
        var clock = new FixedClock(DateTime.Parse(utc, null, System.Globalization.DateTimeStyles.AdjustToUniversal));
        var seedToday = DemoSeedCalendar.NextOpen(clock.VietnamToday);
        var current = new Mock<ICurrentUserService>();
        using var factory = new CustomWebApplicationFactory();
        using var app = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IDateTimeProvider>();
            services.AddSingleton<IDateTimeProvider>(clock);
            services.RemoveAll<ICurrentUserService>();
            services.AddSingleton(current.Object);
        }));
        using var scope = app.Services.CreateScope();
        var services = scope.ServiceProvider;
        await DevelopmentDataSeeder.SeedDoctorSchedulesAndSlotsAsync(DemoSeedCalendar.WithAnchoredClock(services), 30, true);
        var db = services.GetRequiredService<AppDbContext>();
        var patients = await db.Patients.ToDictionaryAsync(p => p.Id);
        var doctors = await db.Doctors.Include(d => d.DoctorSpecialties).ThenInclude(s => s.Specialty).ToDictionaryAsync(d => d.Id);
        void Eligible(long patientId, long doctorId, DateOnly date)
        {
            var patient = patients[patientId];
            var specialty = doctors[doctorId].DoctorSpecialties.Single(s => s.IsPrimary).Specialty.SpecialtyCode;
            if (specialty == "SP02") Assert.True(patient.DateOfBirth > date.AddYears(-16) && patient.DateOfBirth <= date);
            if (specialty == "SP03") Assert.Equal(Gender.Female, patient.Gender);
        }
        var appointments = await db.Appointments.ToListAsync();
        Assert.All(appointments, a => { Assert.Matches(@"^APT-\d{6}-[0-9A-F]{8}$", a.AppointmentCode); Eligible(a.PatientId, a.DoctorId, a.AppointmentDate); });
        Assert.Contains(appointments, a => a.AppointmentDate == seedToday);
        foreach (var visit in await db.PatientVisits.ToListAsync())
        {
            Assert.Matches(@"^VIS-\d{8}-\d{4}$", visit.VisitCode);
            Eligible(visit.PatientId, visit.AssignedDoctorId!.Value, visit.VisitDate);
        }
        foreach (var order in await db.DiagnosticOrders.ToListAsync())
        {
            Assert.Matches(@"^DX-\d{8}-\d{4}$", order.OrderCode);
            Eligible(order.PatientId, order.OrderingDoctorId, DateOnly.FromDateTime(clock.ConvertUtcToVietnam(order.OrderedAtUtc)));
        }
        foreach (var prescription in await db.Prescriptions.ToListAsync())
            Eligible(prescription.PatientId, prescription.DoctorId, DateOnly.FromDateTime(clock.ConvertUtcToVietnam(prescription.CreatedAt)));
        var registrations = await db.HealthPackageRegistrations.Where(r => r.Status != HealthPackageRegistrationStatus.Cancelled).ToListAsync();
        foreach (var group in registrations.GroupBy(r => (r.PatientId, r.HealthPackageId)))
        {
            var dates = group.Select(r => r.PreferredDate).Order().ToList();
            for (var i = 1; i < dates.Count; i++) Assert.True(dates[i].DayNumber - dates[i - 1].DayNumber >= 30);
        }
        Assert.All(await db.HealthPackageRegistrations.ToListAsync(), r => Assert.Matches(@"^REG-\d{6}-[0-9A-F]{8}$", r.RegistrationCode));
        var invoices = await db.Invoices.Include(i => i.Payments).Include(i => i.Items).ToListAsync();
        Assert.All(invoices, i => Assert.Matches(@"^INV-\d{6}-[0-9A-F]{8}$", i.InvoiceCode));
        Assert.All(invoices.Where(i => i.Status == InvoiceStatus.Paid), i =>
        {
            Assert.Equal(i.TotalAmount, i.Items.Where(item => !item.IsCancelled).Sum(item => item.LineTotal));
            Assert.Equal(i.TotalAmount, i.Payments.Where(p => p.Status == PaymentStatus.Succeeded).Sum(p => p.Amount));
        });
        var payments = await db.Payments.ToListAsync();
        Assert.All(payments, p => Assert.Matches(@"^PAY-\d{6}-[0-9A-F]{8}$", p.PaymentCode));
        var daily = payments.GroupBy(p => DateOnly.FromDateTime(clock.ConvertUtcToVietnam(p.ReceivedAtUtc))).ToDictionary(g => g.Key, g => g.Count());
        for (var i = 0; i < 30; i++)
        {
            var date = DemoSeedCalendar.PreviousOpen(clock.VietnamToday).AddDays(-i);
            Assert.InRange(daily.GetValueOrDefault(date), date.DayOfWeek == DayOfWeek.Sunday ? 0 : 2, date.DayOfWeek == DayOfWeek.Sunday ? 0 : 6);
        }
        var counts = Enumerable.Range(0, 30).Select(i => DemoSeedCalendar.PreviousOpen(clock.VietnamToday).AddDays(-i)).Where(d => d.DayOfWeek != DayOfWeek.Sunday).Select(d => daily[d]).ToList();
        Assert.True(counts.Distinct().Count() >= 4);
        Assert.Contains(Enumerable.Range(0, counts.Count - 5), i => counts[i] != counts[i + 5]);
        Assert.DoesNotContain(await db.DoctorWorkSchedules.ToListAsync(), s => s.WorkDate.DayOfWeek == DayOfWeek.Sunday);
        Assert.DoesNotContain(appointments, a => a.AppointmentDate.DayOfWeek == DayOfWeek.Sunday);
        var leaves = await db.DoctorLeaveRequests.Where(l => l.Status == DoctorLeaveRequestStatus.Approved).ToListAsync();
        foreach (var doctor in doctors.Values)
        {
            var upcoming = appointments.Where(a => a.DoctorId == doctor.Id && a.AppointmentDate > clock.VietnamToday && a.AppointmentDate <= clock.VietnamToday.AddDays(7)).ToList();
            Assert.InRange(upcoming.Count, 2, 4);
            Assert.All(upcoming, a => Assert.DoesNotContain(leaves, leave => leave.DoctorId == doctor.Id && DateOnly.FromDateTime(leave.StartDateTime) == a.AppointmentDate));
        }
        var controller = ActivatorUtilities.CreateInstance<AdminController>(services);
        var response = Assert.IsType<OkObjectResult>(await controller.GetAuditLogs(db, clock, null, null, 1, 100));
        var audit = Assert.IsType<ApiResponse<PagedResult<SystemAuditLogDto>>>(response.Value).Data!;
        Assert.Contains(audit.Items, l => l.EntityName == "Payment" && l.Description.Contains(" ₫"));
        Assert.DoesNotContain(audit.Items, l => l.EntityName == "DevelopmentData" || l.Description.StartsWith("Khởi tạo dữ liệu"));
        Assert.All(audit.Items, log => { Assert.NotEmpty(log.ReferenceCode); Assert.False(long.TryParse(log.ReferenceCode, out _)); });
        var notifications = await db.Notifications.Where(n => n.Type == NotificationType.Prescription && n.Title == "Đã cấp thuốc").ToListAsync();
        Assert.NotEmpty(notifications);
        Assert.All(notifications, n => { Assert.Contains("RX-", n.Message); Assert.DoesNotContain("Đơn thuốc #", n.Message); });

        var main = await db.Users.SingleAsync(u => u.Email == "doctor@cliniccare.local");
        current.SetupGet(c => c.UserId).Returns(main.Id);
        var appointmentService = services.GetRequiredService<IDoctorAppointmentService>();
        var ordered = appointments.Where(a => a.DoctorId == doctors.Values.Single(d => d.UserId == main.Id).Id)
            .OrderBy(a => a.AppointmentDate < clock.VietnamToday)
            .ThenBy(a => a.AppointmentDate >= clock.VietnamToday ? a.AppointmentDate : DateOnly.MaxValue)
            .ThenBy(a => a.AppointmentDate >= clock.VietnamToday ? a.StartTime : TimeOnly.MinValue)
            .ThenByDescending(a => a.AppointmentDate).ThenByDescending(a => a.StartTime).ThenBy(a => a.Id).Select(a => a.Id).ToList();
        var first = await appointmentService.GetMyAppointmentsAsync(null, null, null, 1, 3);
        var second = await appointmentService.GetMyAppointmentsAsync(null, null, null, 2, 3);
        Assert.Equal(ordered.Take(6), first.Items.Concat(second.Items).Select(a => a.Id));
        var all = await appointmentService.GetMyAppointmentsAsync(null, null, null, 1, 100);
        Assert.Equal(ordered, all.Items.Select(a => a.Id));

        var pharmacist = await db.Users.SingleAsync(u => u.Email == "pharmacist@cliniccare.local");
        current.SetupGet(c => c.UserId).Returns(pharmacist.Id);
        var rx = await db.Prescriptions.Include(p => p.Items).OrderByDescending(p => p.CreatedAt)
            .FirstAsync(p => p.AppointmentId != null && p.Status == PrescriptionStatus.Dispensed && p.CreatedAt > clock.UtcNow.AddDays(-5));
        rx.Status = PrescriptionStatus.Issued;
        db.Invoices.Add(new Invoice { InvoiceCode = BusinessDisplay.RandomCode("INV", clock.VietnamToday, 7), PatientId = rx.PatientId,
            AppointmentId = rx.AppointmentId, Status = InvoiceStatus.Paid, CreatedByUserId = pharmacist.Id,
            Items = rx.Items.Select(item => new InvoiceItem { ItemCode = "MED", Description = "Thuốc", Quantity = item.Quantity,
                ReferenceType = "PrescriptionItem:v2", ReferenceId = rx.Id * 4294967296L + item.MedicineId }).ToList() });
        await db.SaveChangesAsync();
        await services.GetRequiredService<IPharmacyService>().DispensePrescriptionAsync(rx.Id);
        var notification = await db.Notifications.SingleAsync(n => n.DedupeKey == $"rx_dispensed_{rx.Id}");
        Assert.Contains($"Đơn thuốc {BusinessDisplay.PrescriptionCode(rx, clock)}", notification.Message);
        Assert.DoesNotContain("Đơn thuốc #", notification.Message);
    }

    private sealed class FixedClock(DateTime utc) : IDateTimeProvider
    {
        private readonly DateTimeProvider converter = new();
        public DateTime UtcNow => utc;
        public DateTime VietnamNow => ConvertUtcToVietnam(utc);
        public DateOnly VietnamToday => DateOnly.FromDateTime(VietnamNow);
        public TimeOnly VietnamTime => TimeOnly.FromDateTime(VietnamNow);
        public TimeZoneInfo VietnamTimeZone => converter.VietnamTimeZone;
        public DateTime ConvertUtcToVietnam(DateTime value) => converter.ConvertUtcToVietnam(value);
        public DateTime ConvertVietnamToUtc(DateTime value) => converter.ConvertVietnamToUtc(value);
    }
}
