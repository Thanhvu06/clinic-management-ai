using System.Net;
using ClinicManagement.Api;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.IntegrationTests;

public sealed class MedicineImageSeederFactory : CustomWebApplicationFactory
{
    public string RootPath { get; } = Path.Combine(Path.GetTempPath(), $"medicine-image-seeder-{Guid.NewGuid():N}");
    public string SourcePath => Path.Combine(RootPath, "SeedData", "medicine-images");
    public string ImagesPath => Path.Combine(RootPath, "uploads", "medicines");

    public MedicineImageSeederFactory() => Directory.CreateDirectory(SourcePath);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseContentRoot(RootPath);
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Storage:MedicineImagesPath"] = ImagesPath,
                ["Jwt:ExpiryMinutes"] = "60",
                ["DemoSeed:Enabled"] = "true"
            }));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(RootPath)) Directory.Delete(RootPath, true);
    }
}

public sealed class MedicineImageSeederTests : IDisposable
{
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII=");
    private readonly MedicineImageSeederFactory _factory = new();
    private readonly HttpClient _client;

    public MedicineImageSeederTests() => _client = _factory.CreateClient();

    private AppDbContext Db(IServiceScope scope) => scope.ServiceProvider.GetRequiredService<AppDbContext>();
    private Task SeedAsync(AppDbContext db) => MedicineImageSeeder.SeedAsync(db,
        _factory.Services.GetRequiredService<MedicineImageStorage>(),
        _factory.Services.GetRequiredService<IWebHostEnvironment>(),
        _factory.Services.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(MedicineImageSeederTests)),
        CancellationToken.None);

    private static async Task<Medicine> AddMedicineAsync(AppDbContext db, string code, string? imagePath = null)
    {
        var medicine = new Medicine { Code = code, Name = code, Unit = "Viên", ImagePath = imagePath };
        db.Medicines.Add(medicine);
        await db.SaveChangesAsync();
        return medicine;
    }

    [Fact]
    public async Task SeedsMissingImageAndServesPngThroughMediaEndpoint()
    {
        using var scope = _factory.Services.CreateScope();
        var db = Db(scope);
        var medicine = await AddMedicineAsync(db, "MED01");
        await File.WriteAllBytesAsync(Path.Combine(_factory.SourcePath, "MED01.png"), Png);
        var before = DateTime.UtcNow;

        await SeedAsync(db);
        await db.Entry(medicine).ReloadAsync();

        Assert.Matches(@"^\d+-[a-f0-9]{32}\.png$", medicine.ImagePath!);
        Assert.StartsWith($"{medicine.Id}-", medicine.ImagePath);
        Assert.True(medicine.UpdatedAt >= before);
        Assert.Equal(Png, await File.ReadAllBytesAsync(Path.Combine(_factory.ImagesPath, medicine.ImagePath!)));
        var response = await _client.GetAsync($"/media/medicines/{medicine.ImagePath}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(Png, await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task KeepsExistingAdminImageAndUpdatedAt()
    {
        using var scope = _factory.Services.CreateScope();
        var db = Db(scope);
        var name = $"1-{Guid.NewGuid():N}.png";
        var medicine = await AddMedicineAsync(db, "MED01", name);
        await File.WriteAllBytesAsync(Path.Combine(_factory.ImagesPath, name), Png);
        await File.WriteAllBytesAsync(Path.Combine(_factory.SourcePath, "MED01.png"), Png);

        await SeedAsync(db);
        await db.Entry(medicine).ReloadAsync();

        Assert.Equal(name, medicine.ImagePath);
        Assert.Null(medicine.UpdatedAt);
        Assert.Single(Directory.GetFiles(_factory.ImagesPath));
    }

    [Fact]
    public async Task ReplacesImagePathWhoseFileIsMissing()
    {
        using var scope = _factory.Services.CreateScope();
        var db = Db(scope);
        var missing = $"1-{Guid.NewGuid():N}.png";
        var medicine = await AddMedicineAsync(db, "MED01", missing);
        await File.WriteAllBytesAsync(Path.Combine(_factory.SourcePath, "MED01.png"), Png);

        await SeedAsync(db);
        await db.Entry(medicine).ReloadAsync();

        Assert.NotEqual(missing, medicine.ImagePath);
        Assert.Matches(@"^\d+-[a-f0-9]{32}\.png$", medicine.ImagePath!);
        Assert.True(File.Exists(Path.Combine(_factory.ImagesPath, medicine.ImagePath!)));
    }

    [Fact]
    public async Task SkipsUnknownCodesInvalidNamesSignaturesAndOversizeFilesAndContinues()
    {
        using var scope = _factory.Services.CreateScope();
        var db = Db(scope);
        foreach (var code in new[] { "MED01", "MED02", "MED03", "MED04", "MED05" }) await AddMedicineAsync(db, code);
        await File.WriteAllBytesAsync(Path.Combine(_factory.SourcePath, "abc.png"), Png);
        await File.WriteAllBytesAsync(Path.Combine(_factory.SourcePath, "MED99.png"), Png);
        await File.WriteAllTextAsync(Path.Combine(_factory.SourcePath, "MED02.png"), "not an image");
        await File.WriteAllBytesAsync(Path.Combine(_factory.SourcePath, "MED03.jpg"), Png);
        await File.WriteAllBytesAsync(Path.Combine(_factory.SourcePath, "MED04.png"), new byte[MedicineImageStorage.MaxBytes + 1]);
        await File.WriteAllBytesAsync(Path.Combine(_factory.SourcePath, "med05.PNG"), Png);

        await SeedAsync(db);

        var medicines = await db.Medicines.AsNoTracking().OrderBy(m => m.Code).ToListAsync();
        Assert.All(medicines.Take(4), medicine => Assert.Null(medicine.ImagePath));
        Assert.NotNull(medicines[4].ImagePath);
        Assert.Single(Directory.GetFiles(_factory.ImagesPath));
    }

    [Fact]
    public async Task SecondRunKeepsImageNamesFileCountAndTimestamps()
    {
        using var scope = _factory.Services.CreateScope();
        var db = Db(scope);
        var medicine = await AddMedicineAsync(db, "MED01");
        await File.WriteAllBytesAsync(Path.Combine(_factory.SourcePath, "MED01.png"), Png);
        await SeedAsync(db);
        var firstName = medicine.ImagePath;
        var firstUpdatedAt = medicine.UpdatedAt;
        var firstFiles = Directory.GetFiles(_factory.ImagesPath);

        await SeedAsync(db);
        await db.Entry(medicine).ReloadAsync();

        Assert.Equal(firstName, medicine.ImagePath);
        Assert.Equal(firstUpdatedAt, medicine.UpdatedAt);
        Assert.Equal(firstFiles, Directory.GetFiles(_factory.ImagesPath));
    }

    [Fact]
    public async Task MissingSourceDirectoryReturnsWithoutChangingMedicines()
    {
        using var scope = _factory.Services.CreateScope();
        var db = Db(scope);
        var medicine = await AddMedicineAsync(db, "MED01");
        Directory.Delete(_factory.SourcePath);

        await SeedAsync(db);

        Assert.Null(medicine.ImagePath);
        Assert.Empty(Directory.GetFiles(_factory.ImagesPath));
    }

    [Fact]
    public async Task WriteFailureLeavesMedicineUnchangedWithoutThrowing()
    {
        using var scope = _factory.Services.CreateScope();
        var db = Db(scope);
        var medicine = await AddMedicineAsync(db, "MED01");
        await File.WriteAllBytesAsync(Path.Combine(_factory.SourcePath, "MED01.png"), Png);
        Directory.Delete(_factory.ImagesPath);
        await File.WriteAllTextAsync(_factory.ImagesPath, "blocks the image directory");

        await SeedAsync(db);
        await db.Entry(medicine).ReloadAsync();

        Assert.Null(medicine.ImagePath);
        Assert.Null(medicine.UpdatedAt);
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }
}
