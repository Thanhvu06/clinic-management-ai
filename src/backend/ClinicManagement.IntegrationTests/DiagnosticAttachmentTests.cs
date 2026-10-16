using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using ClinicManagement.Api;
using ClinicManagement.Application.Authentication.Interfaces;
using ClinicManagement.Application.Common.Constants;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Diagnostics.DTOs;
using ClinicManagement.Application.Diagnostics.Interfaces;
using ClinicManagement.Application.Doctors.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Identity;
using ClinicManagement.Infrastructure.Diagnostics;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.IntegrationTests;

public class DiagnosticAttachmentFactory : CustomWebApplicationFactory
{
    public string AttachmentsPath { get; } = Path.Combine(Path.GetTempPath(), $"diagnostic-attachments-{Guid.NewGuid():N}");
    public FailAttachmentSaveInterceptor SaveFailure { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
            new Dictionary<string, string?> { ["Storage:DiagnosticAttachmentsPath"] = AttachmentsPath }));
        builder.ConfigureServices(services => services.AddDbContext<AppDbContext>(options => options.AddInterceptors(SaveFailure)));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(AttachmentsPath)) Directory.Delete(AttachmentsPath, true);
    }
}

public class FailAttachmentSaveInterceptor : SaveChangesInterceptor
{
    public bool FailNextAttachmentSave { get; set; }
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (FailNextAttachmentSave && eventData.Context!.ChangeTracker.Entries<DiagnosticResultAttachment>().Any(e => e.State == EntityState.Added))
        {
            FailNextAttachmentSave = false;
            throw new DbUpdateException("Simulated attachment persistence failure.");
        }
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}

// Own database factory and own actors per case; no shared seeded users or working dates.
public class DiagnosticAttachmentTests(DiagnosticAttachmentFactory factory) : IClassFixture<DiagnosticAttachmentFactory>, IDisposable
{
    private readonly HttpClient _client = factory.CreateClient();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aS1cAAAAASUVORK5CYII=");
    private sealed record Actor(Guid Id, string Email);
    private sealed record Fixture(long OrderId, long ItemId, Actor Technician, Actor OtherTechnician, Actor Doctor,
        Actor Patient, Actor OtherPatient, Actor Supervisor, Actor ReviewingDoctor, Actor Receptionist);

    private async Task<Fixture> SeedAsync(DiagnosticCategory category = DiagnosticCategory.Imaging)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        async Task<Actor> User(string role)
        {
            var email = $"att-{Guid.NewGuid():N}@test.com";
            var user = new ApplicationUser { Id = Guid.NewGuid(), Email = email, UserName = email,
                FullName = $"Attachment {role}", PhoneNumber = "09" + Random.Shared.NextInt64(100000000, 999999999), IsActive = true };
            Assert.True((await users.CreateAsync(user, "Pass@123")).Succeeded);
            Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
            return new(user.Id, email);
        }
        var tech = await User(RoleNames.DiagnosticTechnician);
        var otherTech = await User(RoleNames.DiagnosticTechnician);
        var doc = await User(RoleNames.Doctor);
        var patient = await User(RoleNames.Patient);
        var otherPatient = await User(RoleNames.Patient);
        var supervisor = await User(RoleNames.LabSupervisor);
        var reviewer = await User(RoleNames.Doctor);
        var receptionist = await User(RoleNames.Receptionist);
        var facility = new Facility { Code = "ATT-" + Guid.NewGuid().ToString("N")[..20], Name = "Attachment facility", IsActive = true };
        var otherFacility = new Facility { Code = "ATT-" + Guid.NewGuid().ToString("N")[..20], Name = "Other attachment facility", IsActive = true };
        db.Facilities.AddRange(facility, otherFacility);
        var doctor = new Doctor { UserId = doc.Id, IsActive = true };
        db.Doctors.AddRange(doctor, new Doctor { UserId = reviewer.Id, IsActive = true });
        var entityPatient = new Patient { UserId = patient.Id, FullName = "Attachment patient", MedicalRecordNumber = "ATT-" + Guid.NewGuid().ToString("N")[..20] };
        db.Patients.AddRange(entityPatient, new Patient { UserId = otherPatient.Id, FullName = "Other attachment patient", MedicalRecordNumber = "ATT-" + Guid.NewGuid().ToString("N")[..20] });
        var item = new DiagnosticOrderItem { Status = DiagnosticItemStatus.InProgress, DiagnosticService = new DiagnosticService {
            Code = "ATT-" + Guid.NewGuid().ToString("N"), Name = "Attachment service", Category = category, IsActive = true } };
        var order = new DiagnosticOrder { OrderCode = "ATT-" + Guid.NewGuid().ToString("N"), Patient = entityPatient,
            OrderingDoctor = doctor, Facility = facility, ClinicalIndication = "Attachment acceptance", Status = DiagnosticOrderStatus.InProgress,
            StartedAtUtc = DateTime.UtcNow, StartedByUserId = tech.Id, Items = new List<DiagnosticOrderItem> { item } };
        db.DiagnosticOrders.Add(order);
        await db.SaveChangesAsync();
        foreach (var (actor, role, location) in new[] { (tech, RoleNames.DiagnosticTechnician, facility),
                     (otherTech, RoleNames.DiagnosticTechnician, otherFacility), (doc, RoleNames.Doctor, facility),
                     (supervisor, RoleNames.LabSupervisor, facility), (reviewer, RoleNames.Doctor, facility) })
            db.StaffFacilityAssignments.Add(new StaffFacilityAssignment { UserId = actor.Id, Role = role, FacilityId = location.Id, IsActive = true });
        await db.SaveChangesAsync();
        await LoginAsync(tech);
        return new(order.Id, item.Id, tech, otherTech, doc, patient, otherPatient, supervisor, reviewer, receptionist);
    }

    private async Task LoginAsync(Actor actor)
    {
        using var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { emailOrPhone = actor.Email, password = "Pass@123" });
        var data = await JsonAsync(response);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", data.GetProperty("data").GetProperty("accessToken").GetString());
    }
    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    private static async Task<DiagnosticOrderDto> ReadAsync(HttpResponseMessage response)
    {
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await JsonAsync(response)).GetProperty("data").Deserialize<DiagnosticOrderDto>(JsonOptions)!;
    }
    private static string Route(Fixture f) => $"/api/v1/diagnostics/orders/{f.OrderId}/items/{f.ItemId}/attachments";
    private static string Content(long id) => $"/api/v1/diagnostic-attachments/{id}/content";
    private Task<HttpResponseMessage> UploadAsync(Fixture f, byte[]? bytes = null, string name = "scan.png")
    {
        var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(bytes ?? Png);
        content.Headers.ContentType = new MediaTypeHeaderValue("text/plain"); // Deliberately untrusted client metadata.
        form.Add(content, "file", name);
        return SendAsync();
        async Task<HttpResponseMessage> SendAsync() { using (form) return await _client.PostAsync(Route(f), form); }
    }
    private async Task MutateAsync(Fixture f, Action<DiagnosticOrder, DiagnosticOrderItem> mutate)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var order = await db.DiagnosticOrders.Include(o => o.Items).ThenInclude(i => i.DiagnosticService).SingleAsync(o => o.Id == f.OrderId);
        mutate(order, order.Items.Single());
        await db.SaveChangesAsync();
    }
    private static async Task ErrorAsync(HttpResponseMessage response, string code)
    {
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(code, (await JsonAsync(response)).GetProperty("errorCode").GetString());
    }

    [Theory]
    [InlineData("jpg", "image/jpeg")]
    [InlineData("png", "image/png")]
    [InlineData("webp", "image/webp")]
    [InlineData("pdf", "application/pdf")]
    public async Task UploadDetectsMagicBytes_StoresGuidName_AndServesPrivateContent(string extension, string contentType)
    {
        var f = await SeedAsync();
        var bytes = extension switch { "jpg" => new byte[] { 0xff, 0xd8, 0xff, 0xe0, 0, 0, 0xff, 0xd9 },
            "webp" => "RIFF\u0018\0\0\0WEBPVP8 "u8.ToArray(), "pdf" => "%PDF-1.7\n%%EOF"u8.ToArray(), _ => Png };
        var response = await UploadAsync(f, bytes, "../../ảnh chụp.exe");
        var order = await ReadAsync(response);
        var attachment = Assert.Single(order.Items.Single().Attachments);
        Assert.Equal("ảnh chụp.exe", attachment.OriginalFileName);
        Assert.Equal(contentType, attachment.ContentType);
        Assert.Equal(bytes.Length, attachment.SizeBytes);
        Assert.Equal(DateTimeKind.Utc, attachment.UploadedAtUtc.Kind);
        Assert.Equal("Attachment DiagnosticTechnician", attachment.UploadedByUserName);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.DiagnosticResultAttachments.SingleAsync(a => a.Id == attachment.Id);
        Assert.Matches($"^[a-f0-9]{{32}}\\.{extension}$", stored.StoredFileName);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(factory.AttachmentsPath, stored.StoredFileName)));
        var json = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("storedFileName", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(factory.AttachmentsPath, json);
        using var file = await _client.GetAsync(Content(attachment.Id));
        Assert.Equal(HttpStatusCode.OK, file.StatusCode);
        Assert.Equal(contentType, file.Content.Headers.ContentType!.MediaType);
        Assert.Equal("nosniff", Assert.Single(file.Headers.GetValues("X-Content-Type-Options")));
        Assert.True(file.Headers.CacheControl!.NoStore);
        Assert.Equal("inline", file.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal("ảnh chụp.exe", file.Content.Headers.ContentDisposition.FileNameStar);
        Assert.Equal(bytes, await file.Content.ReadAsByteArrayAsync());
        Assert.True(await db.SystemAuditLogs.AnyAsync(a => a.Action == "DiagnosticAttachmentUploaded" && a.EntityName == "DiagnosticOrder" && a.EntityId == f.OrderId.ToString()));
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Content(attachment.Id))).StatusCode);
        foreach (var route in new[] { "/uploads/diagnostics/", "/media/diagnostics/", "/diagnostics/", "/media/medicines/" })
            Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(route + stored.StoredFileName)).StatusCode);
    }

    [Fact]
    public async Task InvalidTypeAndOversizedFilesAreRejected()
    {
        var f = await SeedAsync();
        await ErrorAsync(await UploadAsync(f, "plain text pretending to be jpeg"u8.ToArray(), "fake.jpg"), "INVALID_ATTACHMENT_TYPE");
        await ErrorAsync(await UploadAsync(f, new byte[10 * 1024 * 1024 + 1]), "ATTACHMENT_TOO_LARGE");
        using var scope = factory.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<AppDbContext>().DiagnosticResultAttachments.AnyAsync(a => a.DiagnosticOrderItemId == f.ItemId));
    }

    [Fact]
    public async Task StreamLimitIsEnforcedEvenWhenDeclaredLengthIsSmall_AndNamesAreSanitized()
    {
        using var scope = factory.Services.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<IDiagnosticAttachmentStorage>();
        using var oversized = new MemoryStream(new byte[DiagnosticAttachmentStorage.MaxBytes + 1]);
        var failure = await Assert.ThrowsAsync<BusinessException>(() => storage.SaveAsync(oversized, 1, "scan.png", default));
        Assert.Equal("ATTACHMENT_TOO_LARGE", failure.ErrorCode);
        foreach (var (name, expected) in new[] { ("../../\u0001\r\n", "tep-dinh-kem.png"), ("C:\\patient\\scan\u0001.png", "scan.png"), (new string('a', 220), new string('a', 200)) })
        {
            using var input = new MemoryStream(Png);
            var stored = await storage.SaveAsync(input, Png.Length, name, default);
            Assert.Equal(expected, stored.OriginalFileName);
            storage.DeleteFile("../" + stored.StoredFileName);
            Assert.True(File.Exists(Path.Combine(factory.AttachmentsPath, stored.StoredFileName)));
            storage.DeleteFile(stored.StoredFileName);
        }
    }

    [Theory]
    [InlineData(DiagnosticCategory.Imaging)]
    [InlineData(DiagnosticCategory.Ultrasound)]
    public async Task EleventhFileIsRejected(DiagnosticCategory category)
    {
        var f = await SeedAsync(category);
        for (var i = 0; i < 10; i++) await ReadAsync(await UploadAsync(f));
        await ErrorAsync(await UploadAsync(f), "ATTACHMENT_LIMIT");
        using var scope = factory.Services.CreateScope();
        Assert.Equal(10, await scope.ServiceProvider.GetRequiredService<AppDbContext>().DiagnosticResultAttachments.CountAsync(a => a.DiagnosticOrderItemId == f.ItemId));
    }

    [Theory]
    [InlineData(DiagnosticCategory.Laboratory)]
    [InlineData(DiagnosticCategory.Other)]
    public async Task NonImagingItemsRejectAttachments(DiagnosticCategory category)
    {
        var f = await SeedAsync(category);
        await ErrorAsync(await UploadAsync(f), "ATTACHMENT_NOT_ALLOWED");
    }

    [Theory]
    [InlineData("Ordered", "INVALID_STATE")]
    [InlineData("Completed", "INVALID_STATE")]
    [InlineData("Pending", "RESULTS_LOCKED_PENDING_VERIFICATION")]
    [InlineData("CancelledItem", "ITEM_CANCELLED")]
    public async Task StateGuardsRejectBothUploadAndDelete(string state, string code)
    {
        var f = await SeedAsync();
        var attachment = Assert.Single((await ReadAsync(await UploadAsync(f))).Items.Single().Attachments);
        await MutateAsync(f, (order, item) => {
            if (state == "Pending") order.VerificationStatus = DiagnosticVerificationStatus.Pending;
            else if (state == "CancelledItem") item.Status = DiagnosticItemStatus.Cancelled;
            else order.Status = Enum.Parse<DiagnosticOrderStatus>(state);
        });
        await ErrorAsync(await UploadAsync(f), code);
        await ErrorAsync(await _client.DeleteAsync(Route(f) + "/" + attachment.Id), code);
    }

    [Fact]
    public async Task OtherFacilityAndUnsupportedRolesCannotMutateOrReadContent()
    {
        var f = await SeedAsync();
        var attachment = Assert.Single((await ReadAsync(await UploadAsync(f))).Items.Single().Attachments);
        await LoginAsync(f.OtherTechnician);
        Assert.Equal(HttpStatusCode.NotFound, (await UploadAsync(f)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.DeleteAsync(Route(f) + "/" + attachment.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync(Content(attachment.Id))).StatusCode);
        foreach (var actor in new[] { f.Doctor, f.Patient, f.Supervisor, f.Receptionist })
        {
            await LoginAsync(actor);
            Assert.Equal(HttpStatusCode.Forbidden, (await UploadAsync(f)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await _client.DeleteAsync(Route(f) + "/" + attachment.Id)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync(Content(attachment.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync(Content(long.MaxValue))).StatusCode);
    }

    [Fact]
    public async Task ContentVisibilityReusesDoctorReviewAndPatientPublicationRules()
    {
        var f = await SeedAsync();
        var attachment = Assert.Single((await ReadAsync(await UploadAsync(f))).Items.Single().Attachments);
        await LoginAsync(f.Patient);
        var hidden = await ReadAsync(await _client.GetAsync($"/api/v1/patients/me/diagnostic-orders/{f.OrderId}"));
        Assert.Empty(hidden.Items.Single().Attachments);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync(Content(attachment.Id))).StatusCode);
        await LoginAsync(f.Technician);
        var resulted = await ReadAsync(await _client.PutAsJsonAsync($"/api/v1/diagnostics/orders/{f.OrderId}/items/{f.ItemId}/result", new { resultText = "Kết quả hình ảnh bình thường" }));
        var completed = await ReadAsync(await _client.PostAsJsonAsync($"/api/v1/diagnostics/orders/{f.OrderId}/complete", new { rowVersion = resulted.RowVersion }));
        Assert.Equal("Completed", completed.Status);
        await LoginAsync(f.Doctor);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync(Content(attachment.Id))).StatusCode);
        await LoginAsync(f.Patient);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync(Content(attachment.Id))).StatusCode);
        await LoginAsync(f.Doctor);
        await ReadAsync(await _client.PostAsJsonAsync($"/api/v1/doctor/diagnostic-orders/{f.OrderId}/review", new { rowVersion = completed.RowVersion }));
        await LoginAsync(f.Patient);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync(Content(attachment.Id))).StatusCode);
        var published = await ReadAsync(await _client.GetAsync($"/api/v1/patients/me/diagnostic-orders/{f.OrderId}"));
        Assert.Equal(attachment.Id, Assert.Single(published.Items.Single().Attachments).Id);
        await LoginAsync(f.OtherPatient);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync(Content(attachment.Id))).StatusCode);
    }

    [Fact]
    public async Task MixedPendingOrderRedactsAttachmentsForOrderingDoctor_AndUsesVerificationScopeForReviewers()
    {
        var f = await SeedAsync();
        var attachment = Assert.Single((await ReadAsync(await UploadAsync(f))).Items.Single().Attachments);
        await MutateAsync(f, (order, _) => {
            order.VerificationStatus = DiagnosticVerificationStatus.Pending;
            order.Items.Add(new DiagnosticOrderItem { DiagnosticService = new DiagnosticService { Code = "ATT-LAB-" + Guid.NewGuid().ToString("N"), Name = "Own lab service", Category = DiagnosticCategory.Laboratory, SpecimenType = "Máu" } });
        });
        await LoginAsync(f.Doctor);
        var hidden = await ReadAsync(await _client.GetAsync($"/api/v1/doctor/diagnostic-orders/{f.OrderId}"));
        Assert.All(hidden.Items, item => { Assert.Null(item.Result); Assert.Empty(item.Attachments); });
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync(Content(attachment.Id))).StatusCode);
        foreach (var actor in new[] { f.Supervisor, f.ReviewingDoctor })
        {
            await LoginAsync(actor);
            Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync(Content(attachment.Id))).StatusCode);
        }
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var assignments = await db.StaffFacilityAssignments.Where(a => a.UserId == f.Supervisor.Id || a.UserId == f.ReviewingDoctor.Id).ToListAsync();
        foreach (var assignment in assignments) assignment.IsActive = false;
        await db.SaveChangesAsync();
        foreach (var actor in new[] { f.Supervisor, f.ReviewingDoctor })
        {
            await LoginAsync(actor);
            Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync(Content(attachment.Id))).StatusCode);
        }
    }

    [Fact]
    public async Task DeleteRemovesRowAndFile_AndWritesAudit()
    {
        var f = await SeedAsync();
        var attachment = Assert.Single((await ReadAsync(await UploadAsync(f))).Items.Single().Attachments);
        string storedName;
        using (var scope = factory.Services.CreateScope()) storedName = (await scope.ServiceProvider.GetRequiredService<AppDbContext>().DiagnosticResultAttachments.FindAsync(attachment.Id))!.StoredFileName;
        var updated = await ReadAsync(await _client.DeleteAsync(Route(f) + "/" + attachment.Id));
        Assert.Empty(updated.Items.Single().Attachments);
        Assert.False(File.Exists(Path.Combine(factory.AttachmentsPath, storedName)));
        using var verification = factory.Services.CreateScope();
        var db = verification.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Null(await db.DiagnosticResultAttachments.FindAsync(attachment.Id));
        Assert.True(await db.SystemAuditLogs.AnyAsync(a => a.Action == "DiagnosticAttachmentDeleted" && a.EntityName == "DiagnosticOrder" && a.EntityId == f.OrderId.ToString() && a.UserId == f.Technician.Id));
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync(Content(attachment.Id))).StatusCode);
    }

    [Fact]
    public async Task DatabaseFailureCleansUpNewlyWrittenFile()
    {
        var f = await SeedAsync();
        Directory.CreateDirectory(factory.AttachmentsPath);
        var before = Directory.GetFiles(factory.AttachmentsPath).Order().ToArray();
        factory.SaveFailure.FailNextAttachmentSave = true;
        Assert.Equal(HttpStatusCode.InternalServerError, (await UploadAsync(f)).StatusCode);
        Assert.Equal(before, Directory.GetFiles(factory.AttachmentsPath).Order().ToArray());
        using var scope = factory.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<AppDbContext>().DiagnosticResultAttachments.AnyAsync(a => a.DiagnosticOrderItemId == f.ItemId));
    }

    private sealed class PairedStorage(IDiagnosticAttachmentStorage inner) : IDiagnosticAttachmentStorage
    {
        private readonly TaskCompletionSource _bothWritten = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _written;
        public async Task<StoredDiagnosticAttachment> SaveAsync(Stream input, long length, string name, CancellationToken cancellationToken)
        {
            var stored = await inner.SaveAsync(input, length, name, cancellationToken);
            if (Interlocked.Increment(ref _written) == 2) _bothWritten.TrySetResult();
            await _bothWritten.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            return stored;
        }
        public Stream? OpenRead(string name) => inner.OpenRead(name);
        public void DeleteFile(string name) => inner.DeleteFile(name);
    }

    [Fact]
    public async Task ConcurrentUploadsCannotExceedTenFiles_AndLosingUploadIsCleanedUp()
    {
        var f = await SeedAsync();
        for (var i = 0; i < 9; i++) await ReadAsync(await UploadAsync(f));
        using var firstScope = factory.Services.CreateScope();
        using var secondScope = factory.Services.CreateScope();
        var storage = new PairedStorage(firstScope.ServiceProvider.GetRequiredService<IDiagnosticAttachmentStorage>());
        var user = Mock.Of<ICurrentUserService>(current => current.UserId == f.Technician.Id);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, f.Technician.Id.ToString()), new Claim(ClaimTypes.Role, RoleNames.DiagnosticTechnician) }, "Test"));
        var http = Mock.Of<IHttpContextAccessor>(accessor => accessor.HttpContext == new DefaultHttpContext { User = principal });
        DiagnosticWorkflowService Workflow(IServiceScope scope) => new(scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            Mock.Of<IDoctorContextService>(), user, scope.ServiceProvider.GetRequiredService<IDateTimeProvider>(),
            NullLogger<DiagnosticWorkflowService>.Instance, http, attachmentStorage: storage);
        async Task<Exception?> Upload(DiagnosticWorkflowService workflow)
        {
            using var stream = new MemoryStream(Png);
            try { await workflow.UploadAttachmentAsync(f.OrderId, f.ItemId, stream, Png.Length, "scan.png"); return null; }
            catch (Exception exception) { return exception; }
        }
        var results = await Task.WhenAll(Upload(Workflow(firstScope)), Upload(Workflow(secondScope)));
        Assert.Single(results, result => result == null);
        Assert.IsType<ConflictException>(Assert.Single(results, result => result != null));
        using var verification = factory.Services.CreateScope();
        var db = verification.ServiceProvider.GetRequiredService<AppDbContext>();
        var names = await db.DiagnosticResultAttachments.Where(a => a.DiagnosticOrderItemId == f.ItemId).Select(a => a.StoredFileName).ToListAsync();
        Assert.Equal(10, names.Count);
        Assert.All(names, name => Assert.True(File.Exists(Path.Combine(factory.AttachmentsPath, name))));
        var allStoredNames = await db.DiagnosticResultAttachments.Select(a => a.StoredFileName).ToListAsync();
        Assert.All(Directory.GetFiles(factory.AttachmentsPath), path => Assert.Contains(Path.GetFileName(path), allStoredNames));
    }

    [Fact]
    public async Task AttachmentsRemainOptionalForCompletion()
    {
        var f = await SeedAsync();
        await ReadAsync(await _client.PutAsJsonAsync($"/api/v1/diagnostics/orders/{f.OrderId}/items/{f.ItemId}/result", new { resultText = "Không có tệp, kết quả hợp lệ" }));
        var completed = await ReadAsync(await _client.PostAsJsonAsync($"/api/v1/diagnostics/orders/{f.OrderId}/complete", new { }));
        Assert.Equal("Completed", completed.Status);
        Assert.Empty(completed.Items.Single().Attachments);
    }

    public void Dispose() => _client.Dispose();
}
