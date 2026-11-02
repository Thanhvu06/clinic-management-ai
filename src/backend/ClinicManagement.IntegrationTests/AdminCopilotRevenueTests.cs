using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using ClinicManagement.Application.AI.Tools;
using ClinicManagement.Application.Authentication.Interfaces;
using ClinicManagement.Application.Billing.DTOs;
using ClinicManagement.Application.Billing.Interfaces;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Pharmacy.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.AI.Tools;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace ClinicManagement.IntegrationTests;

[Collection(AiPhase12AcceptanceCollection.Name)]
public class AdminCopilotRevenueTests(CustomWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task Today_range_and_suggestion_match_billing_report_including_partial_payments()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var foreign = new Facility { Code = Code(), Name = "Cơ sở ngoài phân công", Address = "Test" };
        var department = new Department { Code = Code(), Name = "Khoa kiểm thử", Facility = foreign };
        var visit = new PatientVisit { VisitCode = Code(), PatientId = Patient1EntityId, Facility = foreign, Department = department, VisitDate = clock.VietnamToday, CreatedByUserId = ReceptionistId };
        var invoice = new Invoice { InvoiceCode = Code(), PatientId = Patient1EntityId, PatientVisit = visit, Status = InvoiceStatus.Unpaid, TotalAmount = 10000m, CreatedByUserId = ReceptionistId };
        var unpaidSource = new Invoice { InvoiceCode = Code(), PatientId = Patient1EntityId, PatientVisit = visit, Status = InvoiceStatus.Paid, TotalAmount = 50000m, PaidAtUtc = clock.UtcNow, CreatedByUserId = ReceptionistId };
        db.Invoices.AddRange(invoice, unpaidSource);
        await db.SaveChangesAsync();
        db.Payments.AddRange(
            Payment(invoice.Id, 1000m, PaymentStatus.Succeeded, clock.UtcNow),
            Payment(invoice.Id, 2000m, PaymentStatus.Succeeded, clock.UtcNow),
            Payment(invoice.Id, 9000m, PaymentStatus.Voided, clock.UtcNow));
        await db.SaveChangesAsync();
        var client = await CreateAuthenticatedClientAsync("admin@test.com");
        var today = clock.VietnamToday;
        var expected = await DataAsync(await client.GetAsync($"/api/v1/admin/billing/revenue?fromDate={today:yyyy-MM-dd}&toDate={today:yyyy-MM-dd}"));
        Assert.True(expected.GetProperty("totalRevenue").GetDecimal() >= 3000m);
        foreach (var request in new[]
        {
            new { message = "doanh thu hôm nay bao nhiêu", suggestionCode = (string?)null },
            new { message = $"doanh thu từ {today:dd/MM/yyyy} đến {today:dd/MM/yyyy}", suggestionCode = (string?)null },
            new { message = "doanh thu hôm nay", suggestionCode = (string?)"admin.revenue_today" }
        })
        {
            var response = await DataAsync(await client.PostAsJsonAsync("/api/v1/ai/copilot/chat", request));
            var report = Assert.Single(response.GetProperty("cards").EnumerateArray()).GetProperty("data");
            Assert.Equal(expected.GetProperty("totalRevenue").GetDecimal(), report.GetProperty("totalRevenue").GetDecimal());
            Assert.Equal(expected.GetProperty("totalSucceededTransactions").GetInt32(), report.GetProperty("totalSucceededTransactions").GetInt32());
            var message = response.GetProperty("message").GetString()!;
            Assert.Contains($"{report.GetProperty("totalRevenue").GetDecimal().ToString("N0", CultureInfo.GetCultureInfo("vi-VN"))} đồng", message);
            Assert.Contains($"{report.GetProperty("totalSucceededTransactions").GetInt32()} giao dịch thành công", message);
            Assert.DoesNotContain("từ", message);
            Assert.DoesNotContain("cơ sở được phân quyền", message);
            Assert.Contains(request.message.Contains("từ") ? $"ngày {today:dd/MM/yyyy}" : $"hôm nay ({today:dd/MM/yyyy})", message);
        }
    }

    [Theory]
    [InlineData("doanh thu hôm nay bao nhiêu", "today", "2026-11-02", "2026-11-02", "hôm nay (02/11/2026)")]
    [InlineData("doanh thu hôm qua bao nhiêu", "yesterday", "2026-11-01", "2026-11-01", "hôm qua (01/11/2026)")]
    [InlineData("doanh thu tuần này", "this_week", "2026-11-02", "2026-11-08", "từ 02/11/2026 đến 08/11/2026")]
    [InlineData("doanh thu tháng này", "this_month", "2026-11-01", "2026-11-30", "từ 01/11/2026 đến 30/11/2026")]
    [InlineData("doanh thu tháng trước", "last_month", "2026-10-01", "2026-10-31", "từ 01/10/2026 đến 31/10/2026")]
    [InlineData("doanh thu từ 02/11/2026 đến 02/11/2026", "range:2026-11-02:2026-11-02", "2026-11-02", "2026-11-02", "ngày 02/11/2026")]
    [InlineData("doanh thu từ 30/10/2026 đến 02/11/2026", "range:2026-10-30:2026-11-02", "2026-10-30", "2026-11-02", "từ 30/10/2026 đến 02/11/2026")]
    public async Task All_periods_use_report_service_and_main_message_contains_numbers(string message, string period, string start, string end, string label)
    {
        var decision = new Infrastructure.AI.Planning.AiDeterministicPlanner().Plan(new Application.AI.Planning.AiCopilotPlanningContext
        {
            Role = AiActorRole.Admin, NormalizedMessage = message,
            Analysis = new Application.AI.Conversation.AiConversationAnalysis { Intent = new() }
        });
        var call = Assert.Single(decision.ToolCalls);
        Assert.Equal(period, call.Arguments.GetProperty("period").GetString());
        using var scope = Factory.Services.CreateScope();
        var clock = new Mock<IDateTimeProvider>();
        clock.SetupGet(c => c.VietnamToday).Returns(new DateOnly(2026, 11, 2));
        var billing = new Mock<IBillingService>(MockBehavior.Strict);
        var from = DateOnly.Parse(start, CultureInfo.InvariantCulture);
        var to = DateOnly.Parse(end, CultureInfo.InvariantCulture);
        billing.Setup(b => b.GetRevenueReportAsync(from, to, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RevenueReportDto { FromDate = from, ToDate = to, TotalRevenue = 2390000m, TotalSucceededTransactions = 3 });
        var handler = new RoleCopilotToolHandler(scope.ServiceProvider.GetRequiredService<AppDbContext>(), Mock.Of<ICurrentUserService>(), clock.Object, Mock.Of<IPharmacyService>(), billing: billing.Object);
        var invocation = new AiToolInvocation { ToolName = call.Name, ArgumentsJson = call.Arguments.GetRawText() };
        var context = new AiToolExecutionContext { Roles = new HashSet<AiActorRole> { AiActorRole.Admin } };
        Assert.True(handler.ValidateArguments(invocation, context).IsValid);
        var result = await handler.ExecuteAsync(invocation, context, CancellationToken.None);
        var composed = new Infrastructure.AI.Planning.AiGroundedResponseComposer().Compose(decision, [result]);
        Assert.Equal($"Doanh thu {label}: 2.390.000 đồng, 3 giao dịch thành công.", composed.Message);
        billing.VerifyAll();
    }

    private static Payment Payment(long invoice, decimal amount, PaymentStatus status, DateTime at) => new()
    {
        PaymentCode = Code(), InvoiceId = invoice, Amount = amount, Status = status, ReceivedByUserId = ReceptionistId, ReceivedAtUtc = at
    };

    private static string Code() => Guid.NewGuid().ToString("N");

    private static async Task<JsonElement> DataAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);
        return JsonDocument.Parse(body).RootElement.GetProperty("data").Clone();
    }
}
