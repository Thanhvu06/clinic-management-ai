using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClinicManagement.Application.Diagnostics.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ClinicManagement.IntegrationTests;

public class DiagnosticSpecimenWorkflowTests : IntegrationTestBase
{
    public DiagnosticSpecimenWorkflowTests(CustomWebApplicationFactory factory) : base(factory) { }
    private static int _offset = 360;

    private async Task<DiagnosticOrderDto> CreateOrderAsync(bool requiresSpecimen = true, bool parameters = false)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var facilityId = await db.StaffFacilityAssignments.Where(a => a.UserId == TechnicianId && a.IsActive).Select(a => a.FacilityId).FirstAsync();
        var slot = await CreateAvailableSlotAsync(DoctorEntityId, GetFutureWorkingDate(Interlocked.Increment(ref _offset)), new TimeOnly(8, 0), new TimeOnly(8, 30));
        var service = new DiagnosticService
        {
            Code = $"SP-{Guid.NewGuid():N}", Name = "Dịch vụ mẫu kiểm thử", Category = DiagnosticCategory.Laboratory,
            SpecimenType = requiresSpecimen ? "Máu toàn phần" : null, ContainerType = requiresSpecimen ? "Ống EDTA (nắp tím)" : null
        };
        if (parameters) service.Parameters.Add(new DiagnosticServiceParameter { Code = "WBC", Name = "Bạch cầu", Unit = "10^9/L", RefLow = 4, RefHigh = 10, CriticalLow = 2, CriticalHigh = 30 });
        var appointment = new Appointment
        {
            AppointmentCode = $"SP-{Guid.NewGuid():N}", PatientId = Patient1EntityId, DoctorId = DoctorEntityId,
            SpecialtyId = SpecialtyEntityId, FacilityId = facilityId, AppointmentSlotId = slot.Id, AppointmentDate = slot.SlotDate,
            StartTime = slot.StartTime, EndTime = slot.EndTime, Status = AppointmentStatus.InConsultation
        };
        var order = new DiagnosticOrder
        {
            OrderCode = $"DX-SP-{Guid.NewGuid():N}", PatientId = Patient1EntityId, OrderingDoctorId = DoctorEntityId,
            FacilityId = facilityId, Appointment = appointment, ClinicalIndication = "Kiểm thử quy trình mẫu",
            Items = new List<DiagnosticOrderItem> { new() { DiagnosticService = service } }
        };
        db.DiagnosticOrders.Add(order);
        await db.SaveChangesAsync();
        await AuthenticateAsync("tech@test.com");
        return await ReadOrderAsync(await Client.GetAsync($"/api/v1/diagnostics/orders/{order.Id}"));
    }

    private static async Task<DiagnosticOrderDto> ReadOrderAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, text);
        return JsonSerializer.Deserialize<DiagnosticOrderDto>(JsonDocument.Parse(text).RootElement.GetProperty("data").GetRawText(), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }
    private Task<HttpResponseMessage> StepAsync(DiagnosticOrderDto order, string step, object body) =>
        Client.PostAsJsonAsync($"/api/v1/diagnostics/orders/{order.Id}/items/{order.Items[0].Id}/specimen/{step}", body);
    private async Task<DiagnosticOrderDto> StartAsync(DiagnosticOrderDto order) =>
        await ReadOrderAsync(await Client.PostAsJsonAsync($"/api/v1/diagnostics/orders/{order.Id}/start", new { rowVersion = order.RowVersion }));
    private async Task<DiagnosticOrderDto> CollectAsync(DiagnosticOrderDto order) =>
        await ReadOrderAsync(await StepAsync(order, "collect", new { rowVersion = order.Items[0].RowVersion, note = "Lấy mẫu kiểm thử" }));
    private Task<HttpResponseMessage> ResultAsync(DiagnosticOrderDto order, object body) =>
        Client.PutAsJsonAsync($"/api/v1/diagnostics/orders/{order.Id}/items/{order.Items[0].Id}/result", body);
    private static async Task AssertErrorAsync(HttpResponseMessage response, string code, HttpStatusCode status = HttpStatusCode.UnprocessableEntity)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Contains(code, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task StartActivatesOnlyDeclaredSpecimens_AndLeavesCancelledItemsUnchanged()
    {
        var order = await CreateOrderAsync();
        Assert.Equal("NotRequired", order.Items[0].SpecimenStatus);
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.DiagnosticOrderItems.Add(new DiagnosticOrderItem { DiagnosticOrderId = order.Id,
                DiagnosticService = new DiagnosticService { Code = $"TEXT-{Guid.NewGuid():N}", Name = "Siêu âm", Category = DiagnosticCategory.Ultrasound } });
            db.DiagnosticOrderItems.Add(new DiagnosticOrderItem { DiagnosticOrderId = order.Id, Status = DiagnosticItemStatus.Cancelled,
                DiagnosticService = new DiagnosticService { Code = $"CANCEL-{Guid.NewGuid():N}", Name = "Dịch vụ hủy", SpecimenType = "Máu" } });
            await db.SaveChangesAsync();
        }
        var started = await StartAsync(order);
        Assert.Equal("PendingCollection", started.Items.Single(i => i.Id == order.Items[0].Id).SpecimenStatus);
        Assert.Equal("NotRequired", started.Items.Single(i => i.ServiceName == "Siêu âm").SpecimenStatus);
        Assert.Equal("NotRequired", started.Items.Single(i => i.ServiceName == "Dịch vụ hủy").SpecimenStatus);
    }

    [Theory]
    [InlineData("PendingCollection")]
    [InlineData("Collected")]
    [InlineData("Rejected")]
    public async Task ResultIsBlockedBeforeValidationUntilReceived(string state)
    {
        var order = await StartAsync(await CreateOrderAsync());
        if (state != "PendingCollection") order = await CollectAsync(order);
        if (state == "Rejected") order = await ReadOrderAsync(await StepAsync(order, "reject", new { reason = SpecimenRejectionReason.Hemolyzed, rowVersion = order.Items[0].RowVersion }));
        await AssertErrorAsync(await ResultAsync(order, new { resultText = "", rowVersion = order.Items[0].RowVersion }), "SPECIMEN_NOT_RECEIVED");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CollectReceiveAndRecordSupportsTextAndPhaseOneValues(bool numeric)
    {
        var order = await StartAsync(await CreateOrderAsync(parameters: numeric));
        var collected = await CollectAsync(order);
        Assert.Equal($"S{order.Items[0].Id:D7}-1", collected.Items[0].SpecimenBarcode);
        Assert.Equal("Collected", collected.Items[0].SpecimenStatus);
        Assert.NotNull(collected.Items[0].CollectedAtUtc);
        Assert.False(string.IsNullOrWhiteSpace(collected.Items[0].CollectedByUserName));
        var received = await ReadOrderAsync(await StepAsync(collected, "receive", new { rowVersion = collected.Items[0].RowVersion }));
        Assert.Equal("Received", received.Items[0].SpecimenStatus);
        Assert.NotNull(received.Items[0].ReceivedAtUtc);
        object request = numeric ? new { resultText = "", values = new[] { new { parameterId = received.Items[0].Parameters[0].Id, value = 7.5m } }, rowVersion = received.Items[0].RowVersion }
            : new { resultText = "Kết quả chữ tương thích", rowVersion = received.Items[0].RowVersion };
        var saved = await ReadOrderAsync(await ResultAsync(received, request));
        Assert.Equal(numeric ? 1 : 0, saved.Items[0].Result!.Values.Count);
        Assert.Contains(numeric ? "Bạch cầu (WBC)" : "Kết quả chữ tương thích", saved.Items[0].Result!.ResultText);
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var audit = await db.SystemAuditLogs.Where(a => a.EntityId == order.Items[0].Id.ToString() && a.Action.StartsWith("Specimen")).ToListAsync();
        Assert.Equal(new[] { "SpecimenCollected", "SpecimenReceived" }, audit.OrderBy(a => a.Id).Select(a => a.Action));
        Assert.All(audit, a => Assert.Contains(collected.Items[0].SpecimenBarcode!, a.Description));
        var complete = await ReadOrderAsync(await Client.PostAsJsonAsync($"/api/v1/diagnostics/orders/{order.Id}/complete", new { rowVersion = saved.RowVersion }));
        Assert.Equal("Completed", complete.Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abcd")]
    [InlineData("     ")]
    public async Task OtherRejectionRequiresFiveTrimmedCharacters(string? note)
    {
        var order = await CollectAsync(await StartAsync(await CreateOrderAsync()));
        await AssertErrorAsync(await StepAsync(order, "reject", new { reason = SpecimenRejectionReason.Other, note, rowVersion = order.Items[0].RowVersion }), "VALIDATION_ERROR");
        var unchanged = await ReadOrderAsync(await Client.GetAsync($"/api/v1/diagnostics/orders/{order.Id}"));
        Assert.Equal("Collected", unchanged.Items[0].SpecimenStatus);
    }

    [Fact]
    public async Task RejectNotifiesOnce_RecollectRotatesBarcodeAndRetainsAuditHistory()
    {
        var order = await CollectAsync(await StartAsync(await CreateOrderAsync()));
        var rejected = await ReadOrderAsync(await StepAsync(order, "reject", new { reason = SpecimenRejectionReason.Other, note = "Ống không đúng mã", rowVersion = order.Items[0].RowVersion }));
        Assert.Equal("Rejected", rejected.Items[0].SpecimenStatus);
        Assert.Equal("Other", rejected.Items[0].RejectionReason);
        Assert.NotNull(rejected.Items[0].RejectedAtUtc);
        await AssertErrorAsync(await StepAsync(rejected, "reject", new { reason = SpecimenRejectionReason.Hemolyzed, rowVersion = rejected.Items[0].RowVersion }), "INVALID_SPECIMEN_STATE");
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var notices = await db.Notifications.Where(n => n.DedupeKey == $"diag_specimen_rejected_{order.Items[0].Id}_0").ToListAsync();
        var notice = Assert.Single(notices);
        Assert.Equal(DoctorId, notice.UserId);
        Assert.Equal(NotificationType.Diagnostic, notice.Type);
        Assert.Equal("Mẫu xét nghiệm bị từ chối — cần lấy lại", notice.Title);
        Assert.Contains(order.OrderCode, notice.Message);
        Assert.Contains(order.Items[0].ServiceName, notice.Message);
        Assert.Contains("Khác", notice.Message);
        Assert.Equal($"/doctor/appointments/{order.AppointmentId}/examination", notice.Route);
        var audit = await db.SystemAuditLogs.SingleAsync(a => a.EntityId == order.Items[0].Id.ToString() && a.Action == "SpecimenRejected");
        Assert.Contains(order.Items[0].SpecimenBarcode!, audit.Description);
        Assert.Contains("Ống không đúng mã", audit.Description);
        var recollected = await CollectAsync(rejected);
        Assert.Equal(1, recollected.Items[0].RecollectCount);
        Assert.Equal($"S{order.Items[0].Id:D7}-2", recollected.Items[0].SpecimenBarcode);
        Assert.Null(recollected.Items[0].RejectionReason);
        Assert.Null(recollected.Items[0].RejectionNote);
        Assert.Null(recollected.Items[0].RejectedAtUtc);
        Assert.Null(recollected.Items[0].RejectedByUserName);
        await ReadOrderAsync(await StepAsync(recollected, "reject", new { reason = SpecimenRejectionReason.Clotted, rowVersion = recollected.Items[0].RowVersion }));
        Assert.Equal(2, await db.Notifications.CountAsync(n => n.RelatedEntityId == order.Id.ToString() && n.Title.StartsWith("Mẫu xét nghiệm")));
        Assert.Equal(2, await db.SystemAuditLogs.CountAsync(a => a.EntityId == order.Items[0].Id.ToString() && a.Action == "SpecimenCollected"));
    }

    [Theory]
    [InlineData("collect")]
    [InlineData("receive")]
    [InlineData("reject")]
    public async Task NotRequiredCannotUseSpecimenActions(string step)
    {
        var order = await StartAsync(await CreateOrderAsync(requiresSpecimen: false));
        await AssertErrorAsync(await StepAsync(order, step, new { reason = SpecimenRejectionReason.Hemolyzed, rowVersion = order.Items[0].RowVersion }), "INVALID_SPECIMEN_STATE");
    }

    [Fact]
    public async Task InvalidTransitionsAndStaleRowVersionAreRejected()
    {
        var ordered = await CreateOrderAsync();
        await AssertErrorAsync(await StepAsync(ordered, "collect", new { rowVersion = ordered.Items[0].RowVersion }), "INVALID_SPECIMEN_STATE");
        var pending = await StartAsync(ordered);
        await AssertErrorAsync(await StepAsync(pending, "receive", new { rowVersion = pending.Items[0].RowVersion }), "INVALID_SPECIMEN_STATE");
        var collected = await CollectAsync(pending);
        await AssertErrorAsync(await StepAsync(collected, "receive", new { rowVersion = pending.Items[0].RowVersion }), "STATE_CONFLICT", HttpStatusCode.Conflict);
        await AssertErrorAsync(await StepAsync(collected, "collect", new { rowVersion = collected.Items[0].RowVersion }), "INVALID_SPECIMEN_STATE");
        var received = await ReadOrderAsync(await StepAsync(collected, "receive", new { rowVersion = collected.Items[0].RowVersion }));
        await AssertErrorAsync(await StepAsync(received, "reject", new { reason = SpecimenRejectionReason.Hemolyzed, rowVersion = received.Items[0].RowVersion }), "INVALID_SPECIMEN_STATE");
    }

    [Fact]
    public async Task OldNoSpecimenFlowStillStartsRecordsAndCompletes()
    {
        var order = await StartAsync(await CreateOrderAsync(requiresSpecimen: false));
        Assert.Equal("NotRequired", order.Items[0].SpecimenStatus);
        var saved = await ReadOrderAsync(await ResultAsync(order, new { resultText = "Kết quả cũ", rowVersion = order.Items[0].RowVersion }));
        var complete = await ReadOrderAsync(await Client.PostAsJsonAsync($"/api/v1/diagnostics/orders/{order.Id}/complete", new { rowVersion = saved.RowVersion }));
        Assert.Equal("Completed", complete.Status);
    }

    [Fact]
    public async Task ExistingInProgressItemsRemainNotRequiredDespiteServiceConfiguration()
    {
        var order = await CreateOrderAsync();
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var entity = await db.DiagnosticOrders.Include(o => o.Items).SingleAsync(o => o.Id == order.Id);
            entity.Status = DiagnosticOrderStatus.InProgress; entity.Items.Single().Status = DiagnosticItemStatus.InProgress;
            await db.SaveChangesAsync();
        }
        var old = await ReadOrderAsync(await Client.GetAsync($"/api/v1/diagnostics/orders/{order.Id}"));
        Assert.Equal("NotRequired", old.Items[0].SpecimenStatus);
        await ReadOrderAsync(await ResultAsync(old, new { resultText = "Kết quả phiếu cũ", rowVersion = old.Items[0].RowVersion }));
    }

    [Fact]
    public async Task PatientRedactionHidesBarcodeStaffAndRejectionNoteBeforeAndAfterPublication()
    {
        var order = await CollectAsync(await StartAsync(await CreateOrderAsync()));
        var rejected = await ReadOrderAsync(await StepAsync(order, "reject", new { reason = SpecimenRejectionReason.Other, note = "Ghi chú nội bộ", rowVersion = order.Items[0].RowVersion }));
        await AuthenticateAsync("pat1@test.com");
        var hidden = await ReadOrderAsync(await Client.GetAsync($"/api/v1/patients/me/diagnostic-orders/{order.Id}"));
        var item = hidden.Items[0];
        Assert.Equal("Rejected", item.SpecimenStatus); Assert.Null(item.SpecimenBarcode); Assert.Null(item.CollectedByUserName);
        Assert.Null(item.ReceivedByUserName); Assert.Null(item.RejectedByUserName); Assert.Null(item.RejectionNote);
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var entity = await db.DiagnosticOrders.FindAsync(order.Id); entity!.Status = DiagnosticOrderStatus.Completed;
            entity.ReviewedAtUtc = DateTime.UtcNow; entity.ReviewedByDoctorId = DoctorEntityId; await db.SaveChangesAsync();
        }
        var published = await ReadOrderAsync(await Client.GetAsync($"/api/v1/patients/me/diagnostic-orders/{order.Id}"));
        Assert.Null(published.Items[0].SpecimenBarcode); Assert.Null(published.Items[0].RejectedByUserName); Assert.Null(published.Items[0].RejectionNote);
        var envelope = JsonDocument.Parse(await (await Client.GetAsync("/api/v1/patients/me/diagnostic-orders?pageSize=100")).Content.ReadAsStringAsync());
        var batched = envelope.RootElement.GetProperty("data").GetProperty("items").EnumerateArray().Single(o => o.GetProperty("id").GetInt64() == order.Id);
        Assert.Equal(JsonSerializer.Serialize(published), JsonSerializer.Serialize(JsonSerializer.Deserialize<DiagnosticOrderDto>(batched.GetRawText(), new JsonSerializerOptions(JsonSerializerDefaults.Web))));
    }

    [Fact]
    public async Task SpecimenActionsRequireTechnicianRoleAndExactFacilityScope()
    {
        var order = await StartAsync(await CreateOrderAsync());
        await AuthenticateAsync("doc@test.com");
        Assert.Equal(HttpStatusCode.Forbidden, (await StepAsync(order, "collect", new { rowVersion = order.Items[0].RowVersion })).StatusCode);
        await AuthenticateAsync("tech@test.com");
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var facility = new Facility { Code = $"SP-{Guid.NewGuid():N}"[..20], Name = "Cơ sở không phân quyền", Address = "Test", City = "Test", Phone = "00000000" };
            db.Facilities.Add(facility); await db.SaveChangesAsync();
            var entity = await db.DiagnosticOrders.FindAsync(order.Id); entity!.FacilityId = facility.Id; await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.NotFound, (await StepAsync(order, "collect", new { rowVersion = order.Items[0].RowVersion })).StatusCode);
    }
}
