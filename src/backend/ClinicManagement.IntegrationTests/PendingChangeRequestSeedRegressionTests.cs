using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicManagement.IntegrationTests;

public class PendingChangeRequestSeedRegressionTests
{
    [Theory]
    [InlineData(27)]
    [InlineData(31)]
    public async Task RescheduleSeed_ChoosesAnAvailableSlotInTheFollowingCalendarWeek(int day)
    {
        using var factory = new FixedSeedWebApplicationFactory(new DateTime(2026, 10, day, 3, 0, 0, DateTimeKind.Utc));
        using var scope = factory.Services.CreateScope();
        await DevelopmentDataSeeder.SeedDoctorSchedulesAndSlotsAsync(scope.ServiceProvider, 7, true);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var request = await db.AppointmentChangeRequests.Include(r => r.Appointment).Include(r => r.RequestedSlot)
            .SingleAsync(r => r.RequestType == AppointmentChangeRequestType.Reschedule && r.Reason.Contains("tuần sau"));
        var nextMonday = request.Appointment.AppointmentDate.AddDays(7 - ((int)request.Appointment.AppointmentDate.DayOfWeek + 6) % 7);
        Assert.InRange(request.RequestedSlot!.SlotDate, nextMonday, nextMonday.AddDays(6));
        Assert.False(request.RequestedSlot.IsBooked);
        Assert.Equal(request.Appointment.DoctorId, request.RequestedSlot.DoctorId);
    }
}
