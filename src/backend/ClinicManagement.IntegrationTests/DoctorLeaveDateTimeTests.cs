using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Leaves.DTOs;
using ClinicManagement.Application.Leaves.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Infrastructure.Identity;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicManagement.IntegrationTests;

public class DoctorLeaveDateTimeTests(CustomWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    private const string MissingTimeMessage = "Vui lòng chọn thời gian bắt đầu và kết thúc nghỉ.";
    private static string FutureDate => DateTime.UtcNow.AddDays(30).ToString("yyyy-MM-dd");

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Missing_datetime_fields_return_400(bool includeStart, bool includeEnd)
    {
        await AuthenticateAsync("doc@test.com");
        var payload = new Dictionary<string, string>
        {
            ["startDate"] = FutureDate, ["endDate"] = FutureDate, ["reason"] = "Legacy date payload"
        };
        if (includeStart) payload["startDateTime"] = $"{FutureDate}T08:00:00";
        if (includeEnd) payload["endDateTime"] = $"{FutureDate}T09:00:00";

        var response = await Client.PostAsJsonAsync("/api/v1/doctor/leave-requests", payload);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(MissingTimeMessage, body.GetProperty("message").GetString());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Service_rejects_default_time_before_other_validations(bool missingStart)
    {
        using var scope = Factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IDoctorLeaveService>();
        var error = await Assert.ThrowsAsync<BusinessException>(() => service.CreateLeaveRequestAsync(new CreateLeaveRequestDto
        {
            StartDateTime = missingStart ? default : DateTime.UtcNow.AddDays(30),
            EndDateTime = missingStart ? DateTime.UtcNow.AddDays(31) : default,
            Reason = "Missing time"
        }));
        Assert.Equal("INVALID_TIME", error.ErrorCode);
        Assert.Equal(MissingTimeMessage, error.Message);
    }

    [Theory]
    [InlineData("01:00:00Z", "02:00:00Z", 8, 9)]
    [InlineData("01:00:00", "02:00:00", 1, 2)]
    [InlineData("01:00:00Z", "09:00:00", 8, 9)]
    public async Task Create_persists_Vietnam_local_times(string start, string end, int startHour, int endHour)
    {
        using var scope = Factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = $"leave-time-{Guid.NewGuid():N}@test.com";
        var user = new ApplicationUser { UserName = email, Email = email, PhoneNumber = $"09{Random.Shared.Next(10000000, 99999999)}", FullName = "Doctor leave time", IsActive = true };
        Assert.True((await users.CreateAsync(user, "Pass@123")).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, "Doctor")).Succeeded);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Doctors.Add(new Doctor { UserId = user.Id, IsActive = true });
        await db.SaveChangesAsync();
        using var client = await CreateAuthenticatedClientAsync(email);

        var response = await client.PostAsJsonAsync("/api/v1/doctor/leave-requests", new
        {
            startDateTime = $"{FutureDate}T{start}", endDateTime = $"{FutureDate}T{end}", reason = "Local time test"
        });

        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var data = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        var leave = await db.DoctorLeaveRequests.AsNoTracking().SingleAsync(x => x.Id == data.GetProperty("id").GetInt64());
        var expectedStart = DateTime.Parse($"{FutureDate}T{startHour:00}:00:00");
        var expectedEnd = DateTime.Parse($"{FutureDate}T{endHour:00}:00:00");
        Assert.Equal(expectedStart, leave.StartDateTime);
        Assert.Equal(expectedEnd, leave.EndDateTime);
        Assert.Equal(expectedStart, data.GetProperty("startDateTime").GetDateTime());
        Assert.Equal(expectedEnd, data.GetProperty("endDateTime").GetDateTime());
        Assert.Equal(DateTimeKind.Unspecified, leave.StartDateTime.Kind);
        Assert.Equal(DateTimeKind.Unspecified, leave.EndDateTime.Kind);
    }

    [Theory]
    [InlineData("start", "end", "01:00:00Z", "02:00:00Z", 8, 9)]
    [InlineData("startDate", "endDate", "01:00:00Z", "02:00:00Z", 8, 9)]
    [InlineData("start", "end", "01:00:00", "02:00:00", 1, 2)]
    [InlineData("start", "end", "00:00:00Z", "01:00:00Z", 7, 8)]
    public async Task Preview_uses_local_times(string startKey, string endKey, string start, string end, int startHour, int endHour)
    {
        await AuthenticateAsync("doc@test.com");
        var response = await Client.GetAsync($"/api/v1/doctor/leave-requests/preview?{startKey}={FutureDate}T{start}&{endKey}={FutureDate}T{end}");
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var data = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        Assert.Equal(DateTime.Parse($"{FutureDate}T{startHour:00}:00:00"), data.GetProperty("startDateTime").GetDateTime());
        Assert.Equal(DateTime.Parse($"{FutureDate}T{endHour:00}:00:00"), data.GetProperty("endDateTime").GetDateTime());
    }

    [Theory]
    [InlineData("0001-01-01T00:00:00", "2099-01-01T09:00:00")]
    [InlineData("2099-01-01T08:00:00", "0001-01-01T00:00:00")]
    public async Task Preview_rejects_default_time(string start, string end)
    {
        await AuthenticateAsync("doc@test.com");
        var response = await Client.GetAsync($"/api/v1/doctor/leave-requests/preview?start={start}&end={end}");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("INVALID_TIME", body.GetProperty("errorCode").GetString());
        Assert.Equal(MissingTimeMessage, body.GetProperty("message").GetString());
    }
}
