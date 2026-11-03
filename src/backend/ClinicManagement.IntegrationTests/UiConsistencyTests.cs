using System.Net.Http.Json;
using System.Text.Json;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using ClinicManagement.Infrastructure.Schedules;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicManagement.IntegrationTests;

public sealed class UiConsistencyTests(CustomWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task Reception_cashier_count_matches_unbilled_queue_with_a_no_show()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var today = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>().VietnamToday;
        var department = await db.Departments.Include(d => d.Specialty).FirstAsync(d => d.SpecialtyId == SpecialtyEntityId);
        department.Specialty!.ConsultationFee = 100_000;
        var visits = new[] { VisitStatus.InBilling, VisitStatus.NoShow }.Select((status, index) => new PatientVisit
        {
            VisitCode = $"VIS-{Guid.NewGuid():N}"[..20],
            PatientId = Patient1EntityId,
            FacilityId = department.FacilityId,
            DepartmentId = department.Id,
            VisitDate = today,
            QueueNumber = index + 1,
            Status = status,
            CreatedByUserId = ReceptionistId
        }).ToArray();
        db.PatientVisits.AddRange(visits);
        await db.SaveChangesAsync();

        using var client = await CreateAuthenticatedClientAsync("rec@test.com");
        var facilityQuery = $"facilityId={department.FacilityId}";
        var stats = await DataAsync(await client.GetAsync($"/api/v1/reception/stats?{facilityQuery}"));
        var queue = await DataAsync(await client.GetAsync($"/api/v1/reception/billing/unbilled-visits?{facilityQuery}"));

        Assert.Equal(1, queue.GetProperty("totalItems").GetInt32());
        Assert.Equal(visits[0].Id, Assert.Single(queue.GetProperty("items").EnumerateArray()).GetProperty("visitId").GetInt64());
        Assert.Equal(queue.GetProperty("totalItems").GetInt32(), stats.GetProperty("unbilledCount").GetInt32());
    }

    [Fact]
    public async Task Upcoming_doctor_appointments_include_both_pending_change_statuses()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var today = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>().VietnamToday;
        var facilityId = await db.StaffFacilityAssignments.Where(a => a.UserId == DoctorId && a.IsActive)
            .Select(a => a.FacilityId).FirstAsync();
        var appointments = new List<Appointment>();
        foreach (var status in new[] { AppointmentStatus.PendingReschedule, AppointmentStatus.PendingCancellation })
        {
            var slot = await CreateAvailableSlotAsync(DoctorEntityId, today.AddDays(1 + appointments.Count), new(9, 0), new(9, 30));
            appointments.Add(new Appointment
            {
                AppointmentCode = $"APT-{Guid.NewGuid():N}"[..20],
                PatientId = Patient1EntityId,
                DoctorId = DoctorEntityId,
                SpecialtyId = SpecialtyEntityId,
                FacilityId = facilityId,
                AppointmentSlotId = slot.Id,
                AppointmentDate = slot.SlotDate,
                StartTime = slot.StartTime,
                EndTime = slot.EndTime,
                Status = status
            });
        }
        db.Appointments.AddRange(appointments);
        await db.SaveChangesAsync();

        using var client = await CreateAuthenticatedClientAsync("doc@test.com");
        var dashboard = await DataAsync(await client.GetAsync("/api/v1/doctor/dashboard"));
        var upcoming = dashboard.GetProperty("upcomingAppointments").EnumerateArray().ToArray();
        Assert.Equal<(string?, string?)>(appointments.Select(a => ((string?)a.AppointmentCode, (string?)a.Status.ToString())),
            upcoming.Select(a => (a.GetProperty("appointmentCode").GetString(), a.GetProperty("status").GetString())));
    }

    [Theory]
    [InlineData("TwentyFourSeven", "Ca đêm", "Ca ngày", "Ca tối")]
    [InlineData("Standard", "Ca sáng", "Ca sáng", "Ca chiều")]
    public async Task Weekly_schedule_uses_operating_hours_shift_names(string mode, string first, string second, string third)
    {
        var monday = new DateOnly(2030, 1, 7);
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (!await db.DoctorWorkSchedules.AnyAsync(s => s.DoctorId == DoctorEntityId && s.WorkDate == monday))
        {
            db.DoctorWorkSchedules.AddRange(new[] { (0, 8), (8, 16), (16, 23) }.Select(shift => new DoctorWorkSchedule
            {
                DoctorId = DoctorEntityId, WorkDate = monday, StartTime = new(shift.Item1, 0), EndTime = new(shift.Item2, 0), IsActive = true
            }));
            await db.SaveChangesAsync();
        }

        using var authenticated = await CreateAuthenticatedClientAsync("doc@test.com");
        using var modeFactory = Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.Configure<ClinicOperatingHoursOptions>(options => options.Mode = mode)));
        using var client = modeFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = authenticated.DefaultRequestHeaders.Authorization;
        var week = await DataAsync(await client.GetAsync("/api/v1/doctor/schedule?fromDate=2030-01-07&toDate=2030-01-13"));
        var shifts = week[0].GetProperty("shifts").EnumerateArray();
        Assert.Equal(new[] { first, second, third }, shifts.Select(s => s.GetProperty("shiftName").GetString()));
    }

    [Fact]
    public async Task Pharmacy_queue_returns_prescription_code_and_patient_name_from_the_list()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var prescription = new Prescription
        {
            PatientId = Patient1EntityId, DoctorId = DoctorEntityId, Status = PrescriptionStatus.Issued,
            CreatedAt = new DateTime(2026, 11, 2, 20, 0, 0, DateTimeKind.Utc)
        };
        db.Prescriptions.Add(prescription);
        await db.SaveChangesAsync();

        using var client = await CreateAuthenticatedClientAsync("pharm@test.com");
        var page = await DataAsync(await client.GetAsync("/api/v1/pharmacy/prescriptions?status=Issued"));
        var listed = page.GetProperty("items").EnumerateArray().Single(row => row.GetProperty("id").GetInt64() == prescription.Id);
        var reply = await DataAsync(await client.PostAsJsonAsync("/api/v1/ai/copilot/chat", new { message = "có bao nhiêu đơn chờ cấp thuốc" }));
        var queue = reply.GetProperty("cards").EnumerateArray().Single(card => card.GetProperty("type").GetString() == "pharmacist_prescription_queue");
        var item = queue.GetProperty("data").EnumerateArray().Single(row => row.GetProperty("id").GetInt64() == prescription.Id);

        Assert.Equal($"RX-261103-{prescription.Id:D4}", item.GetProperty("prescriptionCode").GetString());
        Assert.Equal(listed.GetProperty("patientName").GetString(), item.GetProperty("patientName").GetString());
        Assert.False(string.IsNullOrWhiteSpace(item.GetProperty("patientName").GetString()));
    }

    private static async Task<JsonElement> DataAsync(HttpResponseMessage response)
    {
        using (response)
        {
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        }
    }
}
