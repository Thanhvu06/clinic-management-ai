using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace ClinicManagement.IntegrationTests;

[Collection(AiPhase12AcceptanceCollection.Name)]
public sealed class AiPhase32ReadAccessTests : IntegrationTestBase
{
    public AiPhase32ReadAccessTests(CustomWebApplicationFactory factory) : base(factory) { }

    [Fact]
    public async Task Doctor_reads_walk_in_summary_orders_and_prescription_through_http_without_private_identifiers()
    {
        var fixture = await CreateClinicalFixtureAsync(Patient1EntityId, DoctorEntityId);
        var client = await CreateAuthenticatedClientAsync("doc@test.com");

        var summary = await ChatAsync(client, "Ca walk-in này có triệu chứng và sinh hiệu gì?", new { visitId = fixture.VisitId }, "doctor-walkin-summary");
        Assert.Equal("NotCalled", summary.GetProperty("providerStatus").GetString());
        var summaryCard = AssertCard(summary, "doctor_patient_summary");
        var summaryData = summaryCard.GetProperty("data");
        Assert.Equal("walk_in_visit", summaryData.GetProperty("caseType").GetString());
        Assert.Equal("Đau đầu tổng hợp cho test", summaryData.GetProperty("chiefComplaint").GetString());
        Assert.Equal(120, summaryData.GetProperty("vitals").GetProperty("bloodPressureSystolic").GetInt32());
        Assert.DoesNotContain("MedicalRecord", summary.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("BN-", summary.GetRawText(), StringComparison.OrdinalIgnoreCase);

        var orders = await ChatAsync(client, "Chỉ định nào đã có kết quả, chỉ định nào đang chờ?", new { visitId = fixture.VisitId }, "doctor-walkin-orders");
        var orderCard = AssertCard(orders, "doctor_diagnostic_orders");
        Assert.Contains("WBC 7.5", orderCard.GetRawText(), StringComparison.Ordinal);
        Assert.Contains("chưa có kết quả", orderCard.GetProperty("description").GetString(), StringComparison.OrdinalIgnoreCase);

        var prescription = await ChatAsync(client, "Đơn thuốc của ca này đang ở trạng thái nào?", new { visitId = fixture.VisitId }, "doctor-walkin-prescription");
        var prescriptionCard = AssertCard(prescription, "doctor_prescription_status");
        Assert.True(prescriptionCard.GetRawText().Contains("Issued", StringComparison.Ordinal), prescriptionCard.GetRawText());
        Assert.Contains("Paracetamol", prescriptionCard.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Patient_reads_only_own_results_and_admin_card_never_contains_clinical_rows()
    {
        var fixture = await CreateClinicalFixtureAsync(Patient1EntityId, DoctorEntityId);
        var patient1 = await CreateAuthenticatedClientAsync("pat1@test.com");
        var patient2 = await CreateAuthenticatedClientAsync("pat2@test.com");
        var admin = await CreateAuthenticatedClientAsync("admin@test.com");

        var own = await ChatAsync(patient1, "Xem kết quả xét nghiệm của tôi", null, "patient-own-results");
        AssertCard(own, "patient_diagnostic_results");
        Assert.Contains("WBC 7.5", own.GetRawText(), StringComparison.Ordinal);

        var other = await ChatAsync(patient2, "Xem kết quả xét nghiệm của tôi", null, "patient-other-results");
        var otherCard = AssertCard(other, "patient_diagnostic_results");
        Assert.DoesNotContain("WBC 7.5", other.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("Đau đầu tổng hợp cho test", other.GetRawText(), StringComparison.Ordinal);
        Assert.Contains("chưa có kết quả", otherCard.GetProperty("description").GetString(), StringComparison.OrdinalIgnoreCase);

        var adminResponse = await ChatAsync(admin, "Tổng hợp chỉ số vận hành hôm nay", null, "admin-metrics");
        Assert.DoesNotContain("WBC 7.5", adminResponse.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("Đau đầu tổng hợp cho test", adminResponse.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Context_resolver_rejects_cross_patient_composite_and_reassignment_at_request_time()
    {
        var first = await CreateClinicalFixtureAsync(Patient1EntityId, DoctorEntityId);
        var second = await CreateClinicalFixtureAsync(Patient2EntityId, DoctorEntityId);
        var client = await CreateAuthenticatedClientAsync("doc@test.com");

        var mixed = await ChatAsync(client, "Tóm tắt ca đang mở này", new { visitId = first.VisitId, diagnosticOrderId = second.OrderId }, "mixed-case");
        Assert.Equal("Clarifying", mixed.GetProperty("assistantStatus").GetString());
        Assert.Empty(mixed.GetProperty("cards").EnumerateArray());
        Assert.DoesNotContain("Đau đầu tổng hợp cho test", mixed.GetRawText(), StringComparison.Ordinal);

        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var visit = await db.PatientVisits.SingleAsync(x => x.Id == first.VisitId);
            visit.AssignedDoctorId = Doctor2EntityId;
            await db.SaveChangesAsync();
        }

        try
        {
            var reassigned = await ChatAsync(client, "Tóm tắt ca đang mở này", new { visitId = first.VisitId }, "reassigned-case");
            Assert.Equal("Clarifying", reassigned.GetProperty("assistantStatus").GetString());
            Assert.Empty(reassigned.GetProperty("cards").EnumerateArray());
        }
        finally
        {
            await using var restoreScope = Factory.Services.CreateAsyncScope();
            var db = restoreScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var visit = await db.PatientVisits.SingleAsync(x => x.Id == first.VisitId);
            visit.AssignedDoctorId = DoctorEntityId;
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Doctor_cannot_read_scheduled_appointment_from_unassigned_second_facility()
    {
        long appointmentId;
        await using (var setupScope = Factory.Services.CreateAsyncScope())
        {
            var db = setupScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var specialtyId = await db.Departments.Where(x => x.IsActive).Select(x => x.SpecialtyId).FirstAsync(x => x.HasValue) ?? SpecialtyEntityId;
            var facility = new Facility
            {
                Code = $"AI32-F2-{Guid.NewGuid():N}"[..16].ToUpperInvariant(),
                Name = "Cơ sở AI Phase 3.2 số hai",
                Address = "Địa chỉ tổng hợp kiểm thử",
                City = "Hồ Chí Minh",
                Phone = "02830003232",
                IsActive = true
            };
            db.Facilities.Add(facility);
            await db.SaveChangesAsync();

            var department = new Department
            {
                FacilityId = facility.Id,
                SpecialtyId = specialtyId,
                Code = $"AI32-D2-{Guid.NewGuid():N}"[..16].ToUpperInvariant(),
                Name = "Khoa khám cơ sở thứ hai",
                DepartmentType = DepartmentType.Clinical,
                IsActive = true
            };
            var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10));
            var slot = new AppointmentSlot
            {
                DoctorId = Doctor2EntityId,
                SlotDate = date,
                StartTime = new TimeOnly(9, 0),
                EndTime = new TimeOnly(9, 30),
                IsBooked = true
            };
            db.Departments.Add(department);
            db.AppointmentSlots.Add(slot);
            await db.SaveChangesAsync();

            db.StaffFacilityAssignments.Add(new StaffFacilityAssignment
            {
                UserId = Doctor2UserId,
                FacilityId = facility.Id,
                DepartmentId = department.Id,
                Role = "Doctor",
                IsPrimary = true,
                IsActive = true,
                AssignedAtUtc = DateTime.UtcNow
            });
            var appointment = new Appointment
            {
                AppointmentCode = $"AI32-ONLINE-{Guid.NewGuid():N}"[..20].ToUpperInvariant(),
                PatientId = Patient2EntityId,
                DoctorId = Doctor2EntityId,
                SpecialtyId = specialtyId,
                FacilityId = facility.Id,
                AppointmentSlotId = slot.Id,
                AppointmentDate = date,
                StartTime = slot.StartTime,
                EndTime = slot.EndTime,
                Reason = "Đặt lịch trực tuyến tổng hợp cho kiểm thử",
                Status = AppointmentStatus.Confirmed
            };
            db.Appointments.Add(appointment);
            await db.SaveChangesAsync();
            appointmentId = appointment.Id;
        }

        var doctor = await CreateAuthenticatedClientAsync("doc@test.com");
        var denied = await ChatAsync(doctor, "Tóm tắt ca đang mở này", new { appointmentId }, "unassigned-second-facility");
        Assert.Equal("Clarifying", denied.GetProperty("assistantStatus").GetString());
        Assert.Empty(denied.GetProperty("cards").EnumerateArray());
    }

    [Fact]
    public async Task Facility_revoke_and_technician_department_scope_are_checked_at_tool_execution()
    {
        var fixture = await CreateClinicalFixtureAsync(Patient1EntityId, DoctorEntityId);
        long facilityId;
        string outOfDepartmentOrderCode = string.Empty;
        StaffFacilityAssignment? removedDoctorAssignment;
        await using (var setupScope = Factory.Services.CreateAsyncScope())
        {
            var db = setupScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var department = await db.Departments.Where(x => x.IsActive).OrderBy(x => x.Id).FirstAsync();
            facilityId = department.FacilityId;
            removedDoctorAssignment = await db.StaffFacilityAssignments.SingleAsync(x => x.UserId == DoctorId && x.FacilityId == facilityId && x.Role == "Doctor" && x.IsActive);
            db.StaffFacilityAssignments.Remove(removedDoctorAssignment);
            var unassignedDepartment = new Department
            {
                FacilityId = facilityId, SpecialtyId = department.SpecialtyId, Code = $"AI32-TECH-{Guid.NewGuid():N}"[..16].ToUpperInvariant(),
                Name = "Khoa kỹ thuật ngoài phạm vi", DepartmentType = DepartmentType.Paraclinical, IsActive = true
            };
            db.Departments.Add(unassignedDepartment);
            await db.SaveChangesAsync();
            var order = new DiagnosticOrder
            {
                PatientVisitId = fixture.VisitId, PatientId = Patient1EntityId, OrderingDoctorId = DoctorEntityId,
                FacilityId = facilityId, PerformingDepartmentId = unassignedDepartment.Id,
                OrderCode = $"AI32-TECH-{Guid.NewGuid():N}"[..18].ToUpperInvariant(), ClinicalIndication = "Ngoài khoa được phân công", Status = DiagnosticOrderStatus.Ordered
            };
            db.DiagnosticOrders.Add(order);
            await db.SaveChangesAsync();
            outOfDepartmentOrderCode = order.OrderCode;
        }

        try
        {
            var doctor = await CreateAuthenticatedClientAsync("doc@test.com");
            var denied = await ChatAsync(doctor, "Tóm tắt ca đang mở này", new { visitId = fixture.VisitId }, "revoked-facility");
            Assert.Equal("Clarifying", denied.GetProperty("assistantStatus").GetString());
            Assert.Empty(denied.GetProperty("cards").EnumerateArray());

            var technician = await CreateAuthenticatedClientAsync("tech@test.com");
            var worklist = await ChatAsync(technician, "Xem danh sách chỉ định đang chờ xử lý", null, "wrong-department");
            Assert.DoesNotContain(outOfDepartmentOrderCode, worklist.GetRawText(), StringComparison.Ordinal);
        }
        finally
        {
            await using var restoreScope = Factory.Services.CreateAsyncScope();
            var db = restoreScope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (!await db.StaffFacilityAssignments.AnyAsync(x => x.UserId == DoctorId && x.FacilityId == facilityId && x.Role == "Doctor" && x.IsActive))
            {
                db.StaffFacilityAssignments.Add(new StaffFacilityAssignment
                {
                    UserId = removedDoctorAssignment!.UserId, FacilityId = removedDoctorAssignment.FacilityId,
                    DepartmentId = removedDoctorAssignment.DepartmentId, Role = removedDoctorAssignment.Role,
                    IsPrimary = removedDoctorAssignment.IsPrimary, IsActive = true
                });
                await db.SaveChangesAsync();
            }
        }
    }

    [Fact]
    public async Task Explicit_read_route_stays_local_when_provider_is_broken_and_provider_payload_is_identifier_redacted()
    {
        var fixture = await CreateClinicalFixtureAsync(Patient1EntityId, DoctorEntityId);
        Factory.MockAiProvider.Setup(x => x.ChatWithAiAsync(
                It.IsAny<string>(), It.IsAny<List<ClinicManagement.Application.AI.DTOs.ChatMessageDto>>(),
                It.IsAny<List<ClinicManagement.Application.AI.DTOs.WhitelistItemDto>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("provider intentionally unavailable"));
        var client = await CreateAuthenticatedClientAsync("doc@test.com");

        var response = await ChatAsync(client, "Xem hàng đợi bệnh nhân của tôi", null, "provider-not-needed");
        Assert.Equal("NotCalled", response.GetProperty("providerStatus").GetString());
        Assert.Contains(response.GetProperty("cards").EnumerateArray(), x => x.GetProperty("type").GetString() == "doctor_queue");

        var captured = string.Empty;
        Factory.MockAiProvider.Setup(x => x.ChatWithAiAsync(
                It.IsAny<string>(), It.IsAny<List<ClinicManagement.Application.AI.DTOs.ChatMessageDto>>(),
                It.IsAny<List<ClinicManagement.Application.AI.DTOs.WhitelistItemDto>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, List<ClinicManagement.Application.AI.DTOs.ChatMessageDto>, List<ClinicManagement.Application.AI.DTOs.WhitelistItemDto>, string, CancellationToken>((message, _, _, _, _) => captured = message)
            .ReturnsAsync(new ClinicManagement.Application.AI.DTOs.AiChatProviderResult
            {
                IsSuccess = true, Status = "Success", IsClear = false, PlannerSchemaVersion = "1.0", PlannerConfidence = .5m,
                PrimaryIntent = ClinicManagement.Application.AI.DTOs.AiChatIntentTypes.UnclearOrOutOfScope,
                Clarification = "Cần làm rõ"
            });
        var providerRequest = await ChatAsync(client, "Tôi là Patient 1, CCCD 079088012345, MRN BN-2026-000001, email patient1@test.com; tôi cần hỗ trợ", null, "provider-redaction");
        Assert.Equal("Gemini", providerRequest.GetProperty("plannerMode").GetString());
        Assert.DoesNotContain("079088012345", captured, StringComparison.Ordinal);
        Assert.DoesNotContain("BN-2026-000001", captured, StringComparison.Ordinal);
        Assert.DoesNotContain("patient1@test.com", captured, StringComparison.OrdinalIgnoreCase);
        _ = fixture;
    }

    private async Task<JsonElement> ChatAsync(HttpClient client, string message, object? resource, string session)
    {
        var response = await client.PostAsJsonAsync("/api/v1/ai/copilot/chat", new
        {
            message, sessionId = $"sess_{session}_{Guid.NewGuid():N}",
            currentRoute = "/doctor/appointments",
            resourceContext = resource
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("data").Clone();
    }

    private static JsonElement AssertCard(JsonElement response, string type)
    {
        var card = response.GetProperty("cards").EnumerateArray().FirstOrDefault(x => x.GetProperty("type").GetString() == type);
        Assert.True(card.ValueKind != JsonValueKind.Undefined, $"Missing card {type}: {response.GetRawText()}");
        return card;
    }

    private async Task<(long VisitId, long OrderId)> CreateClinicalFixtureAsync(long patientId, long doctorId)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var department = await db.Departments.Where(x => x.IsActive).OrderBy(x => x.Id).FirstAsync();
        var service = await db.DiagnosticServices.Where(x => x.IsActive).OrderBy(x => x.Id).FirstAsync();
        var medicine = await db.Medicines.Where(x => x.IsActive).OrderBy(x => x.Id).FirstAsync();
        var visit = new PatientVisit
        {
            VisitCode = $"AI32-{Guid.NewGuid():N}"[..18].ToUpperInvariant(),
            PatientId = patientId, FacilityId = department.FacilityId, DepartmentId = department.Id,
            AssignedDoctorId = doctorId, AppointmentId = null,
            VisitDate = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7)), ArrivalType = VisitArrivalType.WalkIn,
            Priority = VisitPriority.Normal, ChiefComplaint = "Đau đầu tổng hợp cho test", QueueNumber = Random.Shared.Next(1000, 9000),
            Status = VisitStatus.InConsultation, CreatedByUserId = ReceptionistId
        };
        db.PatientVisits.Add(visit);
        await db.SaveChangesAsync();
        db.VisitSummaries.Add(new VisitSummary { PatientVisitId = visit.Id, DoctorId = doctorId, ChiefComplaint = visit.ChiefComplaint, Summary = "Tóm tắt ca walk-in tổng hợp", ClinicalFindings = "Khám ổn định", Diagnosis = "Theo dõi", TreatmentPlan = "Theo dõi tại nhà" });
        db.AppointmentVitalSigns.Add(new AppointmentVitalSigns { PatientVisitId = visit.Id, BloodPressureSystolic = 120, BloodPressureDiastolic = 80, HeartRate = 78, Temperature = 37.2m, RecordedByUserId = DoctorId });
        var order = new DiagnosticOrder
        {
            PatientVisitId = visit.Id, PatientId = patientId, OrderingDoctorId = doctorId, FacilityId = department.FacilityId,
            PerformingDepartmentId = department.Id, OrderCode = $"AI32-ORD-{Guid.NewGuid():N}"[..18].ToUpperInvariant(),
            ClinicalIndication = "Kiểm tra cận lâm sàng", Status = DiagnosticOrderStatus.Completed
        };
        order.Items.Add(new DiagnosticOrderItem { DiagnosticServiceId = service.Id, Status = DiagnosticItemStatus.Completed, Result = new DiagnosticResult { ResultText = "WBC 7.5", Conclusion = "Trong giới hạn", ResultedByUserId = TechnicianId } });
        db.DiagnosticOrders.Add(order);
        db.DiagnosticOrders.Add(new DiagnosticOrder
        {
            PatientVisitId = visit.Id, PatientId = patientId, OrderingDoctorId = doctorId, FacilityId = department.FacilityId,
            PerformingDepartmentId = department.Id, OrderCode = $"AI32-PEND-{Guid.NewGuid():N}"[..18].ToUpperInvariant(),
            ClinicalIndication = "Chỉ định đang chờ kết quả", Status = DiagnosticOrderStatus.InProgress,
            Items = new List<DiagnosticOrderItem> { new() { DiagnosticServiceId = service.Id, Status = DiagnosticItemStatus.InProgress } }
        });
        var prescription = new Prescription { PatientVisitId = visit.Id, PatientId = patientId, DoctorId = doctorId, Status = PrescriptionStatus.Issued, Notes = "Dùng theo hướng dẫn" };
        prescription.Items.Add(new PrescriptionItem { MedicineId = medicine.Id, Quantity = 2, Dosage = "500mg", Frequency = "2 lần/ngày", DurationDays = 2 });
        db.Prescriptions.Add(prescription);
        await db.SaveChangesAsync();
        return (visit.Id, order.Id);
    }
}
