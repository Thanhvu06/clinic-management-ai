using ClinicManagement.Application.Appointments.Interfaces;
using ClinicManagement.Application.Authentication.Interfaces;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using ClinicManagement.Infrastructure.Schedules;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicManagement.IntegrationTests;

public class OperatingHoursWorkflowTests
{
    [Theory]
    [InlineData("Standard", 1, 2, "book")]
    [InlineData("Standard", 4, 23, "book")]
    [InlineData("TwentyFourSeven", 1, 2, "book")]
    [InlineData("TwentyFourSeven", 4, 23, "book")]
    [InlineData("Standard", 1, 2, "reschedule")]
    [InlineData("Standard", 4, 23, "reschedule")]
    [InlineData("TwentyFourSeven", 1, 2, "reschedule")]
    [InlineData("TwentyFourSeven", 4, 23, "reschedule")]
    [InlineData("Standard", 1, 2, "revisit")]
    [InlineData("Standard", 4, 23, "revisit")]
    [InlineData("TwentyFourSeven", 1, 2, "revisit")]
    [InlineData("TwentyFourSeven", 4, 23, "revisit")]
    public async Task Booking_reschedule_and_revisit_follow_policy(string mode, int day, int hour, string operation)
    {
        using var parent = new FixedSeedWebApplicationFactory(new DateTime(2026, 10, 26, 2, 0, 0, DateTimeKind.Utc));
        var user = new OperatingHoursTestUser();
        using var factory = parent.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.Configure<ClinicOperatingHoursOptions>(options => options.Mode = mode);
            services.AddSingleton<ICurrentUserService>(user);
        }));
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        await DevelopmentDataSeeder.SeedMasterDataAsync(services);
        var db = services.GetRequiredService<AppDbContext>();
        var doctorUser = await db.Users.SingleAsync(u => u.Email == "doctor@cliniccare.local");
        var doctor = await db.Doctors.Include(d => d.DoctorSpecialties).SingleAsync(d => d.UserId == doctorUser.Id);
        var patient = await db.Patients.SingleAsync(p => p.Email == "patient@cliniccare.local");
        var facility = await db.Facilities.SingleAsync(f => f.Code == "CS01");
        var specialty = doctor.DoctorSpecialties.Single(ds => ds.IsPrimary).SpecialtyId;
        var date = new DateOnly(2026, 11, day);
        var start = new TimeOnly(hour, 0);
        var target = new AppointmentSlot { DoctorId = doctor.Id, SlotDate = date, StartTime = start, EndTime = start.AddMinutes(30) };
        var originalSlot = new AppointmentSlot { DoctorId = doctor.Id, SlotDate = date.AddDays(1), StartTime = new(10, 0), EndTime = new(10, 30) };
        db.AppointmentSlots.AddRange(target, originalSlot);
        db.DoctorWorkSchedules.Add(new DoctorWorkSchedule { DoctorId = doctor.Id, WorkDate = date, StartTime = new(0, 0), EndTime = new(23, 30) });
        await db.SaveChangesAsync();
        user.UserId = patient.UserId;
        async Task Act()
        {
            if (operation == "book")
            {
                var created = await services.GetRequiredService<IAppointmentService>().CreateAppointmentAsync(new()
                {
                    DoctorId = doctor.Id, SpecialtyId = specialty, FacilityId = facility.Id, AppointmentSlotId = target.Id, Reason = "Khám sức khỏe định kỳ"
                });
                Assert.Equal(target.Id, created.AppointmentSlotId);
                return;
            }
            var original = new Appointment
            {
                AppointmentCode = $"APT-{Guid.NewGuid():N}", PatientId = patient.Id, DoctorId = doctor.Id, SpecialtyId = specialty, FacilityId = facility.Id,
                AppointmentSlotId = originalSlot.Id, AppointmentDate = originalSlot.SlotDate, StartTime = originalSlot.StartTime, EndTime = originalSlot.EndTime,
                Status = operation == "reschedule" ? AppointmentStatus.Confirmed : AppointmentStatus.Completed, Reason = "Khám sức khỏe định kỳ"
            };
            db.Appointments.Add(original);
            await db.SaveChangesAsync();
            if (operation == "reschedule")
            {
                var changes = services.GetRequiredService<IChangeRequestService>();
                var requested = await changes.CreateRescheduleRequestAsync(original.Id, new() { RequestedSlotId = target.Id, Reason = "Đổi ngày khám theo giờ hoạt động" });
                db.ChangeTracker.Clear();
                user.UserId = (await db.Users.SingleAsync(u => u.Email == "reception@cliniccare.local")).Id;
                await changes.ApproveRescheduleAsync(requested.Id, new());
                db.ChangeTracker.Clear();
                Assert.Equal(target.Id, (await db.Appointments.SingleAsync(a => a.Id == original.Id)).AppointmentSlotId);
            }
            else
            {
                var revisit = new RevisitRequest { AppointmentId = original.Id, PatientId = patient.Id, DoctorId = doctor.Id, SuggestedDate = date,
                    Status = RevisitRequestStatus.PendingPatientResponse };
                db.RevisitRequests.Add(revisit);
                await db.SaveChangesAsync();
                var created = await services.GetRequiredService<IRevisitService>().AcceptRevisitRequestAsync(revisit.Id, new() { TargetSlotId = target.Id });
                Assert.Equal(target.Id, created.AppointmentSlotId);
            }
        }
        if (mode == "Standard" && day == 1) await Assert.ThrowsAsync<BusinessException>(Act);
        else await Act();
    }
}
