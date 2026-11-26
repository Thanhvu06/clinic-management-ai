using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClinicManagement.Application.Appointments.DTOs.Doctor;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Appointments;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicManagement.IntegrationTests;

public sealed class RolesDoctorReceptionFixTests(CustomWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    private static int _minute;

    private async Task<Appointment> InsertAppointmentAsync(DateOnly date, AppointmentStatus status, long? patientId = null, AppointmentSlot? sharedSlot = null)
    {
        var minute = Interlocked.Increment(ref _minute) % 50;
        var start = new TimeOnly(21, minute);
        var slot = sharedSlot ?? await CreateAvailableSlotAsync(DoctorEntityId, date, start, start.AddMinutes(5));
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.AppointmentSlots.SingleAsync(s => s.Id == slot.Id);
        if (status.HoldsSlot()) stored.IsBooked = true;
        var appointment = new Appointment
        {
            AppointmentCode = "APT-RF-" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant(),
            PatientId = patientId ?? Patient1EntityId, DoctorId = DoctorEntityId, SpecialtyId = SpecialtyEntityId,
            AppointmentSlotId = slot.Id, AppointmentDate = date, StartTime = slot.StartTime, EndTime = slot.EndTime,
            Reason = "Kiểm thử vai trò bác sĩ lễ tân", Status = status
        };
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync();
        return appointment;
    }

    private async Task<DateOnly> VietnamTodayAsync()
    {
        using var scope = Factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IDateTimeProvider>().VietnamToday;
    }

    private static async Task<JsonElement> DataAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);
        return JsonDocument.Parse(body).RootElement.GetProperty("data").Clone();
    }

    private async Task<(long AppointmentId, long VisitId)> AppointmentInConsultationAsync()
    {
        var date = GetFutureWorkingDate(23);
        var slot = await CreateAvailableSlotAsync(DoctorEntityId, date, new TimeOnly(15, 10), new TimeOnly(15, 40));
        var patient = await CreateAuthenticatedClientAsync("pat1@test.com");
        var created = await DataAsync(await patient.PostAsJsonAsync("/api/v1/appointments", new
        {
            DoctorId = DoctorEntityId, SpecialtyId = SpecialtyEntityId, AppointmentSlotId = slot.Id, Reason = "Khám đau đầu kéo dài nhiều ngày"
        }));
        var appointmentId = created.GetProperty("id").GetInt64();
        await MoveAppointmentToVietnamTodayAsync(appointmentId);
        var reception = await CreateAuthenticatedClientAsync("rec@test.com");
        Assert.Equal(HttpStatusCode.OK, (await reception.PostAsync($"/api/v1/reception/appointments/{appointmentId}/confirm", null)).StatusCode);
        var doctor = await CreateAuthenticatedClientAsync("doc@test.com");
        Assert.Equal(HttpStatusCode.OK, (await doctor.PostAsync($"/api/v1/doctor/appointments/{appointmentId}/check-in", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await doctor.PostAsync($"/api/v1/doctor/appointments/{appointmentId}/start-consultation", null)).StatusCode);
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (appointmentId, await db.PatientVisits.Where(v => v.AppointmentId == appointmentId).Select(v => v.Id).SingleAsync());
    }

    [Fact]
    public void H1_rules_treat_empty_and_the_legacy_sentence_as_missing()
    {
        Assert.True(ConsultationSummaryRules.IsMissing(null));
        Assert.True(ConsultationSummaryRules.IsMissing("  "));
        Assert.True(ConsultationSummaryRules.IsMissing("Đang trong quá trình thăm khám."));
        Assert.True(ConsultationSummaryRules.IsMissing("  đang trong quá trình thăm khám.  "));
        Assert.False(ConsultationSummaryRules.IsMissing("Viêm họng cấp, điều trị ngoại trú"));
        Assert.Equal(string.Empty, ConsultationSummaryRules.HideLegacyPlaceholder(ConsultationSummaryRules.LegacyPlaceholder));
        Assert.Equal("Kết luận", ConsultationSummaryRules.HideLegacyPlaceholder("Kết luận"));
    }

    [Fact]
    public async Task H1_starting_a_consultation_leaves_the_conclusion_empty_and_completion_requires_a_real_one()
    {
        var (appointmentId, visitId) = await AppointmentInConsultationAsync();
        var doctor = await CreateAuthenticatedClientAsync("doc@test.com");
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var summary = await db.VisitSummaries.SingleAsync(s => s.AppointmentId == appointmentId);
            Assert.Equal(string.Empty, summary.Summary);
        }

        foreach (var summary in new string?[] { null, "", "   ", ConsultationSummaryRules.LegacyPlaceholder })
        {
            var response = await doctor.PostAsJsonAsync($"/api/v1/doctor/visits/{visitId}/complete",
                new CompleteConsultationRequest { Diagnosis = "Đau đầu do căng thẳng", Summary = summary });
            Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
            Assert.Contains("VALIDATION_ERROR", await response.Content.ReadAsStringAsync());
        }
        var byAppointment = await doctor.PostAsJsonAsync($"/api/v1/doctor/appointments/{appointmentId}/complete",
            new CompleteConsultationRequest { Diagnosis = "Đau đầu do căng thẳng", Summary = ConsultationSummaryRules.LegacyPlaceholder });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, byAppointment.StatusCode);

        var ok = await doctor.PostAsJsonAsync($"/api/v1/doctor/visits/{visitId}/complete",
            new CompleteConsultationRequest { Diagnosis = "Đau đầu do căng thẳng", Summary = "Đau đầu căng thẳng, điều trị ngoại trú" });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }

    [Fact]
    public async Task H1_an_old_draft_holding_the_legacy_sentence_is_shown_empty_and_cannot_be_completed_with_it()
    {
        var (appointmentId, visitId) = await AppointmentInConsultationAsync();
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var summary = await db.VisitSummaries.SingleAsync(s => s.AppointmentId == appointmentId);
            summary.Summary = ConsultationSummaryRules.LegacyPlaceholder;
            await db.SaveChangesAsync();
        }
        var doctor = await CreateAuthenticatedClientAsync("doc@test.com");
        var encounter = await DataAsync(await doctor.GetAsync($"/api/v1/doctor/visits/{visitId}/encounter"));
        Assert.Equal(string.Empty, encounter.GetProperty("summary").GetString());

        var response = await doctor.PostAsJsonAsync($"/api/v1/doctor/visits/{visitId}/complete",
            new CompleteConsultationRequest { Diagnosis = "Đau đầu", Summary = ConsultationSummaryRules.LegacyPlaceholder });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task H2_today_queue_excludes_cancelled_appointments_and_walk_ins_but_keeps_no_shows()
    {
        var today = await VietnamTodayAsync();
        var cancelled = await InsertAppointmentAsync(today, AppointmentStatus.Cancelled);
        var noShow = await InsertAppointmentAsync(today, AppointmentStatus.NoShow, Patient2EntityId);
        var confirmed = await InsertAppointmentAsync(today, AppointmentStatus.Confirmed);
        long cancelledVisitId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var department = await db.Departments.OrderBy(d => d.Id).FirstAsync();
            var visit = new PatientVisit
            {
                VisitCode = "VIS-RF-" + Guid.NewGuid().ToString("N")[..10], PatientId = Patient2EntityId, FacilityId = department.FacilityId,
                DepartmentId = department.Id, AssignedDoctorId = DoctorEntityId, VisitDate = today,
                QueueNumber = (await db.PatientVisits.MaxAsync(v => (int?)v.QueueNumber) ?? 900) + 1, Status = VisitStatus.Cancelled,
                CreatedByUserId = ReceptionistId
            };
            db.PatientVisits.Add(visit);
            await db.SaveChangesAsync();
            cancelledVisitId = visit.Id;
        }

        var doctor = await CreateAuthenticatedClientAsync("doc@test.com");
        var dashboard = await DataAsync(await doctor.GetAsync("/api/v1/doctor/dashboard"));
        var queue = dashboard.GetProperty("queue").EnumerateArray().ToList();
        Assert.Equal(dashboard.GetProperty("totalToday").GetInt32(), queue.Count);
        Assert.DoesNotContain(queue, row => row.GetProperty("status").GetString() == "Cancelled");
        Assert.DoesNotContain(queue, row => row.GetProperty("appointmentCode").GetString() == cancelled.AppointmentCode);
        Assert.DoesNotContain(queue, row => row.TryGetProperty("patientVisitId", out var id) && id.ValueKind == JsonValueKind.Number && id.GetInt64() == cancelledVisitId);
        Assert.Contains(queue, row => row.GetProperty("appointmentCode").GetString() == noShow.AppointmentCode && row.GetProperty("status").GetString() == "NoShow");
        Assert.Contains(queue, row => row.GetProperty("appointmentCode").GetString() == confirmed.AppointmentCode);
    }

    [Fact]
    public void H3_slot_state_marks_past_no_show_booked_and_free()
    {
        var day = new DateOnly(2026, 11, 26);
        var now = day.ToDateTime(new TimeOnly(15, 0));
        AppointmentSlot Slot(int hour, bool booked = false) => new() { Id = hour, SlotDate = day, StartTime = new TimeOnly(hour, 0), EndTime = new TimeOnly(hour, 30), IsBooked = booked };
        DoctorScheduleAppointmentRow Row(AppointmentStatus status) => new(1, 1, "APT-X", status, "Người bệnh");

        var pastFree = DoctorScheduleSlotStates.Build(Slot(0), null, now);
        Assert.Equal(DoctorScheduleSlotStates.Past, pastFree.SlotState);
        Assert.False(pastFree.IsBooked);
        Assert.True(pastFree.IsPast);

        var noShow = DoctorScheduleSlotStates.Build(Slot(4), Row(AppointmentStatus.NoShow), now);
        Assert.Equal(DoctorScheduleSlotStates.NoShow, noShow.SlotState);
        Assert.True(noShow.IsBooked);
        Assert.Equal("APT-X", noShow.AppointmentCode);

        Assert.Equal(DoctorScheduleSlotStates.Booked, DoctorScheduleSlotStates.Build(Slot(9), Row(AppointmentStatus.Completed), now).SlotState);
        Assert.Equal(DoctorScheduleSlotStates.Booked, DoctorScheduleSlotStates.Build(Slot(16, booked: true), Row(AppointmentStatus.Confirmed), now).SlotState);
        Assert.Equal(DoctorScheduleSlotStates.Free, DoctorScheduleSlotStates.Build(Slot(17), null, now).SlotState);

        var lateNight = new AppointmentSlot { Id = 99, SlotDate = day, StartTime = new TimeOnly(23, 30), EndTime = new TimeOnly(0, 0) };
        Assert.Equal(DoctorScheduleSlotStates.Free, DoctorScheduleSlotStates.Build(lateNight, null, day.ToDateTime(new TimeOnly(23, 40))).SlotState);
    }

    [Fact]
    public void H3_cancelled_appointments_do_not_occupy_a_slot_and_a_reused_slot_shows_the_live_one()
    {
        var rows = new[]
        {
            new DoctorScheduleAppointmentRow(1, 10, "APT-CANCELLED", AppointmentStatus.Cancelled, "A"),
            new DoctorScheduleAppointmentRow(2, 10, "APT-LIVE", AppointmentStatus.Confirmed, "B"),
            new DoctorScheduleAppointmentRow(3, 11, "APT-ONLY-CANCELLED", AppointmentStatus.Cancelled, "C"),
        };
        var map = DoctorScheduleSlotStates.OccupyingAppointments(rows);
        Assert.Equal("APT-LIVE", map[10].AppointmentCode);
        Assert.False(map.ContainsKey(11));
    }

    [Fact]
    public async Task H3_schedule_endpoint_reports_past_no_show_and_cancelled_slots()
    {
        var today = await VietnamTodayAsync();
        var yesterday = today.AddDays(-1);
        var tomorrow = today.AddDays(1);
        var pastFree = await CreateAvailableSlotAsync(DoctorEntityId, yesterday, new TimeOnly(6, 0), new TimeOnly(6, 30));
        var noShow = await InsertAppointmentAsync(yesterday, AppointmentStatus.NoShow, Patient2EntityId);
        var cancelled = await InsertAppointmentAsync(tomorrow, AppointmentStatus.Cancelled);

        var doctor = await CreateAuthenticatedClientAsync("doc@test.com");
        var days = await DataAsync(await doctor.GetAsync($"/api/v1/doctor/schedule?fromDate={yesterday:yyyy-MM-dd}&toDate={tomorrow:yyyy-MM-dd}"));
        var shifts = days.EnumerateArray().SelectMany(d => d.GetProperty("shifts").EnumerateArray()).ToList();
        var slots = shifts.SelectMany(s => s.GetProperty("slots").EnumerateArray()).ToList();
        JsonElement SlotOf(long id) => slots.Single(s => s.GetProperty("slotId").GetInt64() == id);

        Assert.Equal("Past", SlotOf(pastFree.Id).GetProperty("slotState").GetString());
        var noShowSlot = SlotOf(noShow.AppointmentSlotId);
        Assert.Equal("NoShow", noShowSlot.GetProperty("slotState").GetString());
        Assert.True(noShowSlot.GetProperty("isBooked").GetBoolean());
        Assert.Equal("Free", SlotOf(cancelled.AppointmentSlotId).GetProperty("slotState").GetString());
        foreach (var shift in shifts)
        {
            var shiftSlots = shift.GetProperty("slots").EnumerateArray().ToList();
            Assert.Equal(shiftSlots.Count(s => s.GetProperty("isBooked").GetBoolean()), shift.GetProperty("bookedSlots").GetInt32());
        }
    }

    [Fact]
    public async Task H4_reception_counts_equal_the_rows_of_the_today_and_pending_lists()
    {
        var today = await VietnamTodayAsync();
        await InsertAppointmentAsync(today, AppointmentStatus.Cancelled);
        await InsertAppointmentAsync(today, AppointmentStatus.Pending);
        await InsertAppointmentAsync(today.AddDays(1), AppointmentStatus.Pending);
        await InsertAppointmentAsync(today.AddDays(2), AppointmentStatus.Pending);

        var reception = await CreateAuthenticatedClientAsync("rec@test.com");
        var stats = await DataAsync(await reception.GetAsync("/api/v1/reception/stats"));
        var todayList = await DataAsync(await reception.GetAsync("/api/v1/reception/appointments?tab=today&page=1&pageSize=100"));
        var pendingList = await DataAsync(await reception.GetAsync("/api/v1/reception/appointments?tab=pending&page=1&pageSize=100"));

        Assert.Equal(todayList.GetProperty("totalItems").GetInt32(), stats.GetProperty("appointmentsToday").GetInt32());
        Assert.Equal(pendingList.GetProperty("totalItems").GetInt32(), stats.GetProperty("pendingAppointmentsTotal").GetInt32());
        var pendingRows = pendingList.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(pendingRows.Count(r => DateOnly.Parse(r.GetProperty("appointmentDate").GetString()!) == today), stats.GetProperty("pendingAppointmentsToday").GetInt32());
        Assert.Contains(pendingRows, r => DateOnly.Parse(r.GetProperty("appointmentDate").GetString()!) > today);
    }

    [Fact]
    public async Task H5_H6_every_worklist_row_carries_its_date_and_opens_by_id()
    {
        var today = await VietnamTodayAsync();
        var future = await InsertAppointmentAsync(today.AddDays(1), AppointmentStatus.Pending);
        var reception = await CreateAuthenticatedClientAsync("rec@test.com");
        var pending = await DataAsync(await reception.GetAsync("/api/v1/reception/appointments?tab=pending&page=1&pageSize=100"));
        var row = pending.GetProperty("items").EnumerateArray().Single(r => r.GetProperty("id").GetInt64() == future.Id);
        Assert.Equal(today.AddDays(1), DateOnly.Parse(row.GetProperty("appointmentDate").GetString()!));

        var detail = await DataAsync(await reception.GetAsync($"/api/v1/reception/appointments/{future.Id}"));
        Assert.Equal(future.AppointmentCode, detail.GetProperty("appointmentCode").GetString());
        Assert.Equal(today.AddDays(1), DateOnly.Parse(detail.GetProperty("appointmentDate").GetString()!));
    }

    [Fact]
    public void H8_new_walk_in_birthdays_keep_the_age_and_never_fall_on_the_seed_date()
    {
        var seed = new DateOnly(2026, 11, 25);
        var births = Enumerable.Range(0, 32).Select(i => DevelopmentDemoBirthdaySeed.WalkInBirthDate(seed, i)).ToList();
        for (var i = 0; i < births.Count; i++)
        {
            Assert.False(births[i].Month == seed.Month && births[i].Day == seed.Day);
            Assert.Equal(25 + i, AgeOn(births[i], seed));
        }
        Assert.Equal(births.Count, births.Select(b => (b.Month, b.Day)).Distinct().Count());
    }

    [Fact]
    public async Task H8_startup_seed_repairs_only_demo_walk_ins_born_on_the_seed_date_and_is_idempotent()
    {
        var seed = new DateOnly(2025, 3, 14);
        var indexes = new[] { 40, 41, 42, 43 };
        long outsiderId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            foreach (var index in indexes)
            {
                var phone = $"{DevelopmentDemoBirthdaySeed.WalkInPhonePrefix}{index + 1:D4}";
                var patient = await db.Patients.FirstOrDefaultAsync(p => p.PhoneNumber == phone);
                if (patient is null)
                {
                    patient = new Patient { FullName = $"Bệnh nhân demo {index}", PhoneNumber = phone, MedicalRecordNumber = $"BN-RF-{Guid.NewGuid():N}"[..20] };
                    db.Patients.Add(patient);
                }
                patient.UserId = null;
                patient.DateOfBirth = seed.AddYears(-(DevelopmentDemoBirthdaySeed.FirstWalkInAge + index));
            }
            var outsider = new Patient { FullName = "Bệnh nhân thật", PhoneNumber = "0912000777", MedicalRecordNumber = $"BN-RF-{Guid.NewGuid():N}"[..20], DateOfBirth = seed.AddYears(-30) };
            db.Patients.Add(outsider);
            await db.SaveChangesAsync();
            outsiderId = outsider.Id;
        }

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.True(await DevelopmentDemoBirthdaySeed.EnsureAsync(db) >= indexes.Length);
        }

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            foreach (var index in indexes)
            {
                var phone = $"{DevelopmentDemoBirthdaySeed.WalkInPhonePrefix}{index + 1:D4}";
                var dob = (await db.Patients.SingleAsync(p => p.PhoneNumber == phone)).DateOfBirth!.Value;
                Assert.False(dob.Month == seed.Month && dob.Day == seed.Day);
                Assert.Equal(DevelopmentDemoBirthdaySeed.FirstWalkInAge + index, AgeOn(dob, seed));
            }
            Assert.Equal(seed.AddYears(-30), (await db.Patients.SingleAsync(p => p.Id == outsiderId)).DateOfBirth);
            Assert.Equal(0, await DevelopmentDemoBirthdaySeed.EnsureAsync(db));
        }
    }

    private static int AgeOn(DateOnly birth, DateOnly on)
    {
        var age = on.Year - birth.Year;
        return on < birth.AddYears(age) ? age - 1 : age;
    }
}
