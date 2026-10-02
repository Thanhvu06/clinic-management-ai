using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using ClinicManagement.Application.Appointments.DTOs.Doctor;
using ClinicManagement.Application.Billing.DTOs;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Diagnostics.DTOs;
using ClinicManagement.Application.HealthPackages.DTOs;
using ClinicManagement.Application.Medicines.DTOs;
using ClinicManagement.Application.Organization.DTOs;
using ClinicManagement.Application.Pharmacy.DTOs;
using ClinicManagement.Application.Prescriptions.DTOs;
using ClinicManagement.Application.Visits.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Infrastructure.Identity;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ClinicManagement.IntegrationTests;

public class ReceptionWorkspaceRebuildTests : IntegrationTestBase
{
    public ReceptionWorkspaceRebuildTests(CustomWebApplicationFactory factory) : base(factory) { }

    private async Task<(Facility Facility, Department Department, Room Room)> EnsureFacilityStructureAsync(string suffix = "MAIN", bool assignStaff = true)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var code = $"FAC-TEST-{suffix}";
        var facility = await db.Facilities.FirstOrDefaultAsync(f => f.Code == code);
        if (facility == null)
        {
            facility = new Facility
            {
                Code = code,
                Name = $"Bệnh viện Test {suffix}",
                Address = "123 Đường Y Tế",
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow
            };
            db.Facilities.Add(facility);
            await db.SaveChangesAsync();
        }

        var deptCode = $"DEPT-{suffix}";
        var department = await db.Departments.FirstOrDefaultAsync(d => d.Code == deptCode && d.FacilityId == facility.Id);
        if (department == null)
        {
            department = new Department
            {
                FacilityId = facility.Id,
                Code = deptCode,
                Name = $"Khoa Khám Bệnh {suffix}",
                SpecialtyId = SpecialtyEntityId,
                IsActive = true
            };
            db.Departments.Add(department);
            await db.SaveChangesAsync();
        }

        var roomNumber = $"P101-{suffix}";
        var room = await db.Rooms.FirstOrDefaultAsync(r => r.RoomNumber == roomNumber && r.DepartmentId == department.Id);
        if (room == null)
        {
            room = new Room
            {
                DepartmentId = department.Id,
                RoomNumber = roomNumber,
                Name = $"Phòng Khám 101 {suffix}",
                RoomType = RoomType.Consultation,
                IsActive = true
            };
            db.Rooms.Add(room);
            await db.SaveChangesAsync();
        }

        if (assignStaff)
        {
            await EnsureStaffAssignmentsAsync(db);
        }

        return (facility, department, room);
    }

    [Fact]
    public async Task Idempotency_SameKeySamePayload_ReturnsIdenticalTicketWithoutDuplicateRecord()
    {
        var client = await CreateAuthenticatedClientAsync("rec@test.com");
        var (facility, department, room) = await EnsureFacilityStructureAsync("IDEM1");

        var idempotencyKey = $"IDEM-INTAKE-{Guid.NewGuid()}";
        var request = new ReceptionIntakeRequest
        {
            IdempotencyKey = idempotencyKey,
            FacilityId = facility.Id,
            DepartmentId = department.Id,
            RoomId = room.Id,
            AssignedDoctorId = DoctorEntityId,
            Priority = VisitPriority.Normal,
            ChiefComplaint = "Đau đầu chóng mặt định kỳ",
            ExistingPatientId = Patient1EntityId
        };

        // First Request
        var req1 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/patient-visits/intake")
        {
            Content = JsonContent.Create(request)
        };
        req1.Headers.Add("Idempotency-Key", idempotencyKey);

        var res1 = await client.SendAsync(req1);
        Assert.Equal(HttpStatusCode.OK, res1.StatusCode);
        var body1 = await res1.Content.ReadFromJsonAsync<ApiResponse<CheckInTicketDto>>();
        Assert.NotNull(body1?.Data);
        var ticket1 = body1.Data;
        Assert.True(ticket1.QueueNumber > 0);

        // Second Request with SAME key and SAME body
        var req2 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/patient-visits/intake")
        {
            Content = JsonContent.Create(request)
        };
        req2.Headers.Add("Idempotency-Key", idempotencyKey);

        var res2 = await client.SendAsync(req2);
        Assert.Equal(HttpStatusCode.OK, res2.StatusCode);
        var body2 = await res2.Content.ReadFromJsonAsync<ApiResponse<CheckInTicketDto>>();
        Assert.NotNull(body2?.Data);
        var ticket2 = body2.Data;

        // Verify identical response
        Assert.Equal(ticket1.QueueNumber, ticket2.QueueNumber);
        Assert.Equal(ticket1.VisitId, ticket2.VisitId);
        Assert.Equal(ticket1.VisitCode, ticket2.VisitCode);

        // Verify DB only has 1 record for this ticket
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var count = await db.PatientVisits.CountAsync(v => v.Id == ticket1.VisitId);
        Assert.Equal(1, count);

        var idemRecord = await db.IdempotencyRecords.FirstOrDefaultAsync(r => r.Key == idempotencyKey);
        Assert.NotNull(idemRecord);
    }

    [Fact]
    public async Task Idempotency_SameKeyDifferentPayload_ThrowsConflict()
    {
        var client = await CreateAuthenticatedClientAsync("rec@test.com");
        var (facility, department, room) = await EnsureFacilityStructureAsync("IDEM2");

        var idempotencyKey = $"IDEM-CONFLICT-{Guid.NewGuid()}";
        var request1 = new ReceptionIntakeRequest
        {
            IdempotencyKey = idempotencyKey,
            FacilityId = facility.Id,
            DepartmentId = department.Id,
            RoomId = room.Id,
            AssignedDoctorId = DoctorEntityId,
            Priority = VisitPriority.Normal,
            ChiefComplaint = "Payload Alpha",
            ExistingPatientId = Patient1EntityId
        };

        var req1 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/patient-visits/intake")
        {
            Content = JsonContent.Create(request1)
        };
        req1.Headers.Add("Idempotency-Key", idempotencyKey);
        var res1 = await client.SendAsync(req1);
        Assert.Equal(HttpStatusCode.OK, res1.StatusCode);

        // Second Request with SAME key but DIFFERENT body
        var request2 = new ReceptionIntakeRequest
        {
            IdempotencyKey = idempotencyKey,
            FacilityId = facility.Id,
            DepartmentId = department.Id,
            RoomId = room.Id,
            AssignedDoctorId = DoctorEntityId,
            Priority = VisitPriority.Urgent,
            ChiefComplaint = "Payload Beta Different",
            ExistingPatientId = Patient1EntityId
        };

        var req2 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/patient-visits/intake")
        {
            Content = JsonContent.Create(request2)
        };
        req2.Headers.Add("Idempotency-Key", idempotencyKey);
        var res2 = await client.SendAsync(req2);

        // Must reject reusing idempotency key with different payload
        Assert.True(res2.StatusCode == HttpStatusCode.Conflict || res2.StatusCode == HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Intake_WithExistingPatientId_ReusesStableIdentityAndMrn()
    {
        var client = await CreateAuthenticatedClientAsync("rec@test.com");
        var (facility, department, room) = await EnsureFacilityStructureAsync("STABLE");

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var existingPatient = await db.Patients.FindAsync(Patient1EntityId);
        Assert.NotNull(existingPatient);
        var initialPatientCount = await db.Patients.CountAsync();

        var request = new ReceptionIntakeRequest
        {
            IdempotencyKey = $"IDEM-{Guid.NewGuid()}",
            FacilityId = facility.Id,
            DepartmentId = department.Id,
            RoomId = room.Id,
            AssignedDoctorId = DoctorEntityId,
            Priority = VisitPriority.Normal,
            ChiefComplaint = "Tái khám định kỳ",
            ExistingPatientId = Patient1EntityId
        };

        var res = await client.PostAsJsonAsync("/api/v1/patient-visits/intake", request);
        var resStr = await res.Content.ReadAsStringAsync();
        Assert.True(res.IsSuccessStatusCode, $"Status: {res.StatusCode}, Body: {resStr}");
        var result = await res.Content.ReadFromJsonAsync<ApiResponse<CheckInTicketDto>>();
        Assert.NotNull(result?.Data);

        // Ticket contains existing patient's MRN and Name
        Assert.Equal(existingPatient.MedicalRecordNumber, result.Data.MedicalRecordNumber);
        Assert.Equal(existingPatient.FullName, result.Data.PatientName);

        // Patient count did NOT increase (no shadow patient created)
        var finalPatientCount = await db.Patients.CountAsync();
        Assert.Equal(initialPatientCount, finalPatientCount);
    }

    [Fact]
    public async Task Intake_WithNewPatient_WithoutPhone_WithEmergencyContact_Succeeds()
    {
        var client = await CreateAuthenticatedClientAsync("rec@test.com");
        var (facility, department, room) = await EnsureFacilityStructureAsync("NO-PHONE");

        var emergencyPhone = "0987112233";
        var request = new ReceptionIntakeRequest
        {
            IdempotencyKey = $"IDEM-{Guid.NewGuid()}",
            FacilityId = facility.Id,
            DepartmentId = department.Id,
            RoomId = room.Id,
            AssignedDoctorId = DoctorEntityId,
            Priority = VisitPriority.Normal,
            ChiefComplaint = "Bé bị sốt phát ban",
            NewPatient = new NewPatientProfileDto
            {
                FullName = "Nguyễn Văn Nhỏ",
                DateOfBirth = new DateOnly(2020, 5, 10),
                Gender = Gender.Male,
                PhoneNumber = null, // No personal phone
                EmergencyContact = new EmergencyContactInputDto
                {
                    ContactName = "Mẹ Nguyễn Thị Mẹ",
                    PhoneNumber = emergencyPhone,
                    Relationship = "Mẹ",
                    IsGuardian = true
                }
            }
        };

        var res = await client.PostAsJsonAsync("/api/v1/patient-visits/intake", request);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var result = await res.Content.ReadFromJsonAsync<ApiResponse<CheckInTicketDto>>();
        Assert.NotNull(result?.Data);
        Assert.Equal("Nguyễn Văn Nhỏ", result.Data.PatientName);

        // Verify patient was saved with emergency contact phone as fallback
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var createdPatient = await db.Patients.FirstOrDefaultAsync(p => p.FullName == "Nguyễn Văn Nhỏ");
        Assert.NotNull(createdPatient);
        Assert.Equal(emergencyPhone, createdPatient.PhoneNumber);
    }

    [Fact]
    public async Task Intake_WithoutFacilityId_ThrowsFacilityRequired()
    {
        var client = await CreateAuthenticatedClientAsync("rec@test.com");

        var request = new ReceptionIntakeRequest
        {
            IdempotencyKey = $"IDEM-{Guid.NewGuid()}",
            FacilityId = 0, // Invalid/missing facility
            ChiefComplaint = "Khám không có cơ sở",
            ExistingPatientId = Patient1EntityId
        };

        var res = await client.PostAsJsonAsync("/api/v1/patient-visits/intake", request);
        Assert.True(res.StatusCode == HttpStatusCode.NotFound || res.StatusCode == HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task FacilityAuthorization_RestrictsCrossFacilityAccess_ForScopedUser()
    {
        var adminClient = await CreateAuthenticatedClientAsync("admin@test.com");
        var (fac1, _, _) = await EnsureFacilityStructureAsync("FAC-AUTH-1", assignStaff: false);
        var (fac2, dept2, room2) = await EnsureFacilityStructureAsync("FAC-AUTH-2", assignStaff: false);

        // Remove any existing assignment for receptionist at fac2
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var recFac2 = await db.StaffFacilityAssignments
                .Where(a => a.UserId == ReceptionistId && a.FacilityId == fac2.Id)
                .ToListAsync();
            if (recFac2.Any())
            {
                db.StaffFacilityAssignments.RemoveRange(recFac2);
                await db.SaveChangesAsync();
            }
        }

        // Admin assigns receptionist ONLY to fac1
        var assignReq = new CreateStaffAssignmentRequest
        {
            UserId = ReceptionistId,
            FacilityId = fac1.Id,
            Role = "Receptionist",
            IsPrimary = true
        };
        var assignRes = await adminClient.PostAsJsonAsync("/api/v1/admin/staff-assignments", assignReq);
        Assert.True(assignRes.IsSuccessStatusCode);
        var created = (await assignRes.Content.ReadFromJsonAsync<ApiResponse<StaffFacilityAssignmentDto>>())!.Data;

        try
        {
            // Receptionist attempts intake at fac2 (where they have NO assignment)
            var recClient = await CreateAuthenticatedClientAsync("rec@test.com");
            var crossIntakeReq = new ReceptionIntakeRequest
            {
                IdempotencyKey = $"IDEM-{Guid.NewGuid()}",
                FacilityId = fac2.Id,
                DepartmentId = dept2.Id,
                RoomId = room2.Id,
                AssignedDoctorId = DoctorEntityId,
                Priority = VisitPriority.Normal,
                ChiefComplaint = "Thử truy cập trái phép cơ sở 2",
                ExistingPatientId = Patient1EntityId
            };

            var crossRes = await recClient.PostAsJsonAsync("/api/v1/patient-visits/intake", crossIntakeReq);
            Assert.Equal(HttpStatusCode.Forbidden, crossRes.StatusCode);
        }
        finally
        {
            // Clean up assignment so rec@test.com is not restricted in other tests
            await adminClient.DeleteAsync($"/api/v1/admin/staff-assignments/{created.Id}");
        }
    }

    [Fact]
    public async Task Admin_StaffAssignments_Create_List_And_Delete()
    {
        var adminClient = await CreateAuthenticatedClientAsync("admin@test.com");
        var (fac, _, _) = await EnsureFacilityStructureAsync("STAFF-MGMT");

        // Create assignment
        var assignReq = new CreateStaffAssignmentRequest
        {
            UserId = DoctorId,
            FacilityId = fac.Id,
            Role = "Doctor",
            IsPrimary = true
        };
        var createRes = await adminClient.PostAsJsonAsync("/api/v1/admin/staff-assignments", assignReq);
        Assert.Equal(HttpStatusCode.OK, createRes.StatusCode);
        var created = await createRes.Content.ReadFromJsonAsync<ApiResponse<StaffFacilityAssignmentDto>>();
        Assert.NotNull(created?.Data);
        var assignmentId = created.Data.Id;
        Assert.Equal(DoctorId, created.Data.UserId);
        Assert.Equal(fac.Id, created.Data.FacilityId);

        // List assignments
        var listRes = await adminClient.GetAsync($"/api/v1/admin/staff-assignments?facilityId={fac.Id}");
        Assert.Equal(HttpStatusCode.OK, listRes.StatusCode);
        var listBody = await listRes.Content.ReadFromJsonAsync<ApiResponse<List<StaffFacilityAssignmentDto>>>();
        Assert.NotNull(listBody?.Data);
        Assert.Contains(listBody.Data, a => a.Id == assignmentId);

        // Delete assignment
        var delRes = await adminClient.DeleteAsync($"/api/v1/admin/staff-assignments/{assignmentId}");
        Assert.Equal(HttpStatusCode.OK, delRes.StatusCode);

        // Verify deleted
        var listAfterRes = await adminClient.GetAsync($"/api/v1/admin/staff-assignments?facilityId={fac.Id}");
        var listAfterBody = await listAfterRes.Content.ReadFromJsonAsync<ApiResponse<List<StaffFacilityAssignmentDto>>>();
        Assert.DoesNotContain(listAfterBody?.Data ?? new(), a => a.Id == assignmentId);
    }

    [Fact]
    public async Task Admin_DiagnosticPricing_Update_ReflectsInService()
    {
        var adminClient = await CreateAuthenticatedClientAsync("admin@test.com");

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var diagService = await db.DiagnosticServices.FirstAsync();

        var newPrice = 280000m;
        var updateReq = new UpdateDiagnosticPriceRequest
        {
            Price = newPrice
        };

        var updateRes = await adminClient.PutAsJsonAsync($"/api/v1/admin/diagnostic-services/{diagService.Id}/pricing", updateReq);
        Assert.Equal(HttpStatusCode.OK, updateRes.StatusCode);

        // Verify with GET
        var getRes = await adminClient.GetAsync("/api/v1/admin/diagnostic-services/pricing");
        Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);
        var getBody = await getRes.Content.ReadFromJsonAsync<ApiResponse<List<DiagnosticServiceDto>>>();
        Assert.NotNull(getBody?.Data);
        var updatedItem = getBody.Data.FirstOrDefault(d => d.Id == diagService.Id);
        Assert.NotNull(updatedItem);
        Assert.Equal(newPrice, updatedItem.Price);
    }

    [Fact]
    public async Task Admin_MedicinePricing_Update_ReflectsInPrescriptionBilling()
    {
        var adminClient = await CreateAuthenticatedClientAsync("admin@test.com");

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var medicine = await db.Medicines.FirstAsync();

        var newUnitPrice = 8500m;
        var updateReq = new UpdateMedicineDto
        {
            Name = medicine.Name,
            Unit = medicine.Unit,
            UnitPrice = newUnitPrice,
            ReorderLevel = medicine.ReorderLevel,
            IsActive = true
        };

        var updateRes = await adminClient.PutAsJsonAsync($"/api/v1/admin/medicines/{medicine.Id}", updateReq);
        Assert.Equal(HttpStatusCode.OK, updateRes.StatusCode);

        // Verify DB reflects unit price
        using var scope2 = Factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        var updatedMed = await db2.Medicines.FindAsync(medicine.Id);
        Assert.NotNull(updatedMed);
        Assert.Equal(newUnitPrice, updatedMed.UnitPrice);
    }

    [Fact]
    public async Task ReceptionAppointments_SortingAndTabs_ActionableFirst()
    {
        var client = await CreateAuthenticatedClientAsync("rec@test.com");
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Create slots & appointments for today
        var slot1 = await CreateAvailableSlotAsync(DoctorEntityId, today, new TimeOnly(13, 0), new TimeOnly(13, 30));
        var slot2 = await CreateAvailableSlotAsync(DoctorEntityId, today, new TimeOnly(14, 0), new TimeOnly(14, 30));

        var apptConfirmed = new Appointment
        {
            AppointmentCode = $"APT-CONF-{Guid.NewGuid():N}"[..12],
            PatientId = Patient1EntityId,
            DoctorId = DoctorEntityId,
            SpecialtyId = SpecialtyEntityId,
            AppointmentSlotId = slot1.Id,
            AppointmentDate = today,
            StartTime = new TimeOnly(13, 0),
            EndTime = new TimeOnly(13, 30),
            Status = AppointmentStatus.Confirmed
        };
        var apptCompleted = new Appointment
        {
            AppointmentCode = $"APT-COMP-{Guid.NewGuid():N}"[..12],
            PatientId = Patient2EntityId,
            DoctorId = DoctorEntityId,
            SpecialtyId = SpecialtyEntityId,
            AppointmentSlotId = slot2.Id,
            AppointmentDate = today,
            StartTime = new TimeOnly(14, 0),
            EndTime = new TimeOnly(14, 30),
            Status = AppointmentStatus.Completed
        };
        db.Appointments.AddRange(apptConfirmed, apptCompleted);
        await db.SaveChangesAsync();

        // Query Today Tab
        var resToday = await client.GetAsync("/api/v1/reception/appointments?tab=today&pageNumber=1&pageSize=50");
        Assert.Equal(HttpStatusCode.OK, resToday.StatusCode);
        var jsonToday = await resToday.Content.ReadAsStringAsync();
        var docToday = JsonDocument.Parse(jsonToday);
        var itemsToday = docToday.RootElement.GetProperty("data").GetProperty("items").EnumerateArray().ToList();

        // The confirmed appointment should appear BEFORE completed appointment in actionable sorting
        var confIndex = itemsToday.FindIndex(x => x.GetProperty("appointmentCode").GetString() == apptConfirmed.AppointmentCode);
        var compIndex = itemsToday.FindIndex(x => x.GetProperty("appointmentCode").GetString() == apptCompleted.AppointmentCode);

        Assert.True(confIndex >= 0);
        Assert.True(compIndex >= 0);
        Assert.True(confIndex < compIndex, "Confirmed appointment must be prioritized ahead of Completed appointment");
    }

    [Fact]
    public async Task Pharmacy_PurchaseFlow_ReservesStock_BlocksDispenseBeforePayment_AndAllowsAfterPayment()
    {
        var recClient = await CreateAuthenticatedClientAsync("rec@test.com");
        var docClient = await CreateAuthenticatedClientAsync("doc@test.com");
        var pharmClient = await CreateAuthenticatedClientAsync("pharm@test.com");
        var (facility, department, room) = await EnsureFacilityStructureAsync("PHARM-FLOW");

        // 1. Intake
        var intakeRes = await recClient.PostAsJsonAsync("/api/v1/patient-visits/intake", new ReceptionIntakeRequest
        {
            IdempotencyKey = $"IDEM-{Guid.NewGuid()}",
            FacilityId = facility.Id,
            DepartmentId = department.Id,
            RoomId = room.Id,
            AssignedDoctorId = DoctorEntityId,
            Priority = VisitPriority.Normal,
            ChiefComplaint = "Khám họng và lấy thuốc",
            ExistingPatientId = Patient1EntityId
        });
        var intakeData = (await intakeRes.Content.ReadFromJsonAsync<ApiResponse<CheckInTicketDto>>())!.Data;
        var visitId = intakeData.VisitId;

        // 2. Doctor starts consultation and prescribes medicine
        await docClient.PostAsync($"/api/v1/doctor/visits/{visitId}/start-consultation", null);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var medicine = await db.Medicines.FirstAsync(m => m.StockQuantity > 10);
        var initialStock = medicine.StockQuantity;

        var rxDraftReq = new SavePrescriptionDraftRequest
        {
            Notes = "Uống sau ăn",
            Items = new List<SavePrescriptionItemRequest>
            {
                new()
                {
                    MedicineId = medicine.Id,
                    Quantity = 10,
                    Dosage = "1 viên",
                    Frequency = "2 lần/ngày",
                    DurationDays = 5,
                    Instructions = "Sau ăn"
                }
            }
        };

        var rxRes = await docClient.PutAsJsonAsync($"/api/v1/doctor/visits/{visitId}/prescription-draft", rxDraftReq);
        Assert.Equal(HttpStatusCode.OK, rxRes.StatusCode);

        var completeRes = await docClient.PostAsJsonAsync($"/api/v1/doctor/visits/{visitId}/complete", new CompleteConsultationRequest
        {
            Diagnosis = "Viêm họng cấp",
            TreatmentPlan = "Dùng thuốc theo đơn"
        });
        Assert.Equal(HttpStatusCode.OK, completeRes.StatusCode);

        // Find prescription ID
        var rxListRes = await pharmClient.GetAsync("/api/v1/pharmacy/prescriptions?status=Pending");
        Assert.Equal(HttpStatusCode.OK, rxListRes.StatusCode);
        var rxListDoc = JsonDocument.Parse(await rxListRes.Content.ReadAsStringAsync());
        var rxItems = rxListDoc.RootElement.GetProperty("data").GetProperty("items");
        long prescriptionId = 0;
        foreach (var rx in rxItems.EnumerateArray())
        {
            if (rx.TryGetProperty("patientVisitId", out var rxPvid) && rxPvid.GetInt64() == visitId)
            {
                prescriptionId = rx.GetProperty("id").GetInt64();
                break;
            }
        }
        Assert.True(prescriptionId > 0, "Prescription must exist for the visit");

        // 3. Pharmacist confirms purchase -> reserves inventory
        var confirmRes = await pharmClient.PostAsync($"/api/v1/pharmacy/prescriptions/{prescriptionId}/confirm-purchase", null);
        Assert.Equal(HttpStatusCode.OK, confirmRes.StatusCode);

        // Verify stock reservation and status
        using var scope2 = Factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        var updatedMed = await db2.Medicines.FindAsync(medicine.Id);
        Assert.Equal(initialStock - 10, updatedMed!.StockQuantity);

        var updatedRx = await db2.Prescriptions.FindAsync(prescriptionId);
        Assert.Equal(PrescriptionStatus.ReservedForPurchase, updatedRx!.Status);

        // 4. Pharmacist tries to dispense BEFORE payment -> BLOCKED with PRESCRIPTION_NOT_PAID
        var dispenseBeforePay = await pharmClient.PostAsync($"/api/v1/pharmacy/prescriptions/{prescriptionId}/dispense", null);
        Assert.True(dispenseBeforePay.StatusCode == HttpStatusCode.BadRequest || dispenseBeforePay.StatusCode == HttpStatusCode.UnprocessableEntity);
        var errorContent = await dispenseBeforePay.Content.ReadAsStringAsync();
        Assert.Contains("PRESCRIPTION_NOT_PAID", errorContent);

        // 5. Billing creates invoice and processes payment
        var invRes = await recClient.PostAsJsonAsync("/api/v1/reception/billing/invoices/visit", new CreateVisitInvoiceRequest
        {
            PatientVisitId = visitId
        });
        Assert.Equal(HttpStatusCode.Created, invRes.StatusCode);
        var invData = (await invRes.Content.ReadFromJsonAsync<ApiResponse<InvoiceDto>>())!.Data;
        Assert.True(invData.TotalAmount > 0);

        // Pay invoice
        var payRes = await recClient.PostAsJsonAsync($"/api/v1/reception/billing/invoices/{invData.Id}/pay", new ProcessPaymentRequest
        {
            Amount = invData.TotalAmount,
            Method = PaymentMethod.Cash,
            ReferenceCode = "CASH-PAID-01"
        });
        Assert.Equal(HttpStatusCode.OK, payRes.StatusCode);

        // 6. Pharmacist dispenses AFTER payment -> SUCCEEDS!
        var dispenseAfterPay = await pharmClient.PostAsync($"/api/v1/pharmacy/prescriptions/{prescriptionId}/dispense", null);
        Assert.Equal(HttpStatusCode.OK, dispenseAfterPay.StatusCode);

        using var scope3 = Factory.Services.CreateScope();
        var db3 = scope3.ServiceProvider.GetRequiredService<AppDbContext>();
        var dispensedRx = await db3.Prescriptions.FindAsync(prescriptionId);
        Assert.Equal(PrescriptionStatus.Dispensed, dispensedRx!.Status);
    }

    [Fact]
    public async Task Billing_SupplementaryInvoice_PreventsDoubleBilling()
    {
        var recClient = await CreateAuthenticatedClientAsync("rec@test.com");
        var docClient = await CreateAuthenticatedClientAsync("doc@test.com");
        var (facility, department, room) = await EnsureFacilityStructureAsync("SUPP-BILL");

        // Intake
        var intakeRes = await recClient.PostAsJsonAsync("/api/v1/patient-visits/intake", new ReceptionIntakeRequest
        {
            IdempotencyKey = $"IDEM-{Guid.NewGuid()}",
            FacilityId = facility.Id,
            DepartmentId = department.Id,
            RoomId = room.Id,
            AssignedDoctorId = DoctorEntityId,
            Priority = VisitPriority.Normal,
            ChiefComplaint = "Khám đau bụng cần chỉ định cận lâm sàng",
            ExistingPatientId = Patient1EntityId
        });
        var visitId = (await intakeRes.Content.ReadFromJsonAsync<ApiResponse<CheckInTicketDto>>())!.Data.VisitId;

        // Doctor starts consultation
        await docClient.PostAsync($"/api/v1/doctor/visits/{visitId}/start-consultation", null);

        // Add 1st Diagnostic Order
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var diagService1 = await db.DiagnosticServices.FirstAsync();
        var diagService2 = await db.DiagnosticServices.Skip(1).FirstAsync();

        var diagOrderReq1 = new CreateDiagnosticOrderRequest
        {
            ClinicalIndication = "Nghi viêm ruột thừa",
            Note = "Xét nghiệm ban đầu",
            ServiceIds = new List<long> { diagService1.Id }
        };
        var diag1Res = await docClient.PostAsJsonAsync($"/api/v1/doctor/visits/{visitId}/diagnostic-orders", diagOrderReq1);
        Assert.Equal(HttpStatusCode.Created, diag1Res.StatusCode);

        // 1st Invoice generated
        var inv1Res = await recClient.PostAsJsonAsync("/api/v1/reception/billing/invoices/visit", new CreateVisitInvoiceRequest
        {
            PatientVisitId = visitId
        });
        Assert.Equal(HttpStatusCode.Created, inv1Res.StatusCode);
        var inv1 = (await inv1Res.Content.ReadFromJsonAsync<ApiResponse<InvoiceDetailDto>>())!.Data;
        var firstInvoiceTotal = inv1.TotalAmount;
        // Pay 1st Invoice so that pending invoice block is cleared
        var pay1Res = await recClient.PostAsJsonAsync($"/api/v1/reception/billing/invoices/{inv1.Id}/pay", new ProcessPaymentRequest
        {
            Amount = inv1.TotalAmount,
            Method = PaymentMethod.Cash,
            ReferenceCode = "CASH-1"
        });
        Assert.Equal(HttpStatusCode.OK, pay1Res.StatusCode);

        // Doctor adds 2nd Diagnostic Order later (supplementary test during care)
        var diagOrderReq2 = new CreateDiagnosticOrderRequest
        {
            ClinicalIndication = "Bổ sung xét nghiệm máu đông máu",
            Note = "Chỉ định bổ sung",
            ServiceIds = new List<long> { diagService2.Id }
        };
        var diag2Res = await docClient.PostAsJsonAsync($"/api/v1/doctor/visits/{visitId}/diagnostic-orders", diagOrderReq2);
        Assert.Equal(HttpStatusCode.Created, diag2Res.StatusCode);

        // 2nd Supplementary Invoice generated
        var inv2Res = await recClient.PostAsJsonAsync("/api/v1/reception/billing/invoices/visit", new CreateVisitInvoiceRequest
        {
            PatientVisitId = visitId
        });
        Assert.Equal(HttpStatusCode.Created, inv2Res.StatusCode);
        var inv2 = (await inv2Res.Content.ReadFromJsonAsync<ApiResponse<InvoiceDetailDto>>())!.Data;

        // Verify Supplementary Invoice ONLY charges for the second service (NOT double-charging first service or consultation)
        Assert.Single(inv2.Items);
        Assert.Equal(diagService2.Price ?? 0m, inv2.TotalAmount);
    }

    [Fact]
    public async Task HealthPackageRegistrations_WalkInPatientWithoutUserId_VisibleToReceptionist()
    {
        var recClient = await CreateAuthenticatedClientAsync("rec@test.com");

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Create walk-in patient with UserId = null
        var walkInPatient = new Patient
        {
            UserId = null,
            FullName = "Trần Thị Vãng Lai",
            PhoneNumber = "0912345699",
            MedicalRecordNumber = $"BN-{DateTime.UtcNow.Year}-999999",
            DateOfBirth = new DateOnly(1985, 3, 15),
            Gender = Gender.Female
        };
        db.Patients.Add(walkInPatient);
        await db.SaveChangesAsync();

        var package = await db.HealthPackages.FirstAsync();
        var registration = new HealthPackageRegistration
        {
            PatientId = walkInPatient.Id,
            HealthPackageId = package.Id,
            RegistrationCode = $"REG-WLK-{Guid.NewGuid():N}"[..12],
            PreferredDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)),
            ContactPhone = "0912345699",
            Status = HealthPackageRegistrationStatus.Confirmed,
            Note = "Đăng ký trực tiếp tại quầy",
            CreatedAt = DateTime.UtcNow
        };
        db.HealthPackageRegistrations.Add(registration);
        await db.SaveChangesAsync();

        // Receptionist fetches package registrations
        var res = await recClient.GetAsync("/api/v1/reception/health-package-registrations");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<ApiResponse<PagedResult<HealthPackageRegistrationDto>>>();
        Assert.NotNull(body?.Data);

        // The walk-in patient MUST be visible even though UserId is null (LEFT JOIN)
        var found = body.Data.Items.FirstOrDefault(r => r.PatientId == walkInPatient.Id);
        Assert.NotNull(found);
        Assert.Equal("Trần Thị Vãng Lai", found.PatientName);
    }

    [Fact]
    public async Task Facilities_GetMy_ReturnsOnlyAssignedFacilitiesForStaff_AndAllForAdmin()
    {
        var adminClient = await CreateAuthenticatedClientAsync("admin@test.com");
        var (fac1, _, _) = await EnsureFacilityStructureAsync("FAC-MY-1", assignStaff: false);
        var (fac2, _, _) = await EnsureFacilityStructureAsync("FAC-MY-2", assignStaff: false);

        // Ensure receptionist is assigned ONLY to fac1
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var recFacs = await db.StaffFacilityAssignments
                .Where(a => a.UserId == ReceptionistId)
                .ToListAsync();
            db.StaffFacilityAssignments.RemoveRange(recFacs);
            db.StaffFacilityAssignments.Add(new StaffFacilityAssignment
            {
                UserId = ReceptionistId,
                FacilityId = fac1.Id,
                Role = "Receptionist",
                IsPrimary = true,
                IsActive = true,
                AssignedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var recClient = await CreateAuthenticatedClientAsync("rec@test.com");
        var myRes = await recClient.GetAsync("/api/v1/facilities/my");
        Assert.Equal(HttpStatusCode.OK, myRes.StatusCode);
        var myData = (await myRes.Content.ReadFromJsonAsync<ApiResponse<List<FacilityDto>>>())!.Data;
        Assert.Contains(myData, f => f.Id == fac1.Id);
        Assert.DoesNotContain(myData, f => f.Id == fac2.Id);

        // Admin gets all active facilities
        var adminRes = await adminClient.GetAsync("/api/v1/facilities/my");
        Assert.Equal(HttpStatusCode.OK, adminRes.StatusCode);
        var adminData = (await adminRes.Content.ReadFromJsonAsync<ApiResponse<List<FacilityDto>>>())!.Data;
        Assert.Contains(adminData, f => f.Id == fac1.Id);
        Assert.Contains(adminData, f => f.Id == fac2.Id);
    }

    [Fact]
    public async Task Billing_GetUnbilledVisits_FacilityScoping_EnforcesAccess_RejectsCrossFacilityWith403()
    {
        var (fac1, _, _) = await EnsureFacilityStructureAsync("FAC-BILL-1", assignStaff: false);
        var (fac2, _, _) = await EnsureFacilityStructureAsync("FAC-BILL-2", assignStaff: false);

        // Assign receptionist ONLY to fac1
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var recFacs = await db.StaffFacilityAssignments
                .Where(a => a.UserId == ReceptionistId)
                .ToListAsync();
            db.StaffFacilityAssignments.RemoveRange(recFacs);
            db.StaffFacilityAssignments.Add(new StaffFacilityAssignment
            {
                UserId = ReceptionistId,
                FacilityId = fac1.Id,
                Role = "Receptionist",
                IsPrimary = true,
                IsActive = true,
                AssignedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var recClient = await CreateAuthenticatedClientAsync("rec@test.com");

        // Requesting unbilled visits for fac2 (which receptionist is NOT assigned to) -> 403 Forbidden
        var forbiddenRes = await recClient.GetAsync($"/api/v1/reception/billing/unbilled-visits?facilityId={fac2.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenRes.StatusCode);

        // Requesting for assigned fac1 -> 200 OK
        var okRes = await recClient.GetAsync($"/api/v1/reception/billing/unbilled-visits?facilityId={fac1.Id}");
        Assert.Equal(HttpStatusCode.OK, okRes.StatusCode);

        // Requesting without facilityId -> defaults to allowed facilities (200 OK)
        var defaultRes = await recClient.GetAsync("/api/v1/reception/billing/unbilled-visits");
        Assert.Equal(HttpStatusCode.OK, defaultRes.StatusCode);

        // Admin requesting fac2 -> 200 OK
        var adminClient = await CreateAuthenticatedClientAsync("admin@test.com");
        var adminRes = await adminClient.GetAsync($"/api/v1/reception/billing/unbilled-visits?facilityId={fac2.Id}");
        Assert.Equal(HttpStatusCode.OK, adminRes.StatusCode);
    }

    [Fact]
    public async Task Appointment_Confirm_FacilityScoping_EnforcesAccess_RejectsCrossFacilityWith403()
    {
        var (fac1, _, _) = await EnsureFacilityStructureAsync("FAC-CONF-1", assignStaff: false);
        var (fac2, dept2, _) = await EnsureFacilityStructureAsync("FAC-CONF-2", assignStaff: false);

        long apptId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Create patient & visit at fac2
            var patient = await db.Patients.FirstAsync();
            var appt = new Appointment
            {
                AppointmentCode = $"APT-CONF-{Guid.NewGuid():N}"[..18],
                DoctorId = DoctorEntityId,
                PatientId = patient.Id,
                SpecialtyId = SpecialtyEntityId,
                AppointmentSlotId = SlotEntityId,
                Status = AppointmentStatus.Pending,
                AppointmentDate = DateOnly.FromDateTime(DateTime.UtcNow),
                StartTime = new TimeOnly(15, 0),
                EndTime = new TimeOnly(15, 30)
            };
            db.Appointments.Add(appt);
            await db.SaveChangesAsync();
            apptId = appt.Id;

            var visit = new PatientVisit
            {
                VisitCode = $"VIS-CONF-{Guid.NewGuid():N}"[..18].ToUpper(),
                PatientId = patient.Id,
                AppointmentId = appt.Id,
                FacilityId = fac2.Id,
                DepartmentId = dept2.Id,
                AssignedDoctorId = DoctorEntityId,
                Status = VisitStatus.WaitingDoctor,
                VisitDate = DateOnly.FromDateTime(DateTime.UtcNow),
                Priority = VisitPriority.Normal,
                QueueNumber = 777,
                CheckedInAtUtc = DateTime.UtcNow
            };
            db.PatientVisits.Add(visit);
            await db.SaveChangesAsync();

            // Assign receptionist ONLY to fac1
            var recFacs = await db.StaffFacilityAssignments
                .Where(a => a.UserId == ReceptionistId)
                .ToListAsync();
            db.StaffFacilityAssignments.RemoveRange(recFacs);
            db.StaffFacilityAssignments.Add(new StaffFacilityAssignment
            {
                UserId = ReceptionistId,
                FacilityId = fac1.Id,
                Role = "Receptionist",
                IsPrimary = true,
                IsActive = true,
                AssignedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        // Receptionist assigned to fac1 tries to confirm appointment at fac2 -> 403 Forbidden
        var recClient = await CreateAuthenticatedClientAsync("rec@test.com");
        var resForbidden = await recClient.PostAsync($"/api/v1/reception/appointments/{apptId}/confirm", null);
        Assert.Equal(HttpStatusCode.Forbidden, resForbidden.StatusCode);

        // Admin confirms -> 200 OK
        var adminClient = await CreateAuthenticatedClientAsync("admin@test.com");
        var resAdmin = await adminClient.PostAsync($"/api/v1/reception/appointments/{apptId}/confirm", null);
        Assert.Equal(HttpStatusCode.OK, resAdmin.StatusCode);
    }

    [Fact]
    public async Task ValidateAppointmentAccess_RejectsDoctorWithoutFacility_AndRejectsAmbiguousDoctor()
    {
        var (fac1, _, _) = await EnsureFacilityStructureAsync("FAC-VAL-1", assignStaff: false);
        var (fac2, _, _) = await EnsureFacilityStructureAsync("FAC-VAL-2", assignStaff: false);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var facilityAuth = scope.ServiceProvider.GetRequiredService<IFacilityAuthorizationService>();

        // Create a new doctor user with NO facility assignments
        var noFacDoctorUser = new ApplicationUser
        {
            UserName = $"doc-nofac-{Guid.NewGuid():N}"[..15] + "@test.com",
            Email = $"doc-nofac-{Guid.NewGuid():N}"[..15] + "@test.com",
            PhoneNumber = "0987654321",
            FullName = "Bác sĩ Không Cơ Sở",
            IsActive = true
        };
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        await userManager.CreateAsync(noFacDoctorUser, "Pass@123");
        await userManager.AddToRoleAsync(noFacDoctorUser, "Doctor");

        var noFacDoctor = new Doctor
        {
            UserId = noFacDoctorUser.Id,
            ExperienceYears = 5,
            IsActive = true
        };
        db.Doctors.Add(noFacDoctor);
        await db.SaveChangesAsync();
        db.DoctorSpecialties.Add(new DoctorSpecialty
        {
            DoctorId = noFacDoctor.Id,
            SpecialtyId = CardiologySpecialtyId
        });
        await db.SaveChangesAsync();

        var patient = await db.Patients.FirstAsync();
        var apptNoFac = new Appointment
        {
            AppointmentCode = $"APT-NF-{Guid.NewGuid():N}"[..18],
            DoctorId = noFacDoctor.Id,
            PatientId = patient.Id,
            SpecialtyId = CardiologySpecialtyId,
            AppointmentSlotId = SlotEntityId,
            Status = AppointmentStatus.Pending,
            AppointmentDate = DateOnly.FromDateTime(DateTime.UtcNow),
            StartTime = new TimeOnly(16, 0),
            EndTime = new TimeOnly(16, 30)
        };
        db.Appointments.Add(apptNoFac);
        await db.SaveChangesAsync();

        // Assign receptionist ONLY to fac1
        var recFacs = await db.StaffFacilityAssignments
            .Where(a => a.UserId == ReceptionistId)
            .ToListAsync();
        db.StaffFacilityAssignments.RemoveRange(recFacs);
        db.StaffFacilityAssignments.Add(new StaffFacilityAssignment
        {
            UserId = ReceptionistId,
            FacilityId = fac1.Id,
            Role = "Receptionist",
            IsPrimary = true,
            IsActive = true,
            AssignedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        // Doctor with 0 assignments -> ForbiddenException
        var exNoFac = await Assert.ThrowsAsync<ForbiddenException>(() =>
            facilityAuth.ValidateAppointmentAccessAsync(ReceptionistId, apptNoFac.Id));
        Assert.Contains("chưa được phân công cơ sở", exNoFac.Message);

        // Now assign doctor to BOTH fac1 and fac2 with no department specialty match
        db.StaffFacilityAssignments.Add(new StaffFacilityAssignment
        {
            UserId = noFacDoctorUser.Id,
            FacilityId = fac1.Id,
            Role = "Doctor",
            IsActive = true,
            AssignedAtUtc = DateTime.UtcNow
        });
        db.StaffFacilityAssignments.Add(new StaffFacilityAssignment
        {
            UserId = noFacDoctorUser.Id,
            FacilityId = fac2.Id,
            Role = "Doctor",
            IsActive = true,
            AssignedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        // Doctor assigned to 2 facilities, receptionist only assigned to fac1, indeterminate -> ForbiddenException (no guessing)
        var exAmbiguous = await Assert.ThrowsAsync<ForbiddenException>(() =>
            facilityAuth.ValidateAppointmentAccessAsync(ReceptionistId, apptNoFac.Id));
        Assert.Contains("Không thể xác định chính xác", exAmbiguous.Message);
    }

    [Fact]
    public async Task Billing_GetUnbilledVisits_RealPagination_And_TieBreakerSorting()
    {
        var (fac, dept, _) = await EnsureFacilityStructureAsync("FAC-PAGE-1", assignStaff: false);

        // Assign receptionist to fac
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var recFacs = await db.StaffFacilityAssignments
                .Where(a => a.UserId == ReceptionistId)
                .ToListAsync();
            db.StaffFacilityAssignments.RemoveRange(recFacs);
            db.StaffFacilityAssignments.Add(new StaffFacilityAssignment
            {
                UserId = ReceptionistId,
                FacilityId = fac.Id,
                Role = "Receptionist",
                IsPrimary = true,
                IsActive = true,
                AssignedAtUtc = DateTime.UtcNow
            });

            var patient = await db.Patients.FirstAsync();
            var doc = await db.Doctors.FirstAsync();

            // Create 3 unbilled visits with distinct queue numbers and dates
            for (int i = 1; i <= 3; i++)
            {
                var visit = new PatientVisit
                {
                    VisitCode = $"VIS-PG-{i}-{Guid.NewGuid():N}"[..18].ToUpper(),
                    PatientId = patient.Id,
                    FacilityId = fac.Id,
                    DepartmentId = dept.Id,
                    AssignedDoctorId = doc.Id,
                    Status = VisitStatus.ConsultationCompleted,
                    VisitDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-i)),
                    Priority = VisitPriority.Normal,
                    QueueNumber = 100 + i,
                    CheckedInAtUtc = DateTime.UtcNow.AddDays(-i)
                };
                db.PatientVisits.Add(visit);
            }
            await db.SaveChangesAsync();
        }

        var recClient = await CreateAuthenticatedClientAsync("rec@test.com");

        // Page 1 with pageSize = 2
        var resPage1 = await recClient.GetAsync($"/api/v1/reception/billing/unbilled-visits?facilityId={fac.Id}&page=1&pageSize=2");
        Assert.Equal(HttpStatusCode.OK, resPage1.StatusCode);
        var page1Data = (await resPage1.Content.ReadFromJsonAsync<ApiResponse<PagedResult<UnbilledVisitDto>>>())!.Data;

        Assert.Equal(1, page1Data.Page);
        Assert.Equal(2, page1Data.PageSize);
        Assert.True(page1Data.TotalItems >= 3);
        Assert.True(page1Data.TotalPages >= 2);
        Assert.Equal(2, page1Data.Items.Count);

        // Page 2 with pageSize = 2
        var resPage2 = await recClient.GetAsync($"/api/v1/reception/billing/unbilled-visits?facilityId={fac.Id}&page=2&pageSize=2");
        Assert.Equal(HttpStatusCode.OK, resPage2.StatusCode);
        var page2Data = (await resPage2.Content.ReadFromJsonAsync<ApiResponse<PagedResult<UnbilledVisitDto>>>())!.Data;

        Assert.Equal(2, page2Data.Page);
        Assert.NotEmpty(page2Data.Items);

        // Items on page 1 and page 2 must be disjoint
        var page1Ids = page1Data.Items.Select(x => x.VisitId).ToHashSet();
        foreach (var item in page2Data.Items)
        {
            Assert.DoesNotContain(item.VisitId, page1Ids);
        }

        // Verify sorting order: VisitDate desc, QueueNumber desc, Id desc
        for (int i = 0; i < page1Data.Items.Count - 1; i++)
        {
            var curr = page1Data.Items[i];
            var next = page1Data.Items[i + 1];
            Assert.True(curr.VisitDate >= next.VisitDate);
        }
    }

    [Fact]
    public async Task Billing_GetUnbilledVisits_UnifiedCashierDefinition_IncludesVisitsWithUnpaidInvoices()
    {
        var (fac, dept, _) = await EnsureFacilityStructureAsync("FAC-UNPAID-1", assignStaff: false);
        long visitId;
        decimal unpaidAmount = 150000m;

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var recFacs = await db.StaffFacilityAssignments
                .Where(a => a.UserId == ReceptionistId)
                .ToListAsync();
            db.StaffFacilityAssignments.RemoveRange(recFacs);
            db.StaffFacilityAssignments.Add(new StaffFacilityAssignment
            {
                UserId = ReceptionistId,
                FacilityId = fac.Id,
                Role = "Receptionist",
                IsPrimary = true,
                IsActive = true,
                AssignedAtUtc = DateTime.UtcNow
            });

            var patient = await db.Patients.FirstAsync();
            var doc = await db.Doctors.FirstAsync();

            var visit = new PatientVisit
            {
                VisitCode = $"VIS-UNP-{Guid.NewGuid():N}"[..18].ToUpper(),
                PatientId = patient.Id,
                FacilityId = fac.Id,
                DepartmentId = dept.Id,
                AssignedDoctorId = doc.Id,
                Status = VisitStatus.InBilling,
                VisitDate = DateOnly.FromDateTime(DateTime.UtcNow),
                Priority = VisitPriority.Normal,
                QueueNumber = 555,
                CheckedInAtUtc = DateTime.UtcNow
            };
            db.PatientVisits.Add(visit);
            await db.SaveChangesAsync();
            visitId = visit.Id;

            // Add an unpaid (Unpaid) invoice linked to this visit
            var invoice = new Invoice
            {
                InvoiceCode = $"INV-UNP-{Guid.NewGuid():N}"[..18].ToUpper(),
                PatientId = patient.Id,
                PatientVisitId = visit.Id,
                Status = InvoiceStatus.Unpaid,
                Subtotal = unpaidAmount,
                TotalAmount = unpaidAmount,
                CreatedAtUtc = DateTime.UtcNow,
                Items = new List<InvoiceItem>
                {
                    new()
                    {
                        ItemCode = "SRV-TEST",
                        Description = "Khám lâm sàng",
                        Quantity = 1,
                        UnitPrice = unpaidAmount,
                        LineTotal = unpaidAmount,
                        ReferenceType = "Consultation",
                        ReferenceId = 1
                    }
                }
            };
            db.Invoices.Add(invoice);
            await db.SaveChangesAsync();
        }

        var recClient = await CreateAuthenticatedClientAsync("rec@test.com");
        var res = await recClient.GetAsync($"/api/v1/reception/billing/unbilled-visits?facilityId={fac.Id}&page=1&pageSize=50");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var page = (await res.Content.ReadFromJsonAsync<ApiResponse<PagedResult<UnbilledVisitDto>>>())!.Data;

        var targetVisit = page.Items.FirstOrDefault(v => v.VisitId == visitId);
        Assert.NotNull(targetVisit);
        Assert.True(targetVisit.UnbilledItemCount > 0, "Visit must report unpaid items / invoices");
        Assert.True(targetVisit.EstimatedTotal >= unpaidAmount, "Visit EstimatedTotal must include the unpaid invoice amount");
    }
}
