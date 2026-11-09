using System.Net;
using System.Net.Http.Json;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Diagnostics.DTOs;
using ClinicManagement.Application.Doctors.DTOs;
using ClinicManagement.Application.Specialties.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicManagement.IntegrationTests;

[Collection(AiPhase12AcceptanceCollection.Name)]
public sealed class PublicDoctorSpecialtiesAndAdminTatTests(CustomWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task Public_list_profile_and_cardiology_filter_include_active_secondary_specialties()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var inactive = new Specialty { SpecialtyCode = $"OFF-{Guid.NewGuid():N}"[..20], Name = "Chuyên khoa đã ngừng", IsActive = false };
        db.Specialties.Add(inactive);
        await db.SaveChangesAsync();
        db.DoctorSpecialties.Add(new DoctorSpecialty { DoctorId = DoctorEntityId, SpecialtyId = inactive.Id });
        await db.SaveChangesAsync();
        using var guest = Factory.CreateClient();

        var all = (await guest.GetFromJsonAsync<ApiResponse<List<DoctorBasicDto>>>("/api/v1/doctors"))!.Data!;
        var doctor = Assert.Single(all, row => row.Id == DoctorEntityId);
        Assert.Equal(SpecialtyEntityId, doctor.Specialties[0].Id);
        Assert.Contains(doctor.Specialties, row => row.Id == CardiologySpecialtyId);
        Assert.DoesNotContain(doctor.Specialties, row => row.Id == inactive.Id);

        var profile = (await guest.GetFromJsonAsync<ApiResponse<DoctorDetailDto>>($"/api/v1/doctors/{DoctorEntityId}"))!.Data!;
        Assert.Equal(doctor.Specialties.Select(row => row.Id), profile.Specialties.Select(row => row.Id));
        var filtered = (await guest.GetFromJsonAsync<ApiResponse<PagedResult<DoctorBasicDto>>>(
            $"/api/v1/specialties/{CardiologySpecialtyId}/doctors"))!.Data!;
        Assert.Contains(filtered.Items, row => row.Id == DoctorEntityId && row.Specialties.Any(spec => spec.Id == CardiologySpecialtyId));
    }

    [Fact]
    public async Task Admin_reads_TAT_across_facilities_without_gaining_verification_access_or_mutating_orders()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var day = new DateOnly(2040, 4, 10);
        var facilities = new[]
        {
            new Facility { Code = $"TATA-{Guid.NewGuid():N}"[..25], Name = "TAT A", IsActive = true },
            new Facility { Code = $"TATB-{Guid.NewGuid():N}"[..25], Name = "TAT B", IsActive = true }
        };
        db.Facilities.AddRange(facilities);
        await db.SaveChangesAsync();
        var orders = facilities.Select(facility => new DiagnosticOrder
        {
            FacilityId = facility.Id, PatientId = Patient1EntityId, OrderingDoctorId = DoctorEntityId,
            OrderCode = $"TAT-{Guid.NewGuid():N}"[..28], Status = DiagnosticOrderStatus.Completed,
            OrderedAtUtc = new DateTime(2040, 4, 10, 1, 0, 0, DateTimeKind.Utc),
            CompletedAtUtc = new DateTime(2040, 4, 10, 2, 0, 0, DateTimeKind.Utc)
        }).ToArray();
        db.DiagnosticOrders.AddRange(orders);
        await db.SaveChangesAsync();
        var beforeCount = await db.DiagnosticOrders.CountAsync();
        var beforeAudit = await db.SystemAuditLogs.CountAsync();
        using var admin = await CreateAuthenticatedClientAsync("admin@test.com");

        using var response = await admin.GetAsync($"/api/v1/lab-reports/turnaround?from={day:yyyy-MM-dd}&to={day:yyyy-MM-dd}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var report = (await response.Content.ReadFromJsonAsync<ApiResponse<DiagnosticTurnaroundReportDto>>())!.Data!;
        Assert.True(report.OrderCount >= 2);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/v1/diagnostic-verification/queue")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync($"/api/v1/diagnostic-verification/orders/{orders[0].Id}")).StatusCode);
        Assert.Equal(beforeCount, await db.DiagnosticOrders.CountAsync());
        Assert.Equal(beforeAudit, await db.SystemAuditLogs.CountAsync());
        Assert.All(await db.DiagnosticOrders.AsNoTracking().Where(row => row.Id == orders[0].Id || row.Id == orders[1].Id).ToListAsync(),
            row => Assert.Equal(DiagnosticOrderStatus.Completed, row.Status));
    }
}
