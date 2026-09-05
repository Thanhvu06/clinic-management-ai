using System;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using ClinicManagement.Application.HealthPackages.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ClinicManagement.IntegrationTests;

public class HealthPackageRegistrationTests : IntegrationTestBase
{
    public HealthPackageRegistrationTests(CustomWebApplicationFactory factory) : base(factory) { }

    [Fact]
    public async Task Given_PackageId_When_GetDetails_Then_ReturnsOkAndIncludedServices()
    {
        var response = await Client.GetAsync($"/api/v1/health-packages/{PackageEntityId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("PKG-TEST", json);
        Assert.Contains("Khám nội tổng quát", json);
    }

    [Fact]
    public async Task Given_ValidRequest_When_PatientRegisters_Then_ReturnsCreated()
    {
        await AuthenticateAsync("pat1@test.com");

        var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2));
        var request = new CreatePackageRegistrationRequest
        {
            HealthPackageId = PackageEntityId,
            PreferredDate = tomorrow,
            ContactPhone = "0987654321",
            Note = "Mong muốn khám vào buổi sáng"
        };

        var response = await Client.PostAsJsonAsync("/api/v1/patient/health-package-registrations", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("REG-", json);
        Assert.Contains("Pending", json);

        var doc = JsonDocument.Parse(json);
        var regId = doc.RootElement.GetProperty("data").GetProperty("id").GetInt64();

        // Check in patient's list
        var listResponse = await Client.GetAsync("/api/v1/patient/health-package-registrations");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var listJson = await listResponse.Content.ReadAsStringAsync();
        Assert.Contains($"\"id\":{regId}", listJson);
    }

    [Fact]
    public async Task Given_PastDate_When_PatientRegisters_Then_ReturnsError()
    {
        await AuthenticateAsync("pat1@test.com");

        var pastDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
        var request = new CreatePackageRegistrationRequest
        {
            HealthPackageId = PackageEntityId,
            PreferredDate = pastDate,
            ContactPhone = "0987654321",
            Note = "Test ngày quá khứ"
        };

        var response = await Client.PostAsJsonAsync("/api/v1/patient/health-package-registrations", request);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("INVALID_DATE", json);
    }

    [Fact]
    public async Task Given_NonExistentPackage_When_PatientRegisters_Then_ReturnsError()
    {
        await AuthenticateAsync("pat1@test.com");

        var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2));
        var request = new CreatePackageRegistrationRequest
        {
            HealthPackageId = 999999,
            PreferredDate = tomorrow,
            ContactPhone = "0987654321"
        };

        var response = await Client.PostAsJsonAsync("/api/v1/patient/health-package-registrations", request);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("PACKAGE_NOT_FOUND", json);
    }

    [Fact]
    public async Task Given_Patient1Registration_When_Patient2AttemptsToAccessOrCancel_Then_ReturnsNotFound()
    {
        // 1. Patient 1 registers
        await AuthenticateAsync("pat1@test.com");
        var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3));
        var regResponse = await Client.PostAsJsonAsync("/api/v1/patient/health-package-registrations", new CreatePackageRegistrationRequest
        {
            HealthPackageId = PackageEntityId,
            PreferredDate = tomorrow,
            ContactPhone = "0987654321",
            Note = "Đăng ký của Patient 1"
        });
        Assert.Equal(HttpStatusCode.Created, regResponse.StatusCode);
        var doc = JsonDocument.Parse(await regResponse.Content.ReadAsStringAsync());
        var regId = doc.RootElement.GetProperty("data").GetProperty("id").GetInt64();

        // 2. Patient 2 attempts access and cancellation
        await AuthenticateAsync("pat2@test.com");

        var getResponse = await Client.GetAsync($"/api/v1/patient/health-package-registrations/{regId}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);

        var cancelResponse = await Client.PostAsync($"/api/v1/patient/health-package-registrations/{regId}/cancel", null);
        Assert.Equal(HttpStatusCode.NotFound, cancelResponse.StatusCode);
    }

    [Fact]
    public async Task Given_Registration_When_ReceptionistConfirms_Then_StatusIsConfirmedAndCannotBeCancelledByPatient()
    {
        // 1. Patient 1 registers
        await AuthenticateAsync("pat1@test.com");
        var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(4));
        var regResponse = await Client.PostAsJsonAsync("/api/v1/patient/health-package-registrations", new CreatePackageRegistrationRequest
        {
            HealthPackageId = PackageEntityId,
            PreferredDate = tomorrow,
            ContactPhone = "0987654321"
        });
        Assert.Equal(HttpStatusCode.Created, regResponse.StatusCode);
        var doc = JsonDocument.Parse(await regResponse.Content.ReadAsStringAsync());
        var regId = doc.RootElement.GetProperty("data").GetProperty("id").GetInt64();

        // 2. Receptionist confirms
        await AuthenticateAsync("rec@test.com");
        var confirmResponse = await Client.PostAsync($"/api/v1/reception/health-package-registrations/{regId}/confirm", null);
        Assert.Equal(HttpStatusCode.OK, confirmResponse.StatusCode);

        var confirmJson = await confirmResponse.Content.ReadAsStringAsync();
        Assert.Contains("Confirmed", confirmJson);

        // 3. Patient attempts to cancel -> Must fail (CANNOT_CANCEL)
        await AuthenticateAsync("pat1@test.com");
        var cancelResponse = await Client.PostAsync($"/api/v1/patient/health-package-registrations/{regId}/cancel", null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, cancelResponse.StatusCode);
        var cancelJson = await cancelResponse.Content.ReadAsStringAsync();
        Assert.Contains("CANNOT_CANCEL", cancelJson);
    }

    [Fact]
    public async Task Given_NonPatientRole_When_AccessingPatientRegistrationApi_Then_ReturnsForbidden()
    {
        await AuthenticateAsync("doc@test.com");
        var response = await Client.GetAsync("/api/v1/patient/health-package-registrations");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        await AuthenticateAsync("pharm@test.com");
        var response2 = await Client.GetAsync("/api/v1/patient/health-package-registrations");
        Assert.Equal(HttpStatusCode.Forbidden, response2.StatusCode);
    }

    [Fact]
    public async Task Given_Receptionist_When_QueriesPagedAndManagesRegistrations_Then_CorrectPagedResultAndNotesPersisted()
    {
        // 1. Register a package
        await AuthenticateAsync("pat1@test.com");
        var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5));
        var regRes = await Client.PostAsJsonAsync("/api/v1/patient/health-package-registrations", new CreatePackageRegistrationRequest
        {
            HealthPackageId = PackageEntityId,
            PreferredDate = tomorrow,
            ContactPhone = "0933333333",
            Note = "Cần tư vấn thêm xét nghiệm máu"
        });
        Assert.Equal(HttpStatusCode.Created, regRes.StatusCode);
        var regDoc = JsonDocument.Parse(await regRes.Content.ReadAsStringAsync());
        var regId = regDoc.RootElement.GetProperty("data").GetProperty("id").GetInt64();
        var regCode = regDoc.RootElement.GetProperty("data").GetProperty("registrationCode").GetString();

        // 2. Receptionist queries with paging and search
        await AuthenticateAsync("rec@test.com");
        var pagedRes = await Client.GetAsync($"/api/v1/reception/health-package-registrations?page=1&pageSize=10&search={regCode}&status=Pending");
        Assert.Equal(HttpStatusCode.OK, pagedRes.StatusCode);

        var pagedJson = await pagedRes.Content.ReadAsStringAsync();
        Assert.Contains("\"items\":", pagedJson);
        Assert.Contains("\"totalItems\":", pagedJson);
        Assert.Contains(regCode!, pagedJson);

        // 3. Confirm with reception note
        var confirmRes = await Client.PostAsJsonAsync($"/api/v1/reception/health-package-registrations/{regId}/confirm", new ConfirmPackageRegistrationRequest
        {
            Notes = "Đã gọi xác nhận và hẹn giờ 8:00 sáng"
        });
        Assert.Equal(HttpStatusCode.OK, confirmRes.StatusCode);
        var confirmDoc = JsonDocument.Parse(await confirmRes.Content.ReadAsStringAsync());
        Assert.Equal("Confirmed", confirmDoc.RootElement.GetProperty("data").GetProperty("status").GetString());
        Assert.Equal("Đã gọi xác nhận và hẹn giờ 8:00 sáng", confirmDoc.RootElement.GetProperty("data").GetProperty("adminNotes").GetString());
    }
}
