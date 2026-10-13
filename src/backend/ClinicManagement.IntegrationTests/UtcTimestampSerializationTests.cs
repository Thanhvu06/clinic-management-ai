using System.Net;
using System.Text.Json;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace ClinicManagement.IntegrationTests;

public class UtcTimestampSerializationTests(CustomWebApplicationFactory factory, ITestOutputHelper output) : IntegrationTestBase(factory)
{
    private static readonly DateTime Timestamp = new(2026, 10, 13, 3, 15, 20, DateTimeKind.Unspecified);
    private static int _sequence = 1600;

    [Fact]
    public void ConventionCoversEveryUtcTimestamp_AndPreservesWriteTicksAndLocalTypes()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var properties = db.Model.GetEntityTypes().SelectMany(entity => entity.GetProperties()).ToList();
        var timestamps = properties.Where(property => property.Name.EndsWith("Utc", StringComparison.Ordinal) &&
            (property.ClrType == typeof(DateTime) || property.ClrType == typeof(DateTime?))).ToList();
        Assert.NotEmpty(timestamps);
        foreach (var property in timestamps)
        {
            var converter = property.GetValueConverter(); Assert.NotNull(converter);
            var written = Assert.IsType<DateTime>(converter.ConvertToProvider(Timestamp));
            Assert.Equal(Timestamp.Ticks, written.Ticks); Assert.Equal(DateTimeKind.Unspecified, written.Kind);
            var read = Assert.IsType<DateTime>(converter.ConvertFromProvider(Timestamp));
            Assert.Equal(Timestamp.Ticks, read.Ticks); Assert.Equal(DateTimeKind.Utc, read.Kind);
        }
        Assert.All(properties.Where(property => property.ClrType == typeof(DateOnly) || property.ClrType == typeof(TimeOnly) ||
            ((property.ClrType == typeof(DateTime) || property.ClrType == typeof(DateTime?)) && !property.Name.EndsWith("Utc", StringComparison.Ordinal))),
            property => Assert.Null(property.GetValueConverter()));
        output.WriteLine($"UTC convention: {timestamps.Count} properties ({timestamps.Count(property => property.ClrType == typeof(DateTime))} DateTime, {timestamps.Count(property => property.ClrType == typeof(DateTime?))} nullable DateTime)");
    }

    [Fact]
    public async Task NotificationListSerializesReloadedCreatedAtUtcAndReadAtUtcWithUtcSuffix()
    {
        long notificationId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var notification = new Notification { UserId = Patient1Id, Type = NotificationType.System, Title = "Kiểm thử UTC", Message = "Thời gian không đổi ticks", CreatedAtUtc = Timestamp, ReadAtUtc = Timestamp.AddMinutes(1), IsRead = true };
            db.Notifications.Add(notification); await db.SaveChangesAsync(); notificationId = notification.Id;
            db.ChangeTracker.Clear();
            var reloaded = await db.Notifications.SingleAsync(item => item.Id == notificationId);
            Assert.Equal(DateTimeKind.Utc, reloaded.CreatedAtUtc.Kind); Assert.Equal(Timestamp.Ticks, reloaded.CreatedAtUtc.Ticks);
            Assert.Equal(DateTimeKind.Utc, reloaded.ReadAtUtc!.Value.Kind);
        }
        await AuthenticateAsync("pat1@test.com");
        var response = await Client.GetAsync("/api/v1/notifications?pageSize=100"); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var item = json.RootElement.GetProperty("data").GetProperty("items").EnumerateArray().Single(item => item.GetProperty("id").GetInt64() == notificationId);
        AssertUtc(item.GetProperty("createdAtUtc"), Timestamp);
        AssertUtc(item.GetProperty("readAtUtc"), Timestamp.AddMinutes(1));
    }

    [Fact]
    public async Task DiagnosticDetailSerializesReloadedOrderedAndOptionalTimestampsWithUtcSuffix()
    {
        long orderId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var assignment = await db.StaffFacilityAssignments.Where(item => item.UserId == TechnicianId && item.IsActive).OrderBy(item => item.FacilityId).FirstAsync();
            var sequence = Interlocked.Increment(ref _sequence);
            var visit = new PatientVisit { VisitCode = $"UTC-{Guid.NewGuid():N}"[..22], PatientId = Patient1EntityId, FacilityId = assignment.FacilityId,
                DepartmentId = assignment.DepartmentId!.Value, AssignedDoctorId = DoctorEntityId, VisitDate = GetFutureWorkingDate(sequence), QueueNumber = sequence,
                CreatedByUserId = ReceptionistId, Status = VisitStatus.InDiagnostics };
            var order = new DiagnosticOrder { OrderCode = $"DX-UTC-{Guid.NewGuid():N}", PatientVisit = visit, PatientId = Patient1EntityId, OrderingDoctorId = DoctorEntityId,
                FacilityId = assignment.FacilityId, ClinicalIndication = "Kiểm thử timestamp", Status = DiagnosticOrderStatus.InProgress, OrderedAtUtc = Timestamp,
                StartedAtUtc = Timestamp.AddMinutes(1), StartedByUserId = TechnicianId };
            db.DiagnosticOrders.Add(order); await db.SaveChangesAsync(); orderId = order.Id;
        }
        await AuthenticateAsync("tech@test.com");
        var response = await Client.GetAsync($"/api/v1/diagnostics/orders/{orderId}"); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); var data = json.RootElement.GetProperty("data");
        AssertUtc(data.GetProperty("orderedAtUtc"), Timestamp); AssertUtc(data.GetProperty("startedAtUtc"), Timestamp.AddMinutes(1));
        Assert.Equal(JsonValueKind.Null, data.GetProperty("completedAtUtc").ValueKind);
    }

    [Fact]
    public async Task NullableUtcTimestampsRemainNullAndLocalDatesRemainUnchanged()
    {
        using var scope = Factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var assignment = await db.StaffFacilityAssignments.Where(item => item.UserId == TechnicianId && item.IsActive).OrderBy(item => item.FacilityId).FirstAsync();
        var sequence = Interlocked.Increment(ref _sequence);
        var localDate = GetFutureWorkingDate(sequence);
        var visit = new PatientVisit { VisitCode = $"UTC-{Guid.NewGuid():N}"[..22], PatientId = Patient1EntityId, FacilityId = assignment.FacilityId,
            DepartmentId = assignment.DepartmentId!.Value, AssignedDoctorId = DoctorEntityId, VisitDate = localDate, QueueNumber = sequence, CreatedByUserId = ReceptionistId };
        db.PatientVisits.Add(visit);
        var notification = new Notification { UserId = Patient1Id, Type = NotificationType.System, Title = "UTC null", Message = "Null không đổi", CreatedAtUtc = Timestamp };
        db.Notifications.Add(notification); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        Assert.Null((await db.Notifications.SingleAsync(item => item.Id == notification.Id)).ReadAtUtc);
        Assert.Equal(localDate, (await db.PatientVisits.SingleAsync(item => item.Id == visit.Id)).VisitDate);
    }

    private static void AssertUtc(JsonElement element, DateTime expected)
    {
        var value = element.GetString(); Assert.NotNull(value);
        Assert.True(value.EndsWith('Z') || value.EndsWith("+00:00", StringComparison.Ordinal), value);
        Assert.Equal(expected.Ticks, DateTimeOffset.Parse(value).UtcDateTime.Ticks);
    }
}
