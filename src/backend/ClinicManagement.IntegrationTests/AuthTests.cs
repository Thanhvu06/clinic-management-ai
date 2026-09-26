using System;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using ClinicManagement.Infrastructure.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace ClinicManagement.IntegrationTests;

public class AuthTests : IntegrationTestBase
{
    public AuthTests(CustomWebApplicationFactory factory) : base(factory) { }

    private async Task<string> CreateUniqueUserAsync(bool isActive = true, string password = "Pass@123")
    {
        using var scope = Factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = $"user_{Guid.NewGuid():N}@test.com";
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            FullName = "Test User",
            PhoneNumber = $"09{Random.Shared.Next(10000000, 99999999)}",
            IsActive = isActive,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var result = await userManager.CreateAsync(user, password);
        Assert.True(result.Succeeded, $"Failed to create test user: {string.Join(", ", result.Errors.Select(e => e.Description))}");
        await userManager.AddToRoleAsync(user, "Patient");
        return email;
    }

    [Fact]
    public async Task Given_ValidUserInDemoMode_When_ForgotPassword_Then_ReturnsValidResetToken()
    {
        var email = await CreateUniqueUserAsync();
        var response = await Client.PostAsJsonAsync("/api/v1/auth/forgot-password", new { email });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("Nếu email tồn tại trong hệ thống, hướng dẫn đặt lại mật khẩu đã được xử lý.", doc.RootElement.GetProperty("message").GetString());

        var data = doc.RootElement.GetProperty("data");
        Assert.True(data.TryGetProperty("resetToken", out var tokenProp));
        var token = tokenProp.GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));
    }

    [Fact]
    public async Task Given_ValidResetToken_When_ResetPassword_Then_SuccessAndCanLoginWithNewPassword()
    {
        var email = await CreateUniqueUserAsync();
        var forgotRes = await Client.PostAsJsonAsync("/api/v1/auth/forgot-password", new { email });
        var forgotJson = await forgotRes.Content.ReadAsStringAsync();
        using var forgotDoc = JsonDocument.Parse(forgotJson);
        var token = forgotDoc.RootElement.GetProperty("data").GetProperty("resetToken").GetString()!;

        var newPassword = "NewValidPassword@2026";
        var resetRes = await Client.PostAsJsonAsync("/api/v1/auth/reset-password", new
        {
            email,
            token,
            newPassword
        });
        Assert.Equal(HttpStatusCode.OK, resetRes.StatusCode);

        var loginRes = await Client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            emailOrPhone = email,
            password = newPassword
        });
        Assert.Equal(HttpStatusCode.OK, loginRes.StatusCode);
        var loginJson = await loginRes.Content.ReadAsStringAsync();
        using var loginDoc = JsonDocument.Parse(loginJson);
        Assert.True(loginDoc.RootElement.GetProperty("data").TryGetProperty("accessToken", out var accessProp));
        Assert.False(string.IsNullOrWhiteSpace(accessProp.GetString()));
    }

    [Fact]
    public async Task Given_InvalidToken_When_ResetPassword_Then_RejectedWith422AndResetPasswordFailedCode()
    {
        var email = await CreateUniqueUserAsync();
        var resetRes = await Client.PostAsJsonAsync("/api/v1/auth/reset-password", new
        {
            email,
            token = "invalid-token-123456",
            newPassword = "ValidPassword@123"
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resetRes.StatusCode);
        var resetJson = await resetRes.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(resetJson);
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("RESET_PASSWORD_FAILED", doc.RootElement.GetProperty("errorCode").GetString());
        Assert.Equal("Đặt lại mật khẩu không thành công. Mã xác thực không hợp lệ hoặc đã hết hạn.", doc.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Given_NonexistentEmail_When_ResetPassword_Then_RejectedWith422AndExactSameContractAsInvalidToken()
    {
        var nonexistentEmail = $"nonexistent_{Guid.NewGuid():N}@test.com";
        var resetRes = await Client.PostAsJsonAsync("/api/v1/auth/reset-password", new
        {
            email = nonexistentEmail,
            token = "dummy-token-xyz",
            newPassword = "ValidPassword@123"
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resetRes.StatusCode);
        var resetJson = await resetRes.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(resetJson);
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("RESET_PASSWORD_FAILED", doc.RootElement.GetProperty("errorCode").GetString());
        Assert.Equal("Đặt lại mật khẩu không thành công. Mã xác thực không hợp lệ hoặc đã hết hạn.", doc.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Given_InactiveUser_When_ResetPassword_Then_RejectedWith422AndExactSameContractAsInvalidToken()
    {
        var email = await CreateUniqueUserAsync(isActive: false);
        var resetRes = await Client.PostAsJsonAsync("/api/v1/auth/reset-password", new
        {
            email,
            token = "dummy-token-xyz",
            newPassword = "ValidPassword@123"
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resetRes.StatusCode);
        var resetJson = await resetRes.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(resetJson);
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("RESET_PASSWORD_FAILED", doc.RootElement.GetProperty("errorCode").GetString());
        Assert.Equal("Đặt lại mật khẩu không thành công. Mã xác thực không hợp lệ hoặc đã hết hạn.", doc.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Given_ShortPassword_When_ResetPassword_Then_RejectedWith400Validation()
    {
        var email = await CreateUniqueUserAsync();
        var resetRes = await Client.PostAsJsonAsync("/api/v1/auth/reset-password", new
        {
            email,
            token = "some-token",
            newPassword = "Pass1"
        });
        Assert.Equal(HttpStatusCode.BadRequest, resetRes.StatusCode);
    }

    [Fact]
    public async Task Given_NonexistentEmail_When_ForgotPassword_Then_ReturnsGenericResponseWithoutTokenAndNoError()
    {
        var nonexistentEmail = $"nonexistent_{Guid.NewGuid():N}@test.com";
        var response = await Client.PostAsJsonAsync("/api/v1/auth/forgot-password", new { email = nonexistentEmail });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("Nếu email tồn tại trong hệ thống, hướng dẫn đặt lại mật khẩu đã được xử lý.", doc.RootElement.GetProperty("message").GetString());

        var data = doc.RootElement.GetProperty("data");
        Assert.True(data.TryGetProperty("resetToken", out var tokenProp));
        Assert.Equal(JsonValueKind.Null, tokenProp.ValueKind);
    }

    [Fact]
    public async Task Given_InactiveUser_When_ForgotPassword_Then_ReturnsGenericResponseWithoutToken()
    {
        var email = await CreateUniqueUserAsync(isActive: false);
        var response = await Client.PostAsJsonAsync("/api/v1/auth/forgot-password", new { email });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("Nếu email tồn tại trong hệ thống, hướng dẫn đặt lại mật khẩu đã được xử lý.", doc.RootElement.GetProperty("message").GetString());

        var data = doc.RootElement.GetProperty("data");
        Assert.True(data.TryGetProperty("resetToken", out var tokenProp));
        Assert.Equal(JsonValueKind.Null, tokenProp.ValueKind);
    }

    [Fact]
    public async Task Given_ProductionEnvironment_When_ForgotPassword_Then_TokenIsNotExposed()
    {
        var email = await CreateUniqueUserAsync();

        using var prodFactory = Factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
        });

        var hostEnvironment = prodFactory.Services.GetRequiredService<IHostEnvironment>();
        Assert.Equal("Production", hostEnvironment.EnvironmentName);
        Assert.True(hostEnvironment.IsProduction());

        var prodClient = prodFactory.CreateClient();
        var response = await prodClient.PostAsJsonAsync("/api/v1/auth/forgot-password", new { email });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("Nếu email tồn tại trong hệ thống, hướng dẫn đặt lại mật khẩu đã được xử lý.", doc.RootElement.GetProperty("message").GetString());

        var data = doc.RootElement.GetProperty("data");
        Assert.True(data.TryGetProperty("resetToken", out var tokenProp));
        Assert.Equal(JsonValueKind.Null, tokenProp.ValueKind);
    }

    [Theory]
    [InlineData("/api/v1/admin/users")]
    [InlineData("/api/v1/pharmacy/dashboard")]
    [InlineData("/api/v1/doctor/appointments")]
    [InlineData("/api/v1/appointments/my")]
    [InlineData("/api/v1/reception/stats")]
    public async Task Given_ProtectedEndpoints_When_NoToken_Then_Returns401(string url)
    {
        Client.DefaultRequestHeaders.Authorization = null;
        var response = await Client.GetAsync(url);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/admin/users")]
    [InlineData("/api/v1/pharmacy/dashboard")]
    [InlineData("/api/v1/doctor/appointments")]
    [InlineData("/api/v1/reception/stats")]
    public async Task Given_PatientToken_When_AccessingRestrictedEndpoints_Then_Returns403(string url)
    {
        await AuthenticateAsync("pat1@test.com");
        var response = await Client.GetAsync(url);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/admin/users")]
    [InlineData("/api/v1/pharmacy/dashboard")]
    [InlineData("/api/v1/appointments/my")]
    public async Task Given_DoctorToken_When_AccessingNonDoctorEndpoints_Then_Returns403(string url)
    {
        await AuthenticateAsync("doc@test.com");
        var response = await Client.GetAsync(url);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/admin/users")]
    [InlineData("/api/v1/doctor/appointments")]
    [InlineData("/api/v1/appointments/my")]
    public async Task Given_PharmacistToken_When_AccessingNonPharmacyEndpoints_Then_Returns403(string url)
    {
        await AuthenticateAsync("pharm@test.com");
        var response = await Client.GetAsync(url);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
