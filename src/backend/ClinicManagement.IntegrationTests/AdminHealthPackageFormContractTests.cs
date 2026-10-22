using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace ClinicManagement.IntegrationTests;

public class AdminHealthPackageFormContractTests(CustomWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task Admin_form_payload_with_target_audience_and_service_list_creates_and_updates_the_package()
    {
        await AuthenticateAsync("admin@test.com");
        var code = $"PKG-FORM-{Guid.NewGuid():N}"[..20];

        using var created = await Client.PostAsJsonAsync("/api/v1/admin/health-packages", new
        {
            code, name = "Gói kiểm thử form", description = "Mô tả gói", targetAudience = "Người lớn",
            price = 1500000, includedServices = new[] { "Khám nội", "Xét nghiệm máu" }, imageUrl = "", isActive = true, sortOrder = 0
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var createdJson = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var data = createdJson.RootElement.GetProperty("data");
        var id = data.GetProperty("id").GetInt64();
        Assert.Equal("Người lớn", data.GetProperty("targetAudience").GetString());
        Assert.Equal(["Khám nội", "Xét nghiệm máu"], data.GetProperty("includedServices").EnumerateArray().Select(item => item.GetString()));

        using var updated = await Client.PutAsJsonAsync($"/api/v1/admin/health-packages/{id}", new
        {
            name = "Gói kiểm thử form", description = "Mô tả gói", targetAudience = "Phụ nữ",
            price = 1600000, includedServices = new[] { "Khám phụ khoa" }, imageUrl = "", isActive = true, sortOrder = 0
        });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

        using var list = await Client.GetAsync($"/api/v1/admin/health-packages?search={code}");
        list.EnsureSuccessStatusCode();
        using var listJson = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        var item = Assert.Single(listJson.RootElement.GetProperty("data").GetProperty("items").EnumerateArray());
        Assert.Equal("Phụ nữ", item.GetProperty("targetAudience").GetString());
        Assert.Equal(["Khám phụ khoa"], item.GetProperty("includedServices").EnumerateArray().Select(entry => entry.GetString()));
    }
}
