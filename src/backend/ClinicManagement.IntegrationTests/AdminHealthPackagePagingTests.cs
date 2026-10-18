using System.Net;
using System.Text.Json;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicManagement.IntegrationTests;

public class AdminHealthPackagePagingTests(CustomWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    private async Task<(string Token, HealthPackage[] Packages)> CreatePackages()
    {
        var token = Guid.NewGuid().ToString("N");
        var packages = Enumerable.Range(0, 5).Select(index => new HealthPackage
        {
            Code = $"PK-{token}-{(index == 0 ? "CodeOnly" : index.ToString())}",
            Name = $"NameOnly-{token}-{(index == 0 ? "Heart screening" : $"Wellness {index}")}",
            TargetAudience = "Adults", Description = "Admin health package paging fixture",
            Price = 100000m + index, IsActive = index % 2 == 0,
            CreatedAt = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(index)
        }).ToArray();
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.HealthPackages.AddRange(packages);
        await db.SaveChangesAsync();
        return (token, packages);
    }

    private async Task<JsonElement> GetPage(string query = "")
    {
        var response = await Client.GetAsync($"/api/v1/admin/health-packages{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(document.RootElement.GetProperty("success").GetBoolean());
        return document.RootElement.GetProperty("data").Clone();
    }

    private static long[] ItemIds(JsonElement data) => data.GetProperty("items").EnumerateArray()
        .Select(item => item.GetProperty("id").GetInt64()).ToArray();

    [Fact]
    public async Task AdminListReturnsPagedEnvelopeAndDefaultPageSize()
    {
        var fixture = await CreatePackages();
        await AuthenticateAsync("admin@test.com");
        var data = await GetPage($"?search={fixture.Token}");
        Assert.Equal(JsonValueKind.Object, data.ValueKind);
        Assert.Equal(JsonValueKind.Array, data.GetProperty("items").ValueKind);
        Assert.Equal(5, data.GetProperty("totalItems").GetInt32());
        Assert.Equal(1, data.GetProperty("page").GetInt32());
        Assert.Equal(10, data.GetProperty("pageSize").GetInt32());
        Assert.Equal(1, data.GetProperty("totalPages").GetInt32());
        Assert.Equal(fixture.Packages.Reverse().Select(package => package.Id), ItemIds(data));
        var unfiltered = await GetPage();
        Assert.True(unfiltered.GetProperty("totalItems").GetInt32() >= 5);
        Assert.Equal(1, unfiltered.GetProperty("page").GetInt32());
        Assert.Equal(10, unfiltered.GetProperty("pageSize").GetInt32());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AdminSearchMatchesNameOrCodeSubstringIgnoringCase(bool searchName)
    {
        var fixture = await CreatePackages();
        await AuthenticateAsync("admin@test.com");
        var term = searchName ? $"mEoNlY-{fixture.Token.ToUpperInvariant()}-hEaRt" : $"k-{fixture.Token.ToUpperInvariant()}-cOdEoNlY";
        var data = await GetPage($"?search={Uri.EscapeDataString($"  {term}  ")}");
        Assert.Equal(1, data.GetProperty("totalItems").GetInt32());
        Assert.Equal(new[] { fixture.Packages[0].Id }, ItemIds(data));
    }

    [Theory]
    [InlineData(true, 3)]
    [InlineData(false, 2)]
    public async Task AdminListFiltersBothActiveAndInactivePackages(bool isActive, int expectedCount)
    {
        var fixture = await CreatePackages();
        await AuthenticateAsync("admin@test.com");
        var data = await GetPage($"?search={fixture.Token}&isActive={isActive}");
        Assert.Equal(expectedCount, data.GetProperty("totalItems").GetInt32());
        Assert.All(data.GetProperty("items").EnumerateArray(), item => Assert.Equal(isActive, item.GetProperty("isActive").GetBoolean()));
        Assert.Equal(fixture.Packages.Reverse().Where(package => package.IsActive == isActive).Select(package => package.Id), ItemIds(data));
    }

    [Fact]
    public async Task AdminListPaginatesAfterFilteringInDescendingCreationOrder()
    {
        var fixture = await CreatePackages();
        await AuthenticateAsync("admin@test.com");
        var ids = new List<long>();
        for (var page = 1; page <= 3; page++)
        {
            var data = await GetPage($"?search={fixture.Token}&page={page}&pageSize=2");
            Assert.Equal(5, data.GetProperty("totalItems").GetInt32());
            Assert.Equal(page, data.GetProperty("page").GetInt32());
            Assert.Equal(2, data.GetProperty("pageSize").GetInt32());
            Assert.Equal(3, data.GetProperty("totalPages").GetInt32());
            Assert.Equal(page == 3 ? 1 : 2, ItemIds(data).Length);
            ids.AddRange(ItemIds(data));
        }
        Assert.Equal(fixture.Packages.Reverse().Select(package => package.Id), ids);
        var filtered = await GetPage($"?search={fixture.Token}&isActive=true&page=2&pageSize=2");
        Assert.Equal(3, filtered.GetProperty("totalItems").GetInt32());
        Assert.Equal(new[] { fixture.Packages[0].Id }, ItemIds(filtered));
        var pastEnd = await GetPage($"?search={fixture.Token}&page=4&pageSize=2");
        Assert.Equal(5, pastEnd.GetProperty("totalItems").GetInt32());
        Assert.Empty(ItemIds(pastEnd));
    }

    [Theory]
    [InlineData(0, 0, 1, 1)]
    [InlineData(-10, -10, 1, 1)]
    [InlineData(1, 101, 1, 100)]
    [InlineData(int.MaxValue, int.MaxValue, int.MaxValue, 100)]
    public async Task AdminListClampsPageAndPageSizeWithoutOverflow(int page, int pageSize, int expectedPage, int expectedPageSize)
    {
        var fixture = await CreatePackages();
        await AuthenticateAsync("admin@test.com");
        var data = await GetPage($"?search={fixture.Token}&page={page}&pageSize={pageSize}");
        Assert.Equal(expectedPage, data.GetProperty("page").GetInt32());
        Assert.Equal(expectedPageSize, data.GetProperty("pageSize").GetInt32());
        Assert.Equal(5, data.GetProperty("totalItems").GetInt32());
        var expectedIds = expectedPage == int.MaxValue ? Array.Empty<long>()
            : fixture.Packages.Reverse().Take(expectedPageSize).Select(package => package.Id).ToArray();
        Assert.Equal(expectedIds, ItemIds(data));
    }

    [Fact]
    public async Task AdminListReturnsEmptyPagedShapeWhenSearchHasNoMatches()
    {
        await AuthenticateAsync("admin@test.com");
        var data = await GetPage($"?search=missing-{Guid.NewGuid():N}&page=2&pageSize=3");
        Assert.Empty(ItemIds(data));
        Assert.Equal(0, data.GetProperty("totalItems").GetInt32());
        Assert.Equal(0, data.GetProperty("totalPages").GetInt32());
        Assert.Equal(2, data.GetProperty("page").GetInt32());
        Assert.Equal(3, data.GetProperty("pageSize").GetInt32());
    }

    [Theory]
    [InlineData("pat1@test.com")]
    [InlineData("rec@test.com")]
    [InlineData("doc@test.com")]
    [InlineData("pharm@test.com")]
    [InlineData("tech@test.com")]
    public async Task NonAdminCannotReadTheAdminPackageList(string email)
    {
        await AuthenticateAsync(email);
        var response = await Client.GetAsync("/api/v1/admin/health-packages?search=test&page=1&pageSize=10");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
