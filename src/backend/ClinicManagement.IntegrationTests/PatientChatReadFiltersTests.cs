using System.Net.Http.Json;
using System.Text.Json;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.AI.Interfaces;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicManagement.IntegrationTests;

public sealed class PatientChatReadFiltersTests(CustomWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    private static int _slotSequence;

    [Fact]
    public async Task Bills_combine_vietnam_date_and_payment_status_before_pagination()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var date = new DateOnly(2026, 10, 17);
        var atMidnight = clock.ConvertVietnamToUtc(date.ToDateTime(TimeOnly.MinValue));
        var own = InvoiceFor(Patient1EntityId, date, atMidnight, InvoiceStatus.Unpaid);
        var paid = InvoiceFor(Patient1EntityId, date, atMidnight.AddHours(2), InvoiceStatus.Paid);
        var earlier = InvoiceFor(Patient1EntityId, date.AddDays(-1), atMidnight.AddMinutes(-1), InvoiceStatus.Unpaid);
        var foreign = InvoiceFor(Patient2EntityId, date, atMidnight.AddMinutes(1), InvoiceStatus.Unpaid);
        db.Invoices.AddRange(own, paid, earlier, foreign);
        await db.SaveChangesAsync();

        using var patient = await CreateAuthenticatedClientAsync("pat1@test.com");
        var filtered = await ChatAsync(patient, "xem hóa đơn chưa thanh toán ngày 17/10/2026");
        Assert.Equal("Có 1 hóa đơn chưa thanh toán ngày 17/10/2026.", filtered.Message);
        Assert.Equal(own.InvoiceCode, Assert.Single(Rows(filtered)).GetProperty("invoiceCode").GetString());

        var range = await ChatAsync(patient, "hóa đơn đã thanh toán từ ngày 16/10/2026 đến ngày 17/10/2026");
        Assert.Equal(paid.InvoiceCode, Assert.Single(Rows(range)).GetProperty("invoiceCode").GetString());
        Assert.Contains("từ 16/10/2026 đến 17/10/2026", range.Message);

        var empty = await ChatAsync(patient, "xem hóa đơn ngày 1/1/2020");
        Assert.Equal("Không có hóa đơn ngày 01/01/2020.", empty.Message);
        Assert.Empty(Rows(empty));
    }

    [Fact]
    public async Task Prescriptions_filter_by_relative_vietnam_day_and_return_only_the_latest()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var today = clock.VietnamToday;
        var start = clock.ConvertVietnamToUtc(today.ToDateTime(TimeOnly.MinValue));
        var latest = PrescriptionFor(Patient1EntityId, start.AddHours(2));
        var earlier = PrescriptionFor(Patient1EntityId, start.AddMinutes(1));
        var yesterday = PrescriptionFor(Patient1EntityId, start.AddMinutes(-1));
        db.Prescriptions.AddRange(latest, earlier, yesterday, PrescriptionFor(Patient2EntityId, start.AddHours(3)));
        await db.SaveChangesAsync();

        using var patient = await CreateAuthenticatedClientAsync("pat1@test.com");
        var response = await ChatAsync(patient, "đơn thuốc mới nhất hôm nay của tôi");
        var row = Assert.Single(Rows(response));
        Assert.Equal(latest.Id, row.GetProperty("id").GetInt64());
        Assert.StartsWith("RX-", row.GetProperty("prescriptionCode").GetString());
        Assert.Equal(today.ToString("yyyy-MM-dd"), row.GetProperty("prescribedDate").GetString());
        Assert.Equal(yesterday.Id, Assert.Single(Rows(await ChatAsync(patient, "đơn thuốc hôm qua"))).GetProperty("id").GetInt64());
    }

    [Fact]
    public async Task Visits_use_visit_date_for_current_previous_and_named_month()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var today = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>().VietnamToday;
        var currentMonth = new DateOnly(today.Year, today.Month, 1);
        var previousMonth = currentMonth.AddMonths(-1);
        var department = await db.Departments.FirstAsync();
        PatientVisit Visit(long patient, DateOnly day, int queue) => new()
        {
            PatientId = patient, FacilityId = department.FacilityId, DepartmentId = department.Id, VisitDate = day,
            QueueNumber = queue, VisitCode = "FILTER-" + Guid.NewGuid().ToString("N"), CreatedByUserId = ReceptionistId
        };
        var current = Visit(Patient1EntityId, currentMonth, 101);
        var previous = Visit(Patient1EntityId, previousMonth, 102);
        db.PatientVisits.AddRange(current, previous, Visit(Patient2EntityId, currentMonth, 103));
        await db.SaveChangesAsync();

        using var patient = await CreateAuthenticatedClientAsync("pat1@test.com");
        Assert.Equal(current.Id, Assert.Single(Rows(await ChatAsync(patient, "lượt khám tháng này"))).GetProperty("id").GetInt64());
        Assert.Equal(previous.Id, Assert.Single(Rows(await ChatAsync(patient, "lượt khám tháng trước"))).GetProperty("id").GetInt64());
        Assert.Equal(current.Id, Assert.Single(Rows(await ChatAsync(patient, $"lượt khám tháng {today.Month}"))).GetProperty("id").GetInt64());
    }

    [Fact]
    public async Task Diagnostic_alias_and_order_date_select_own_orders_and_preserve_publication_policy()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var ordered = clock.ConvertVietnamToUtc(new DateTime(2026, 10, 21, 1, 0, 0));
        async Task<DiagnosticService> Service(string code, string name) =>
            await db.DiagnosticServices.FirstOrDefaultAsync(service => service.Code == code) ?? new() { Code = code, Name = name };
        var cbc = await Service("LAB-CBC", "Công thức máu");
        var glucose = await Service("LAB-GLU", "Đường huyết");
        DiagnosticOrder Order(long patient, DiagnosticService service, DateTime date) => new()
        {
            PatientId = patient, OrderingDoctorId = DoctorEntityId, OrderedAtUtc = date,
            OrderCode = "FILTER-" + Guid.NewGuid().ToString("N"), ClinicalIndication = "Kiểm tra",
            Items = [new DiagnosticOrderItem { DiagnosticService = service }]
        };
        var own = Order(Patient1EntityId, cbc, ordered);
        db.DiagnosticOrders.AddRange(own, Order(Patient1EntityId, glucose, ordered),
            Order(Patient1EntityId, cbc, ordered.AddDays(-1)), Order(Patient2EntityId, cbc, ordered));
        await db.SaveChangesAsync();

        using var patient = await CreateAuthenticatedClientAsync("pat1@test.com");
        var response = await ChatAsync(patient, "kết quả xét nghiệm công thức máu ngày 21/10/2026");
        Assert.StartsWith("Có 1 chỉ định cận lâm sàng cho dịch vụ công thức máu ngày 21/10/2026.", response.Message);
        var row = Assert.Single(Rows(response));
        Assert.Equal(own.OrderCode, row.GetProperty("orderCode").GetString());
        Assert.False(row.GetProperty("publishedToPatient").GetBoolean());
        Assert.Equal(JsonValueKind.Null, Assert.Single(row.GetProperty("items").EnumerateArray()).GetProperty("result").ValueKind);
        var range = await ChatAsync(patient, "kết quả xét nghiệm từ ngày 20/10/2026 đến ngày 21/10/2026");
        Assert.Equal(3, Rows(range).Count());
    }

    [Theory]
    [InlineData(1, "hủy lịch hẹn", "đổi lịch", false)]
    [InlineData(2, "tôi muốn hủy lịch hẹn", "đổi lịch hẹn", false)]
    [InlineData(3, "tôi muốn hủy lịch", "tôi muốn đổi lịch hẹn sang ngày mai", true)]
    public async Task Upcoming_and_change_selection_use_clock_status_and_owner_without_writing(
        int caseNumber, string cancelMessage, string rescheduleMessage, bool tomorrow)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var email = $"upcoming-{Guid.NewGuid():N}@test.com";
        using var registration = await Client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            fullName = "Bệnh nhân kiểm thử lịch sắp tới",
            email,
            phoneNumber = $"087641230{caseNumber}",
            password = "Pass@123"
        });
        registration.EnsureSuccessStatusCode();
        var patientId = await db.Patients.Where(patient => patient.Email == email).Select(patient => patient.Id).SingleAsync();

        var now = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>().VietnamNow;
        var date = DateOnly.FromDateTime(now);
        var future = date.AddDays(30);
        var own = AppointmentFor(patientId, future, AppointmentStatus.Confirmed);
        var pending = AppointmentFor(patientId, future, AppointmentStatus.Pending);
        var pendingCancel = AppointmentFor(patientId, future, AppointmentStatus.PendingCancellation);
        var pendingReschedule = AppointmentFor(patientId, future, AppointmentStatus.PendingReschedule);
        db.Appointments.AddRange(own, pending, pendingCancel, pendingReschedule,
            AppointmentFor(patientId, date.AddDays(-1), AppointmentStatus.Confirmed),
            AppointmentFor(patientId, future, AppointmentStatus.NoShow),
            AppointmentFor(patientId, future, AppointmentStatus.Cancelled),
            AppointmentFor(patientId, future, AppointmentStatus.Completed),
            AppointmentFor(Patient2EntityId, future, AppointmentStatus.Confirmed));
        if (now.TimeOfDay > TimeSpan.Zero)
        {
            var pastToday = AppointmentFor(patientId, date, AppointmentStatus.PendingCancellation);
            pastToday.StartTime = TimeOnly.MinValue;
            db.Appointments.Add(pastToday);
        }
        await db.SaveChangesAsync();

        using var patient = await CreateAuthenticatedClientAsync(email);
        var upcoming = await ChatAsync(patient, "lịch hẹn sắp tới của tôi");
        var results = upcoming.ToolResults.Single().Data;
        var actual = JsonSerializer.SerializeToElement(results, JsonSerializerOptions.Web).GetProperty("items")
            .EnumerateArray()
            .Select(row => (Id: row.GetProperty("id").GetInt64(), Code: row.GetProperty("appointmentCode").GetString()!,
                Status: row.GetProperty("status").GetString()!))
            .OrderBy(row => row.Id).ToArray();
        var expected = new[] { own, pending, pendingCancel, pendingReschedule }
            .Select(appointment => (Id: appointment.Id, Code: appointment.AppointmentCode, Status: appointment.Status.ToString()))
            .OrderBy(row => row.Id).ToArray();
        Assert.Equal(expected, actual);
        var cancellation = await ChatAsync(patient, cancelMessage);
        var reschedule = await ChatAsync(patient, rescheduleMessage);
        Assert.Equal(new[] { own.Id, pending.Id }.Order(), cancellation.Actions
            .Where(action => action.Type == "RequestCancellation").Select(action => action.Payload.AppointmentId!.Value).Order());
        Assert.Equal(new[] { own.Id, pending.Id }.Order(), reschedule.Actions
            .Where(action => action.Type == "RequestReschedule").Select(action => action.Payload.AppointmentId!.Value).Order());
        Assert.All(reschedule.Actions.Where(action => action.Type == "RequestReschedule"),
            action => Assert.Equal(tomorrow ? date.AddDays(1).ToString("yyyy-MM-dd") : null, action.Payload.SlotDate));
        Assert.Empty(await db.AiPendingToolActions.ToListAsync());
        Assert.Empty(await db.AppointmentChangeRequests.ToListAsync());
    }

    [Theory]
    [InlineData("hủy lịch")]
    [InlineData("huỷ lịch hẹn")]
    [InlineData("hủy lịch hẹn")]
    [InlineData("tôi muốn hủy lịch")]
    [InlineData("tôi muốn hủy lịch hẹn")]
    [InlineData("đổi lịch")]
    [InlineData("đổi lịch hẹn")]
    [InlineData("tôi muốn đổi lịch hẹn sang ngày mai")]
    public async Task Active_draft_still_cancels_without_preparing_appointment_change(string message)
    {
        using var scope = Factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IAiSessionSnapshotStore>();
        var sessionId = "sess_" + Guid.NewGuid().ToString("N");
        var draftId = "draft_" + Guid.NewGuid().ToString("N");
        Assert.True((await store.TouchSessionAsync(sessionId, Patient1Id, draftId, 1, null)).IsAccepted);
        using var patient = await CreateAuthenticatedClientAsync("pat1@test.com");
        var response = await ChatAsync(patient, message, sessionId);
        Assert.Equal("DraftCancelled", response.DialogueOutcome);
        Assert.Empty(response.Actions);
    }

    [Fact]
    public async Task Schedule_question_starts_wizard_on_day_and_price_uses_vietnamese_money()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Users.SingleAsync(user => user.Id == DoctorId)).FullName = "Nguyễn Minh Khải";
        await db.SaveChangesAsync();
        using var patient = await CreateAuthenticatedClientAsync("pat1@test.com");
        using var request = await patient.PostAsJsonAsync("/api/v1/ai/booking-wizard",
            new { sessionId = "wiz_" + Guid.NewGuid().ToString("N"), step = "start", hint = "bác sĩ Khải khám ngày nào" });
        request.EnsureSuccessStatusCode();
        var wizard = (await request.Content.ReadFromJsonAsync<ApiResponse<AiBookingWizardResponseDto>>())!.Data!;
        Assert.Equal("day", wizard.Step);
        Assert.Equal("Nguyễn Minh Khải", wizard.Summary?.DoctorName);
        var price = await ChatAsync(patient, "khám nội tổng quát giá bao nhiêu");
        Assert.Contains("150.000 ₫", price.Message);
    }

    private static async Task<AiChatResponseDto> ChatAsync(HttpClient patient, string message, string? sessionId = null)
    {
        using var response = await patient.PostAsJsonAsync("/api/v1/ai/chat", new
        {
            message, sessionId = sessionId ?? "sess_" + Guid.NewGuid().ToString("N")
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ApiResponse<AiChatResponseDto>>())!.Data!;
    }

    private static IEnumerable<JsonElement> Rows(AiChatResponseDto response) =>
        JsonSerializer.SerializeToElement(Assert.Single(response.ToolResults).Data, JsonSerializerOptions.Web).EnumerateArray();

    private static Prescription PrescriptionFor(long patient, DateTime date) => new()
    {
        PatientId = patient, DoctorId = DoctorEntityId, Status = PrescriptionStatus.Issued, CreatedAt = date
    };

    private static Invoice InvoiceFor(long patient, DateOnly day, DateTime created, InvoiceStatus status) => new()
    {
        PatientId = patient, Appointment = AppointmentFor(patient, day, AppointmentStatus.Completed),
        InvoiceCode = "FILTER-" + Guid.NewGuid().ToString("N"), SourceType = InvoiceSourceType.Appointment,
        Status = status, CreatedAtUtc = created, CreatedByUserId = ReceptionistId, Subtotal = 100, TotalAmount = 100
    };

    private static Appointment AppointmentFor(long patient, DateOnly day, AppointmentStatus status) => new()
    {
        PatientId = patient, AppointmentCode = "FILTER-" + Guid.NewGuid().ToString("N"),
        DoctorId = DoctorEntityId, SpecialtyId = SpecialtyEntityId, AppointmentDate = day,
        AppointmentSlot = new AppointmentSlot
        {
            DoctorId = DoctorEntityId, SlotDate = day, StartTime = new TimeOnly(8, 0).AddMinutes(Interlocked.Increment(ref _slotSequence)),
            EndTime = new TimeOnly(18, 0)
        },
        StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(8, 30), Status = status
    };
}
