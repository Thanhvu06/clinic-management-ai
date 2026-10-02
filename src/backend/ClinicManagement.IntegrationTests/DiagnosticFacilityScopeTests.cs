using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Diagnostics.DTOs;
using ClinicManagement.Application.Diagnostics.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ClinicManagement.IntegrationTests;

/// <summary>
/// Public-service acceptance matrix for facility resolution and fail-closed technician scope.
/// The test uses the integration-test SQLite database only; it never calls a live provider.
/// </summary>
public class DiagnosticFacilityScopeTests : IntegrationTestBase
{
    private int _sequence;
    private long _diagnosticServiceId;
    private Guid _currentUserId;
    private string _currentRole = "Doctor";

    public DiagnosticFacilityScopeTests(CustomWebApplicationFactory factory) : base(factory) { }

    [Fact]
    public async Task Facility_resolution_matrix_covers_24_fail_closed_scenarios_and_technician_workflow()
    {
        var (facilityA, facilityB, departmentA, departmentB, serviceId) = await EnsureFacilityDataAsync();
        _diagnosticServiceId = serviceId;
        await SetDoctorAssignmentsAsync(facilityA);
        await SetTechnicianAssignmentsAsync(facilityA);

        var scenarioResults = new List<string>();

        // 1. Explicit appointment facility matches both persisted appointment and doctor assignment.
        var s1 = await CreateAppointmentAsync(facilityA);
        await SetDoctorAssignmentsAsync(facilityA);
        var o1 = await CreateForAppointmentAsync(s1.AppointmentId, facilityA);
        Assert.Equal(facilityA, await ReadOrderFacilityAsync(o1.Id));
        scenarioResults.Add("1 appointment explicit match: pass");

        // 2. No explicit facility uses the persisted appointment facility, never a fallback.
        var s2 = await CreateAppointmentAsync(facilityA);
        await SetDoctorAssignmentsAsync(facilityA);
        var o2 = await CreateForAppointmentAsync(s2.AppointmentId, null);
        Assert.Equal(facilityA, await ReadOrderFacilityAsync(o2.Id));
        scenarioResults.Add("2 appointment persisted facility: pass");

        // 3. Legacy appointment with exactly one distinct active doctor assignment resolves safely.
        var s3 = await CreateAppointmentAsync(null);
        await SetDoctorAssignmentsAsync(facilityA);
        var o3 = await CreateForAppointmentAsync(s3.AppointmentId, null);
        Assert.Equal(facilityA, await ReadOrderFacilityAsync(o3.Id));
        scenarioResults.Add("3 legacy appointment one assignment: pass");

        // 4. Legacy appointment with no active doctor assignment is denied before mutation.
        var s4 = await CreateAppointmentAsync(null);
        await SetDoctorAssignmentsAsync();
        await AssertFacilityDeniedWithoutEffectsAsync(() => CreateForAppointmentAsync(s4.AppointmentId, null));
        scenarioResults.Add("4 legacy appointment zero assignments: pass");

        // 5. Multiple distinct assignments are ambiguous even when one is primary.
        var s5 = await CreateAppointmentAsync(null);
        await SetDoctorAssignmentsAsync(facilityA, facilityB);
        await AssertFacilityDeniedWithoutEffectsAsync(() => CreateForAppointmentAsync(s5.AppointmentId, null));
        scenarioResults.Add("5 legacy appointment multiple assignments: pass");

        // 6. Explicit facility must match the appointment's persisted facility.
        var s6 = await CreateAppointmentAsync(facilityA);
        await SetDoctorAssignmentsAsync(facilityA, facilityB);
        await AssertFacilityDeniedWithoutEffectsAsync(() => CreateForAppointmentAsync(s6.AppointmentId, facilityB));
        scenarioResults.Add("6 appointment explicit conflict: pass");

        // 7. An explicit facility is denied when it is not assigned to the doctor.
        var s7 = await CreateAppointmentAsync(null);
        await SetDoctorAssignmentsAsync(facilityA);
        await AssertFacilityDeniedWithoutEffectsAsync(() => CreateForAppointmentAsync(s7.AppointmentId, facilityB));
        scenarioResults.Add("7 appointment explicit unassigned: pass");

        // 8. A persisted appointment facility must have an exact active doctor assignment.
        var s8 = await CreateAppointmentAsync(facilityA);
        await SetDoctorAssignmentsAsync(facilityB);
        await AssertFacilityDeniedWithoutEffectsAsync(() => CreateForAppointmentAsync(s8.AppointmentId, null));
        scenarioResults.Add("8 appointment persisted facility unassigned: pass");

        // 9. Appointment and linked visit agree; persisted sources remain consistent.
        var s9 = await CreateAppointmentAsync(facilityA, facilityA, departmentA);
        await SetDoctorAssignmentsAsync(facilityA);
        var o9 = await CreateForAppointmentAsync(s9.AppointmentId, null);
        Assert.Equal(facilityA, await ReadOrderFacilityAsync(o9.Id));
        scenarioResults.Add("9 appointment linked visit consistent: pass");

        // 10. Appointment and linked visit conflict, so no source can be selected.
        var s10 = await CreateAppointmentAsync(facilityA, facilityB, departmentB);
        await SetDoctorAssignmentsAsync(facilityA, facilityB);
        await AssertFacilityDeniedWithoutEffectsAsync(() => CreateForAppointmentAsync(s10.AppointmentId, null));
        scenarioResults.Add("10 appointment linked visit conflict: pass");

        // 11. Visit FacilityId is authoritative when no explicit facility is supplied.
        var s11 = await CreateAppointmentAsync(null, facilityA, departmentA);
        await SetDoctorAssignmentsAsync(facilityA);
        var o11 = await CreateForVisitAsync(s11.VisitId!.Value, null);
        Assert.Equal(facilityA, await ReadOrderFacilityAsync(o11.Id));
        scenarioResults.Add("11 visit persisted authority: pass");

        // 12. Explicit visit facility equal to persisted Visit.FacilityId is accepted.
        var s12 = await CreateAppointmentAsync(null, facilityA, departmentA);
        await SetDoctorAssignmentsAsync(facilityA);
        var o12 = await CreateForVisitAsync(s12.VisitId!.Value, facilityA);
        Assert.Equal(facilityA, await ReadOrderFacilityAsync(o12.Id));
        scenarioResults.Add("12 visit explicit match: pass");

        // 13. Explicit visit facility mismatch is denied before the visit is changed.
        var s13 = await CreateAppointmentAsync(null, facilityA, departmentA);
        await SetDoctorAssignmentsAsync(facilityA, facilityB);
        await AssertFacilityDeniedWithoutEffectsAsync(() => CreateForVisitAsync(s13.VisitId!.Value, facilityB));
        scenarioResults.Add("13 visit explicit conflict: pass");

        // 14. The visit facility is denied when the doctor is not assigned there.
        var s14 = await CreateAppointmentAsync(null, facilityA, departmentA);
        await SetDoctorAssignmentsAsync(facilityB);
        await AssertFacilityDeniedWithoutEffectsAsync(() => CreateForVisitAsync(s14.VisitId!.Value, null));
        scenarioResults.Add("14 visit doctor unassigned: pass");

        // 15. A linked appointment facility that conflicts with the Visit authority is denied.
        var s15 = await CreateAppointmentAsync(facilityB, facilityA, departmentA);
        await SetDoctorAssignmentsAsync(facilityA, facilityB);
        await AssertFacilityDeniedWithoutEffectsAsync(() => CreateForVisitAsync(s15.VisitId!.Value, null));
        scenarioResults.Add("15 visit linked appointment conflict: pass");

        // Prepare persisted orders for scenarios 16-24. Technician scope is facility A only.
        await SetTechnicianAssignmentsAsync(facilityA);
        var matchingAppointment = await CreateAppointmentAsync(facilityA);
        var conflictingAppointment = await CreateAppointmentAsync(facilityB);
        var matchingVisit = await CreateAppointmentAsync(null, facilityA, departmentA);
        var conflictingVisit = await CreateAppointmentAsync(null, facilityB, departmentB);
        var nullFacilityAppointment = await CreateAppointmentAsync(null);

        // 16. Explicit order facility + matching appointment is visible.
        var e16 = await CreatePersistedOrderAsync(facilityA, matchingAppointment.AppointmentId, null, DiagnosticOrderStatus.Ordered);
        scenarioResults.Add("16 order facility authority with appointment: pass");

        // 17. Explicit order facility + matching visit is visible.
        var e17 = await CreatePersistedOrderAsync(facilityA, null, matchingVisit.VisitId, DiagnosticOrderStatus.Ordered);
        scenarioResults.Add("17 order facility authority with visit: pass");

        // 18. Legacy order uses linked PatientVisit.FacilityId.
        var e18 = await CreatePersistedOrderAsync(null, null, matchingVisit.VisitId, DiagnosticOrderStatus.Ordered);
        scenarioResults.Add("18 legacy order visit facility: pass");

        // 19. Legacy order without visit uses linked Appointment.FacilityId.
        var e19 = await CreatePersistedOrderAsync(null, matchingAppointment.AppointmentId, null, DiagnosticOrderStatus.Ordered);
        scenarioResults.Add("19 legacy order appointment facility: pass");

        // 20. Legacy order without a resolvable source is hidden and its detail is denied.
        var e20 = await CreatePersistedOrderAsync(null, null, null, DiagnosticOrderStatus.Ordered);
        scenarioResults.Add("20 legacy order unknown facility: pass");

        // 21. Explicit order facility conflicting with appointment is hidden.
        var e21 = await CreatePersistedOrderAsync(facilityA, conflictingAppointment.AppointmentId, null, DiagnosticOrderStatus.Ordered);
        scenarioResults.Add("21 order/appointment conflict: pass");

        // 22. Explicit order facility conflicting with visit is hidden.
        var e22 = await CreatePersistedOrderAsync(facilityA, null, conflictingVisit.VisitId, DiagnosticOrderStatus.Ordered);
        scenarioResults.Add("22 order/visit conflict: pass");

        // 23. Legacy order with conflicting visit and appointment sources is hidden.
        var e23 = await CreatePersistedOrderAsync(null, conflictingAppointment.AppointmentId, matchingVisit.VisitId, DiagnosticOrderStatus.Ordered);
        scenarioResults.Add("23 legacy order conflicting sources: pass");

        // 24. Legacy order with an appointment whose facility is null is hidden.
        var e24 = await CreatePersistedOrderAsync(null, nullFacilityAppointment.AppointmentId, null, DiagnosticOrderStatus.Ordered);
        scenarioResults.Add("24 legacy order null appointment facility: pass");

        var technicianList = await AsUserAsync(TechnicianId, "DiagnosticTechnician", e => e.GetTechnicianOrdersAsync(null, null, null, 1, 100));
        var visibleIds = technicianList.Items.Select(x => x.Id).ToHashSet();
        Assert.Contains(e16.OrderId, visibleIds);
        Assert.Contains(e17.OrderId, visibleIds);
        Assert.Contains(e18.OrderId, visibleIds);
        Assert.Contains(e19.OrderId, visibleIds);
        Assert.DoesNotContain(e20.OrderId, visibleIds);
        Assert.DoesNotContain(e21.OrderId, visibleIds);
        Assert.DoesNotContain(e22.OrderId, visibleIds);
        Assert.DoesNotContain(e23.OrderId, visibleIds);
        Assert.DoesNotContain(e24.OrderId, visibleIds);

        var detail = await AsUserAsync(TechnicianId, "DiagnosticTechnician", e => e.GetTechnicianOrderByIdAsync(e18.OrderId));
        Assert.Equal(e18.OrderId, detail.Id);
        await Assert.ThrowsAsync<NotFoundException>(() => IgnoreAsync(AsUserAsync(TechnicianId, "DiagnosticTechnician", e => e.GetTechnicianOrderByIdAsync(e20.OrderId))));

        var statsBefore = await AsUserAsync(TechnicianId, "DiagnosticTechnician", e => e.GetTechnicianStatsAsync());
        Assert.True(statsBefore.OrderedCount >= 4);

        var started = await AsUserAsync(TechnicianId, "DiagnosticTechnician", e => e.StartOrderAsync(e16.OrderId, null));
        Assert.Equal(DiagnosticOrderStatus.InProgress.ToString(), started.Status);
        var itemId = await ReadFirstItemIdAsync(e16.OrderId);
        var recorded = await AsUserAsync(TechnicianId, "DiagnosticTechnician", e => e.RecordItemResultAsync(e16.OrderId, itemId, new RecordDiagnosticResultRequest { ResultText = "Normal" }));
        Assert.Equal(DiagnosticItemStatus.Completed.ToString(), recorded.Items.Single(i => i.Id == itemId).Status);
        var completed = await AsUserAsync(TechnicianId, "DiagnosticTechnician", e => e.CompleteOrderAsync(e16.OrderId, null));
        Assert.Equal(DiagnosticOrderStatus.Completed.ToString(), completed.Status);
        var statsAfter = await AsUserAsync(TechnicianId, "DiagnosticTechnician", e => e.GetTechnicianStatsAsync());
        Assert.True(statsAfter.CompletedTodayCount >= statsBefore.CompletedTodayCount + 1);

        var invalidMutation = await Assert.ThrowsAsync<NotFoundException>(() =>
            IgnoreAsync(AsUserAsync(TechnicianId, "DiagnosticTechnician", e => e.StartOrderAsync(e22.OrderId, null))));
        Assert.Contains("không tồn tại", invalidMutation.Message, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(24, scenarioResults.Count);
    }

    private async Task<T> AsUserAsync<T>(Guid userId, string role, Func<IDiagnosticWorkflowService, Task<T>> action)
    {
        _currentUserId = userId;
        _currentRole = role;
        using var scope = Factory.Services.CreateScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, _currentUserId.ToString()),
                new Claim(ClaimTypes.Role, _currentRole)
            }, "diagnostic-facility-test"))
        };

        try
        {
            return await action(scope.ServiceProvider.GetRequiredService<IDiagnosticWorkflowService>());
        }
        finally
        {
            accessor.HttpContext = null;
        }
    }

    private Task<DiagnosticOrderDto> CreateForAppointmentAsync(long appointmentId, long? facilityId)
    {
        return AsUserAsync(DoctorId, "Doctor", service => service.CreateOrderForDoctorAsync(
            appointmentId,
            new CreateDiagnosticOrderRequest
            {
                ClinicalIndication = "Facility resolution acceptance test",
                ServiceIds = new List<long> { _diagnosticServiceId }
            },
            facilityId: facilityId));
    }

    private Task<DiagnosticOrderDto> CreateForVisitAsync(long visitId, long? facilityId)
    {
        return AsUserAsync(DoctorId, "Doctor", service => service.CreateOrderForVisitDoctorAsync(
            visitId,
            new CreateDiagnosticOrderRequest
            {
                ClinicalIndication = "Facility resolution visit acceptance test",
                ServiceIds = new List<long> { _diagnosticServiceId }
            },
            facilityId: facilityId));
    }

    private async Task<(long FacilityA, long FacilityB, long DepartmentA, long DepartmentB, long ServiceId)> EnsureFacilityDataAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var facilityA = await db.Facilities.Where(f => f.IsActive).OrderBy(f => f.Id).Select(f => f.Id).FirstAsync();
        var departmentA = await db.Departments.Where(d => d.FacilityId == facilityA && d.IsActive).Select(d => d.Id).FirstAsync();

        var facilityBEntity = new Facility
        {
            Code = $"FAC-SCOPE-{Guid.NewGuid():N}"[..18],
            Name = "Facility Scope Test B",
            Address = "Test address B",
            City = "Ho Chi Minh",
            Phone = "02800000002",
            IsActive = true
        };
        db.Facilities.Add(facilityBEntity);
        await db.SaveChangesAsync();

        var departmentBEntity = new Department
        {
            FacilityId = facilityBEntity.Id,
            SpecialtyId = SpecialtyEntityId,
            Code = $"DEP-SCOPE-{Guid.NewGuid():N}"[..18],
            Name = "Facility Scope Test Department B",
            DepartmentType = DepartmentType.Clinical,
            IsActive = true
        };
        db.Departments.Add(departmentBEntity);
        await db.SaveChangesAsync();

        var serviceId = await db.DiagnosticServices.Where(s => s.IsActive).Select(s => s.Id).FirstAsync();
        return (facilityA, facilityBEntity.Id, departmentA, departmentBEntity.Id, serviceId);
    }

    private async Task SetDoctorAssignmentsAsync(params long[] facilityIds)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var old = await db.StaffFacilityAssignments.Where(a => a.UserId == DoctorId && a.Role == "Doctor").ToListAsync();
        db.StaffFacilityAssignments.RemoveRange(old);
        foreach (var (facilityId, index) in facilityIds.Select((id, index) => (id, index)))
        {
            db.StaffFacilityAssignments.Add(new StaffFacilityAssignment
            {
                UserId = DoctorId,
                FacilityId = facilityId,
                Role = "Doctor",
                IsPrimary = index == 0,
                IsActive = true,
                AssignedAtUtc = DateTime.UtcNow
            });
        }
        await db.SaveChangesAsync();
    }

    private async Task SetTechnicianAssignmentsAsync(long facilityId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var old = await db.StaffFacilityAssignments.Where(a => a.UserId == TechnicianId && a.Role == "DiagnosticTechnician").ToListAsync();
        db.StaffFacilityAssignments.RemoveRange(old);
        db.StaffFacilityAssignments.Add(new StaffFacilityAssignment
        {
            UserId = TechnicianId,
            FacilityId = facilityId,
            Role = "DiagnosticTechnician",
            IsPrimary = true,
            IsActive = true,
            AssignedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private async Task<(long AppointmentId, long? VisitId)> CreateAppointmentAsync(long? appointmentFacilityId, long? visitFacilityId = null, long? departmentId = null)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sequence = ++_sequence;
        var date = GetFutureWorkingDate(120 + sequence);
        var slot = new AppointmentSlot
        {
            DoctorId = DoctorEntityId,
            SlotDate = date,
            StartTime = new TimeOnly(7, sequence),
            EndTime = new TimeOnly(7, sequence + 30),
            IsBooked = true
        };
        db.AppointmentSlots.Add(slot);
        await db.SaveChangesAsync();

        var appointment = new Appointment
        {
            AppointmentCode = $"APT-SCOPE-{Guid.NewGuid():N}"[..24],
            PatientId = Patient1EntityId,
            DoctorId = DoctorEntityId,
            SpecialtyId = SpecialtyEntityId,
            FacilityId = appointmentFacilityId,
            AppointmentSlotId = slot.Id,
            AppointmentDate = date,
            StartTime = slot.StartTime,
            EndTime = slot.EndTime,
            Reason = "Facility scope matrix",
            Status = AppointmentStatus.InConsultation
        };
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync();

        long? visitId = null;
        if (visitFacilityId.HasValue)
        {
            var visit = new PatientVisit
            {
                VisitCode = $"VIS-SCOPE-{Guid.NewGuid():N}"[..24],
                PatientId = Patient1EntityId,
                AppointmentId = appointment.Id,
                FacilityId = visitFacilityId.Value,
                DepartmentId = departmentId ?? throw new InvalidOperationException("Department is required for a visit."),
                AssignedDoctorId = DoctorEntityId,
                VisitDate = date,
                QueueNumber = sequence,
                Status = VisitStatus.InConsultation,
                CreatedByUserId = DoctorId,
                CreatedAtUtc = DateTime.UtcNow,
                CheckedInAtUtc = DateTime.UtcNow
            };
            db.PatientVisits.Add(visit);
            await db.SaveChangesAsync();
            visitId = visit.Id;
        }

        return (appointment.Id, visitId);
    }

    private async Task<(long OrderId, long ItemId)> CreatePersistedOrderAsync(long? facilityId, long? appointmentId, long? visitId, DiagnosticOrderStatus status)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var order = new DiagnosticOrder
        {
            OrderCode = $"DX-SCOPE-{Guid.NewGuid():N}"[..24],
            FacilityId = facilityId,
            AppointmentId = appointmentId,
            PatientVisitId = visitId,
            PatientId = Patient1EntityId,
            OrderingDoctorId = DoctorEntityId,
            ClinicalIndication = "Persisted facility matrix",
            Status = status,
            OrderedAtUtc = DateTime.UtcNow,
            RowVersion = Guid.NewGuid().ToByteArray()
        };
        var item = new DiagnosticOrderItem
        {
            DiagnosticServiceId = _diagnosticServiceId,
            Status = status == DiagnosticOrderStatus.InProgress ? DiagnosticItemStatus.InProgress : DiagnosticItemStatus.Ordered,
            RowVersion = Guid.NewGuid().ToByteArray()
        };
        order.Items.Add(item);
        db.DiagnosticOrders.Add(order);
        await db.SaveChangesAsync();
        return (order.Id, item.Id);
    }

    private async Task<long> ReadOrderFacilityAsync(long orderId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (await db.DiagnosticOrders.AsNoTracking().Where(o => o.Id == orderId).Select(o => o.FacilityId).SingleAsync())!.Value;
    }

    private async Task<long> ReadFirstItemIdAsync(long orderId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.DiagnosticOrderItems.Where(i => i.DiagnosticOrderId == orderId).Select(i => i.Id).SingleAsync();
    }

    private async Task AssertFacilityDeniedWithoutEffectsAsync(Func<Task> operation)
    {
        var before = await ReadEffectCountsAsync();
        var exception = await Assert.ThrowsAsync<BusinessException>(operation);
        Assert.Equal("FACILITY_SCOPE_DENIED", exception.ErrorCode);
        var after = await ReadEffectCountsAsync();
        Assert.Equal(before, after);
    }

    private async Task<(int Orders, int Audits, int Notifications)> ReadEffectCountsAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (await db.DiagnosticOrders.CountAsync(), await db.SystemAuditLogs.CountAsync(), await db.Notifications.CountAsync());
    }

    private static async Task IgnoreAsync<T>(Task<T> task) => await task;
}
