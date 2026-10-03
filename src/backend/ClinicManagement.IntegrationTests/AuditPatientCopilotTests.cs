using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClinicManagement.Application.AI.Interfaces;
using ClinicManagement.Application.AI.Tools;
using ClinicManagement.Application.Appointments.DTOs;
using ClinicManagement.Application.Appointments.DTOs.ChangeRequests;
using ClinicManagement.Application.Appointments.Interfaces;
using ClinicManagement.Application.Authentication.Interfaces;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Diagnostics.Interfaces;
using ClinicManagement.Application.Doctors.Interfaces;
using ClinicManagement.Application.Organization.Interfaces;
using ClinicManagement.Application.Specialties.Interfaces;
using ClinicManagement.Infrastructure.AI.Tools;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace ClinicManagement.IntegrationTests;

public class AuditPatientCopilotTests : IntegrationTestBase
{
    public AuditPatientCopilotTests(CustomWebApplicationFactory factory) : base(factory) { }

    private PatientCopilotToolHandler Handler(AppDbContext db, IAppointmentService appointments, IChangeRequestService changes, IAppointmentAvailabilityPolicy availability, IDateTimeProvider clock) => new(
        Mock.Of<ISpecialtyService>(), Mock.Of<IDoctorService>(), availability, appointments, changes,
        Mock.Of<IOrganizationService>(), Mock.Of<IDiagnosticWorkflowService>(), Mock.Of<ICurrentUserService>(), db,
        Mock.Of<IAiBookingConfirmationStore>(), clock);

    [Theory]
    [InlineData("cancel")] [InlineData("reschedule")]
    public async Task B4_Prepared_and_confirmed_change_preserves_trimmed_reason_and_hash(string operation)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var id = Random.Shared.NextInt64(1000000, 9000000);
        var appointments = new Mock<IAppointmentService>();
        appointments.Setup(x => x.GetPatientAppointmentByIdAsync(id)).ReturnsAsync(new AppointmentDto { Id = id, SpecialtyId = SpecialtyEntityId, AppointmentCode = "AUDIT" });
        string? actualReason = null;
        var changes = new Mock<IChangeRequestService>();
        changes.Setup(x => x.CreateCancellationRequestAsync(id, It.IsAny<CreateCancellationRequestDto>(), It.IsAny<Guid?>()))
            .Callback<long, CreateCancellationRequestDto, Guid?>((_, request, _) => actualReason = request.Reason).ReturnsAsync(new ChangeRequestDto { Id = 42 });
        changes.Setup(x => x.CreateRescheduleRequestAsync(id, It.IsAny<CreateRescheduleRequestDto>(), It.IsAny<Guid?>()))
            .Callback<long, CreateRescheduleRequestDto, Guid?>((_, request, _) => actualReason = request.Reason).ReturnsAsync(new ChangeRequestDto { Id = 42 });
        var availability = new Mock<IAppointmentAvailabilityPolicy>();
        availability.Setup(x => x.EvaluateSlotAvailabilityAsync(It.IsAny<SlotAvailabilityRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new SlotAvailabilityResult { IsAvailable = true });
        var handler = Handler(db, appointments.Object, changes.Object, availability.Object, scope.ServiceProvider.GetRequiredService<IDateTimeProvider>());
        var session = $"sess_audit-{Guid.NewGuid():N}";
        var reason = "Có lịch công tác đột xuất";
        var args = operation == "cancel" ? JsonSerializer.Serialize(new { appointmentId = id, reason = $"  {reason}  " }) : JsonSerializer.Serialize(new { appointmentId = id, requestedSlotId = 123, reason = $"  {reason}  " });
        var invocation = new AiToolInvocation { ToolName = $"patient.prepare_{operation}_appointment", ArgumentsJson = args, SessionId = session, IdempotencyKey = Guid.NewGuid().ToString() };
        var context = new AiToolExecutionContext { ActorId = Patient1Id, IsAuthenticated = true, SessionId = session, InvocationChannel = AiToolInvocationChannel.DirectHumanPreparation };
        Assert.True(handler.ValidateArguments(invocation, context).IsValid);
        var prepared = await handler.ExecuteAsync(invocation, context);
        Assert.Equal("pending_confirmation", prepared.Status);
        var action = await db.AiPendingToolActions.SingleAsync(a => a.ActionId == Guid.Parse(prepared.ActionId!));
        Assert.Equal(reason, JsonDocument.Parse(action.NormalizedArgumentsJson).RootElement.GetProperty("reason").GetString());
        string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
        var normalized = JsonSerializer.Serialize(new { appointmentId = id, requestedSlotId = operation == "reschedule" ? (long?)123 : null, reasonHash = Hash(reason) });
        Assert.Equal(Hash($"{operation}|{normalized}"), action.RequestHash);
        var differentArgs = operation == "cancel"
            ? JsonSerializer.Serialize(new { appointmentId = id, reason = "Có việc gia đình cần xử lý" })
            : JsonSerializer.Serialize(new { appointmentId = id, requestedSlotId = 123, reason = "Có việc gia đình cần xử lý" });
        var different = new AiToolInvocation { ToolName = invocation.ToolName, ArgumentsJson = differentArgs, SessionId = session, IdempotencyKey = invocation.IdempotencyKey };
        Assert.Equal("IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_PAYLOAD", (await handler.ExecuteAsync(different, context)).Error?.Code);
        var data = JsonSerializer.SerializeToElement(prepared.Data);
        var confirmed = await handler.ExecuteAsync(new AiToolInvocation { ToolName = "patient.execute_confirmed_action", ArgumentsJson = JsonSerializer.Serialize(new { actionId = action.ActionId, confirm = true, concurrencyToken = data.GetProperty("concurrencyToken").GetString() }) }, new AiToolExecutionContext { ActorId = Patient1Id, IsAuthenticated = true, SessionId = session, InvocationChannel = AiToolInvocationChannel.DirectHumanConfirmation });
        Assert.Equal("completed", confirmed.Status);
        Assert.Equal(reason, actualReason);
    }

    [Fact]
    public async Task B5_Default_slot_search_starts_on_vietnam_date_at_0010()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = new Mock<IDateTimeProvider>();
        clock.SetupGet(x => x.UtcNow).Returns(new DateTime(2030, 1, 1, 17, 10, 0, DateTimeKind.Utc));
        clock.SetupGet(x => x.VietnamToday).Returns(new DateOnly(2030, 1, 2));
        BatchSlotAvailabilityRequest? captured = null;
        var availability = new Mock<IAppointmentAvailabilityPolicy>();
        availability.Setup(x => x.GetAvailableSlotsAsync(It.IsAny<BatchSlotAvailabilityRequest>(), It.IsAny<CancellationToken>()))
            .Callback<BatchSlotAvailabilityRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(new List<ClinicManagement.Application.Doctors.DTOs.AvailableSlotDto>());
        var handler = Handler(db, Mock.Of<IAppointmentService>(), Mock.Of<IChangeRequestService>(), availability.Object, clock.Object);
        await handler.ExecuteAsync(new AiToolInvocation { ToolName = "clinic.get_available_slots" }, new AiToolExecutionContext());
        Assert.Equal(new DateOnly(2030, 1, 2), captured!.FromDate);
        Assert.Equal(new DateOnly(2030, 1, 16), captured.ToDate);
    }
}
