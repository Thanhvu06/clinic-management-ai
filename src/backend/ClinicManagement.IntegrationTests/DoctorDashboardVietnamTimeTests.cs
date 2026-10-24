using System.Text.Json;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ClinicManagement.IntegrationTests;

public class DoctorDashboardVietnamTimeTests : IntegrationTestBase
{
    public DoctorDashboardVietnamTimeTests(CustomWebApplicationFactory factory) : base(factory) { }

    private async Task<PatientVisit> CreateWalkInAsync(TimeOnly vietnamCheckIn)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        await EnsureStaffAssignmentsAsync(db);
        var assignment = await db.StaffFacilityAssignments
            .FirstAsync(a => a.UserId == DoctorId && a.Role == "Doctor" && a.IsActive && a.DepartmentId != null);
        var today = clock.VietnamToday;
        var checkedIn = clock.ConvertVietnamToUtc(today.ToDateTime(vietnamCheckIn));
        var visit = new PatientVisit
        {
            VisitCode = $"VIS-TZ-{Guid.NewGuid():N}"[..20], PatientId = Patient1EntityId,
            FacilityId = assignment.FacilityId, DepartmentId = assignment.DepartmentId!.Value, AssignedDoctorId = DoctorEntityId,
            VisitDate = today, CheckedInAtUtc = checkedIn, CreatedAtUtc = checkedIn,
            ChiefComplaint = "Khám kiểm tra", Status = VisitStatus.WaitingDoctor
        };
        db.PatientVisits.Add(visit);
        await db.SaveChangesAsync();
        return visit;
    }

    private async Task<JsonElement> DataAsync(string url)
    {
        var response = await Client.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);
        return JsonDocument.Parse(body).RootElement.GetProperty("data").Clone();
    }

    [Fact]
    public async Task WalkInVisit_ShowsCheckInTimeInVietnamTime_OnDashboardAndClinicalContext()
    {
        var visit = await CreateWalkInAsync(new TimeOnly(8, 0));
        await AuthenticateAsync("doc@test.com");

        var dashboard = await DataAsync("/api/v1/doctor/dashboard");
        var item = dashboard.GetProperty("queue").EnumerateArray()
            .Single(q => q.GetProperty("visitCode").GetString() == visit.VisitCode);
        Assert.Equal("08:00:00", item.GetProperty("startTime").GetString());
        Assert.Equal("08:15:00", item.GetProperty("endTime").GetString());

        var context = await DataAsync($"/api/v1/doctor/visits/{visit.Id}/clinical-context");
        var current = context.GetProperty("currentAppointment");
        Assert.Equal("08:00:00", current.GetProperty("startTime").GetString());
        Assert.Equal("08:15:00", current.GetProperty("endTime").GetString());
    }

    [Fact]
    public async Task Dashboard_ReportsTodayWorkShifts_FromTheDutyRoster()
    {
        long[] expectedIds;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var today = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>().VietnamToday;
            if (!await db.DoctorWorkSchedules.AnyAsync(s => s.DoctorId == DoctorEntityId && s.WorkDate == today && s.IsActive))
            {
                db.DoctorWorkSchedules.Add(new DoctorWorkSchedule
                {
                    DoctorId = DoctorEntityId, WorkDate = today, StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(11, 30), IsActive = true
                });
                await db.SaveChangesAsync();
            }
            expectedIds = await db.DoctorWorkSchedules.Where(s => s.DoctorId == DoctorEntityId && s.WorkDate == today && s.IsActive)
                .Select(s => s.Id).ToArrayAsync();
        }
        await AuthenticateAsync("doc@test.com");

        var dashboard = await DataAsync("/api/v1/doctor/dashboard");
        var shifts = dashboard.GetProperty("todayShifts").EnumerateArray().ToList();
        Assert.Equal(expectedIds.Length, shifts.Count);
        Assert.All(shifts, shift =>
        {
            var start = TimeOnly.Parse(shift.GetProperty("startTime").GetString()!);
            Assert.Equal(start < new TimeOnly(12, 0) ? "Ca sáng" : "Ca chiều", shift.GetProperty("shiftName").GetString());
        });
        var starts = shifts.Select(s => s.GetProperty("startTime").GetString()).ToList();
        Assert.Equal(starts.OrderBy(s => s).ToList(), starts);
    }
}
