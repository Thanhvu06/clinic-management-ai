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
using ClinicManagement.Application.Visits.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ClinicManagement.IntegrationTests;

public class ConnectedOutpatientCareJourneyTests : IntegrationTestBase
{
    public ConnectedOutpatientCareJourneyTests(CustomWebApplicationFactory factory) : base(factory) { }

    private async Task<(long DepartmentId, long FacilityId)> EnsureFacilityAndDepartmentAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClinicManagement.Infrastructure.Persistence.AppDbContext>();

        var med = await db.Medicines.FirstOrDefaultAsync(m => m.Code == "PARA500");
        if (med != null && (!med.UnitPrice.HasValue || med.UnitPrice.Value <= 0))
        {
            med.UnitPrice = 2000m;
        }

        var diag = await db.DiagnosticServices.FirstOrDefaultAsync(d => d.Code == "LAB-TEST-01");
        if (diag != null && (!diag.Price.HasValue || diag.Price.Value <= 0))
        {
            diag.Price = 120000m;
        }
        await db.SaveChangesAsync();

        var dept = await db.Departments.Include(d => d.Facility).FirstOrDefaultAsync(d => d.IsActive);
        if (dept != null)
        {
            return (dept.Id, dept.FacilityId);
        }

        var facility = new Facility
        {
            Code = "FAC-TEST-MAIN",
            Name = "Bệnh viện Đa khoa Quốc tế Test",
            Address = "123 Đường Y Tế",
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.Facilities.Add(facility);
        await db.SaveChangesAsync();

        var department = new Department
        {
            FacilityId = facility.Id,
            Code = "KKB",
            Name = "Khoa Khám Bệnh Ngoại Trú",
            SpecialtyId = SpecialtyEntityId,
            IsActive = true
        };
        db.Departments.Add(department);
        await db.SaveChangesAsync();

        return (department.Id, facility.Id);
    }

    [Fact]
    public async Task WalkInPatient_EndToEnd_ConnectedJourney_Success()
    {
        var (deptId, facId) = await EnsureFacilityAndDepartmentAsync();

        // 1. Receptionist registers walk-in patient
        await AuthenticateAsync("rec@test.com");
        var walkInReq = new WalkInRegistrationRequest
        {
            FullName = "Nguyễn Văn Vãng Lai",
            PhoneNumber = "0901234888",
            DateOfBirth = new DateOnly(1988, 5, 20),
            Gender = Gender.Male,
            Address = "456 Đường Lê Lợi, Q.1, TP.HCM",
            IdentityCardNumber = "079088012345",
            FacilityId = facId,
            DepartmentId = deptId,
            AssignedDoctorId = DoctorEntityId,
            ChiefComplaint = "Đau đầu, chóng mặt và sốt nhẹ",
            Priority = VisitPriority.Normal
        };

        var walkInRes = await Client.PostAsJsonAsync("/api/v1/patient-visits/walk-in", walkInReq);
        Assert.Equal(HttpStatusCode.OK, walkInRes.StatusCode);
        var ticketDoc = JsonDocument.Parse(await walkInRes.Content.ReadAsStringAsync());
        var ticketData = ticketDoc.RootElement.GetProperty("data");

        var visitId = ticketData.GetProperty("visitId").GetInt64();
        var visitCode = ticketData.GetProperty("visitCode").GetString();
        var queueNumber = ticketData.GetProperty("queueNumber").GetInt32();
        var mrn = ticketData.GetProperty("medicalRecordNumber").GetString();

        Assert.True(visitId > 0);
        Assert.False(string.IsNullOrEmpty(visitCode));
        Assert.True(queueNumber > 0);
        Assert.False(string.IsNullOrEmpty(mrn));
        Assert.Equal("WaitingForDoctor", ticketData.GetProperty("status").GetString());

        // 2. Doctor logs in, views unified queue and clinical context
        await AuthenticateAsync("doc@test.com");
        var queueRes = await Client.GetAsync("/api/v1/doctor/queue");
        Assert.Equal(HttpStatusCode.OK, queueRes.StatusCode);
        var queueDoc = JsonDocument.Parse(await queueRes.Content.ReadAsStringAsync());
        var queueItems = queueDoc.RootElement.GetProperty("data");
        bool foundInQueue = false;
        foreach (var item in queueItems.EnumerateArray())
        {
            if (item.TryGetProperty("patientVisitId", out var pvid) && pvid.ValueKind == JsonValueKind.Number && pvid.GetInt64() == visitId)
            {
                foundInQueue = true;
                Assert.Equal(queueNumber, item.GetProperty("queueNumber").GetInt32());
                break;
            }
        }
        Assert.True(foundInQueue, "Walk-in patient must be visible in Doctor's unified queue");

        // Doctor gets clinical context
        var contextRes = await Client.GetAsync($"/api/v1/doctor/visits/{visitId}/patient-context");
        Assert.Equal(HttpStatusCode.OK, contextRes.StatusCode);

        // 3. Doctor starts consultation
        var startRes = await Client.PostAsync($"/api/v1/doctor/visits/{visitId}/start-consultation", null);
        Assert.Equal(HttpStatusCode.OK, startRes.StatusCode);

        // Verify status is InConsultation
        var visitDetailRes = await Client.GetAsync($"/api/v1/patient-visits/{visitId}");
        Assert.Equal(HttpStatusCode.OK, visitDetailRes.StatusCode);
        var visitDetail = JsonDocument.Parse(await visitDetailRes.Content.ReadAsStringAsync()).RootElement.GetProperty("data");
        Assert.Equal("InConsultation", visitDetail.GetProperty("status").GetString());

        // 4. Doctor records vitals and encounter note
        var vitalsReq = new SaveVitalSignsRequest
        {
            Temperature = 37.5m,
            BloodPressureSystolic = 120,
            BloodPressureDiastolic = 80,
            HeartRate = 78,
            RespiratoryRate = 18,
            Weight = 65,
            Height = 170
        };
        var vitalsRes = await Client.PutAsJsonAsync($"/api/v1/doctor/visits/{visitId}/vitals", vitalsReq);
        Assert.Equal(HttpStatusCode.OK, vitalsRes.StatusCode);

        var encounterReq = new SaveEncounterRequest
        {
            ChiefComplaint = "Bệnh nhân kêu đau đầu 2 ngày nay",
            ClinicalFindings = "Họng sạch, tim phổi bình thường",
            Diagnosis = "Sốt siêu vi theo dõi",
            TreatmentPlan = "Chỉ định công thức máu, kê đơn giảm đau hạ sốt",
            Summary = "Theo dõi sốt siêu vi"
        };
        var encounterRes = await Client.PutAsJsonAsync($"/api/v1/doctor/visits/{visitId}/encounter", encounterReq);
        Assert.Equal(HttpStatusCode.OK, encounterRes.StatusCode);

        // 5. Doctor creates diagnostic order
        var diagCatalogRes = await Client.GetAsync("/api/v1/diagnostic-services");
        Assert.Equal(HttpStatusCode.OK, diagCatalogRes.StatusCode);
        var diagCatalog = JsonDocument.Parse(await diagCatalogRes.Content.ReadAsStringAsync()).RootElement.GetProperty("data");
        Assert.True(diagCatalog.GetArrayLength() > 0);
        var diagServiceId = diagCatalog[0].GetProperty("id").GetInt64();

        var orderReq = new CreateDiagnosticOrderRequest
        {
            ClinicalIndication = "Nghi ngờ nhiễm trùng",
            Note = "Xét nghiệm máu thường quy",
            ServiceIds = new List<long> { diagServiceId }
        };
        var orderRes = await Client.PostAsJsonAsync($"/api/v1/doctor/visits/{visitId}/diagnostic-orders", orderReq);
        Assert.Equal(HttpStatusCode.Created, orderRes.StatusCode);
        var orderDoc = JsonDocument.Parse(await orderRes.Content.ReadAsStringAsync());
        var orderId = orderDoc.RootElement.GetProperty("data").GetProperty("id").GetInt64();
        var itemId = orderDoc.RootElement.GetProperty("data").GetProperty("items")[0].GetProperty("id").GetInt64();

        // 6. Doctor attempts to complete visit consultation BEFORE diagnostics finished -> Guard prevents it!
        var earlyCompleteReq = new CompleteConsultationRequest
        {
            Diagnosis = "Sốt siêu vi",
            TreatmentPlan = "Uống thuốc nghỉ ngơi"
        };
        var earlyCompleteRes = await Client.PostAsJsonAsync($"/api/v1/doctor/visits/{visitId}/complete", earlyCompleteReq);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, earlyCompleteRes.StatusCode);

        // 7. Diagnostic Technician processes the order
        await AuthenticateAsync("tech@test.com");
        var techStartRes = await Client.PostAsync($"/api/v1/diagnostics/orders/{orderId}/start", null);
        Assert.Equal(HttpStatusCode.OK, techStartRes.StatusCode);

        var techResultReq = new RecordDiagnosticResultRequest
        {
            ResultText = "WBC 7.5 (Bình thường)",
            Conclusion = "Kết quả xét nghiệm trong giới hạn bình thường.",
            ReferenceRange = "4.0 - 10.0 K/uL",
            Unit = "K/uL"
        };
        var techResultRes = await Client.PutAsJsonAsync($"/api/v1/diagnostics/orders/{orderId}/items/{itemId}/result", techResultReq);
        Assert.Equal(HttpStatusCode.OK, techResultRes.StatusCode);

        var techCompleteRes = await Client.PostAsync($"/api/v1/diagnostics/orders/{orderId}/complete", null);
        Assert.Equal(HttpStatusCode.OK, techCompleteRes.StatusCode);

        // Verify visit status auto-transitions to ResultsReady
        var visitAfterDiagRes = await Client.GetAsync($"/api/v1/patient-visits/{visitId}");
        var visitAfterDiag = JsonDocument.Parse(await visitAfterDiagRes.Content.ReadAsStringAsync()).RootElement.GetProperty("data");
        Assert.Equal("ResultsReady", visitAfterDiag.GetProperty("status").GetString());

        // 8. Doctor reviews diagnostic results
        await AuthenticateAsync("doc@test.com");
        var reviewRes = await Client.PostAsJsonAsync($"/api/v1/doctor/diagnostic-orders/{orderId}/review", new TransitionDiagnosticOrderRequest());
        Assert.Equal(HttpStatusCode.OK, reviewRes.StatusCode);

        // 9. Doctor saves prescription draft & completes consultation
        var rxDraftReq = new SavePrescriptionDraftRequest
        {
            Notes = "Uống đủ nước, uống thuốc đúng liều",
            Items = new List<SavePrescriptionItemRequest>
            {
                new()
                {
                    MedicineId = MedicineEntityId,
                    Quantity = 10,
                    Dosage = "500mg",
                    Frequency = "Ngày 2 lần sau ăn",
                    DurationDays = 5,
                    Instructions = "Uống khi sốt trên 38.5 độ"
                }
            }
        };
        var rxRes = await Client.PutAsJsonAsync($"/api/v1/doctor/visits/{visitId}/prescription-draft", rxDraftReq);
        Assert.Equal(HttpStatusCode.OK, rxRes.StatusCode);

        var completeRes = await Client.PostAsJsonAsync($"/api/v1/doctor/visits/{visitId}/complete", new CompleteConsultationRequest
        {
            Diagnosis = "Sốt virus cấp tính",
            TreatmentPlan = "Nghỉ ngơi, dùng thuốc theo đơn"
        });
        Assert.Equal(HttpStatusCode.OK, completeRes.StatusCode);

        // Verify visit transitioned to InPharmacy
        var visitAfterConsultRes = await Client.GetAsync($"/api/v1/patient-visits/{visitId}");
        var visitAfterConsult = JsonDocument.Parse(await visitAfterConsultRes.Content.ReadAsStringAsync()).RootElement.GetProperty("data");
        Assert.Equal("InPharmacy", visitAfterConsult.GetProperty("status").GetString());

        // 10. Pharmacy dispenses medication
        await AuthenticateAsync("pharm@test.com");
        var rxListRes = await Client.GetAsync("/api/v1/pharmacy/prescriptions?status=Pending");
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

        var dispenseRes = await Client.PostAsync($"/api/v1/pharmacy/prescriptions/{prescriptionId}/dispense", null);
        Assert.Equal(HttpStatusCode.OK, dispenseRes.StatusCode);

        // Verify visit status is InBilling
        var visitAfterDispenseRes = await Client.GetAsync($"/api/v1/patient-visits/{visitId}");
        var visitAfterDispense = JsonDocument.Parse(await visitAfterDispenseRes.Content.ReadAsStringAsync()).RootElement.GetProperty("data");
        Assert.Equal("InBilling", visitAfterDispense.GetProperty("status").GetString());

        // 11. Receptionist generates consolidated visit invoice and collects payment
        await AuthenticateAsync("rec@test.com");
        var invoiceRes = await Client.PostAsJsonAsync("/api/v1/reception/billing/invoices/visit", new CreateVisitInvoiceRequest
        {
            PatientVisitId = visitId
        });
        Assert.Equal(HttpStatusCode.Created, invoiceRes.StatusCode);
        var invoiceDoc = JsonDocument.Parse(await invoiceRes.Content.ReadAsStringAsync());
        var invoiceData = invoiceDoc.RootElement.GetProperty("data");
        var invoiceId = invoiceData.GetProperty("id").GetInt64();
        var totalAmount = invoiceData.GetProperty("totalAmount").GetDecimal();
        Assert.True(totalAmount > 0, "Consolidated visit invoice total must include consultation, diagnostic and pharmacy fees");

        // Pay invoice
        var payRes = await Client.PostAsJsonAsync($"/api/v1/reception/billing/invoices/{invoiceId}/pay", new ProcessPaymentRequest
        {
            Amount = totalAmount,
            Method = PaymentMethod.Cash,
            ReferenceCode = "CASH-VISIT-001"
        });
        Assert.Equal(HttpStatusCode.OK, payRes.StatusCode);

        // Final check: Visit status is Completed!
        var finalVisitRes = await Client.GetAsync($"/api/v1/patient-visits/{visitId}");
        var finalVisit = JsonDocument.Parse(await finalVisitRes.Content.ReadAsStringAsync()).RootElement.GetProperty("data");
        Assert.Equal("Completed", finalVisit.GetProperty("status").GetString());
    }

    [Fact]
    public async Task ScheduledAppointment_CheckIn_And_Idempotency_Journey()
    {
        var (deptId, facId) = await EnsureFacilityAndDepartmentAsync();

        // 1. Patient books appointment
        var date = GetFutureWorkingDate(44);
        var slot = await CreateAvailableSlotAsync(DoctorEntityId, date, new TimeOnly(9, 0, 0), new TimeOnly(9, 30, 0));

        await AuthenticateAsync("pat1@test.com");
        var bookRes = await Client.PostAsJsonAsync("/api/v1/appointments", new
        {
            DoctorId = DoctorEntityId,
            SpecialtyId = SpecialtyEntityId,
            AppointmentSlotId = slot.Id,
            Reason = "Tái khám định kỳ tăng huyết áp"
        });
        Assert.Equal(HttpStatusCode.Created, bookRes.StatusCode);
        var apptId = JsonDocument.Parse(await bookRes.Content.ReadAsStringAsync()).RootElement.GetProperty("data").GetProperty("id").GetInt64();

        // 2. Reception confirms appointment
        await AuthenticateAsync("rec@test.com");
        var confRes = await Client.PostAsync($"/api/v1/reception/appointments/{apptId}/confirm", null);
        Assert.Equal(HttpStatusCode.OK, confRes.StatusCode);

        // 3. Reception checks in appointment
        var checkInRes = await Client.PostAsync($"/api/v1/reception/appointments/{apptId}/check-in", null);
        Assert.Equal(HttpStatusCode.OK, checkInRes.StatusCode);
        var ticket1 = JsonDocument.Parse(await checkInRes.Content.ReadAsStringAsync()).RootElement.GetProperty("data");

        var visitId1 = ticket1.GetProperty("visitId").GetInt64();
        var queueNum1 = ticket1.GetProperty("queueNumber").GetInt32();
        var visitCode1 = ticket1.GetProperty("visitCode").GetString();

        Assert.True(visitId1 > 0);
        Assert.True(queueNum1 > 0);

        // 4. Idempotency check: Reception checks in AGAIN for the same appointment
        var checkInAgainRes = await Client.PostAsync($"/api/v1/reception/appointments/{apptId}/check-in", null);
        Assert.Equal(HttpStatusCode.OK, checkInAgainRes.StatusCode);
        var ticket2 = JsonDocument.Parse(await checkInAgainRes.Content.ReadAsStringAsync()).RootElement.GetProperty("data");

        Assert.Equal(visitId1, ticket2.GetProperty("visitId").GetInt64());
        Assert.Equal(queueNum1, ticket2.GetProperty("queueNumber").GetInt32());
        Assert.Equal(visitCode1, ticket2.GetProperty("visitCode").GetString());

        // 5. Doctor checks queue and sees the checked-in appointment
        await AuthenticateAsync("doc@test.com");
        var queueRes = await Client.GetAsync($"/api/v1/doctor/queue?date={date:yyyy-MM-dd}");
        Assert.Equal(HttpStatusCode.OK, queueRes.StatusCode);
        var queueDoc = JsonDocument.Parse(await queueRes.Content.ReadAsStringAsync());
        var queueItems = queueDoc.RootElement.GetProperty("data");
        bool found = false;
        foreach (var item in queueItems.EnumerateArray())
        {
            if (item.TryGetProperty("appointmentId", out var aid) && aid.ValueKind == JsonValueKind.Number && aid.GetInt64() == apptId)
            {
                found = true;
                Assert.Equal(visitId1, item.GetProperty("patientVisitId").GetInt64());
                break;
            }
        }
        Assert.True(found, "Checked-in scheduled appointment must appear in unified doctor queue with patientVisitId");
    }

    [Fact]
    public async Task PriorityQueue_Emergency_Patient_Appears_First_In_Department_Queue()
    {
        var (deptId, facId) = await EnsureFacilityAndDepartmentAsync();

        await AuthenticateAsync("rec@test.com");

        // Register normal priority walk-in
        var normalReq = new WalkInRegistrationRequest
        {
            FullName = "Bệnh nhân Thường",
            PhoneNumber = "0911222333",
            FacilityId = facId,
            DepartmentId = deptId,
            ChiefComplaint = "Cảm cúm thông thường",
            Priority = VisitPriority.Normal
        };
        var normalRes = await Client.PostAsJsonAsync("/api/v1/patient-visits/walk-in", normalReq);
        Assert.Equal(HttpStatusCode.OK, normalRes.StatusCode);
        var normalVisitId = JsonDocument.Parse(await normalRes.Content.ReadAsStringAsync()).RootElement.GetProperty("data").GetProperty("visitId").GetInt64();

        // Register emergency priority walk-in
        var emergencyReq = new WalkInRegistrationRequest
        {
            FullName = "Bệnh nhân Cấp Cứu",
            PhoneNumber = "0944555666",
            FacilityId = facId,
            DepartmentId = deptId,
            ChiefComplaint = "Khó thở dữ dội, đau thắt ngực",
            Priority = VisitPriority.Emergency
        };
        var emergencyRes = await Client.PostAsJsonAsync("/api/v1/patient-visits/walk-in", emergencyReq);
        Assert.Equal(HttpStatusCode.OK, emergencyRes.StatusCode);
        var emergencyVisitId = JsonDocument.Parse(await emergencyRes.Content.ReadAsStringAsync()).RootElement.GetProperty("data").GetProperty("visitId").GetInt64();

        // Department queue
        var deptQueueRes = await Client.GetAsync($"/api/v1/patient-visits/department-queue?departmentId={deptId}");
        Assert.Equal(HttpStatusCode.OK, deptQueueRes.StatusCode);
        var deptQueueDoc = JsonDocument.Parse(await deptQueueRes.Content.ReadAsStringAsync());
        var list = deptQueueDoc.RootElement.GetProperty("data");

        int emergencyIndex = -1;
        int normalIndex = -1;
        int idx = 0;
        foreach (var item in list.EnumerateArray())
        {
            var vid = item.GetProperty("visitId").GetInt64();
            if (vid == emergencyVisitId) emergencyIndex = idx;
            if (vid == normalVisitId) normalIndex = idx;
            idx++;
        }

        Assert.True(emergencyIndex >= 0, "Emergency visit must be in queue");
        Assert.True(normalIndex >= 0, "Normal visit must be in queue");
        Assert.True(emergencyIndex < normalIndex, "Emergency visit must be prioritized ahead of Normal visit in queue");
    }

    [Fact]
    public async Task WalkInPatient_ReturnsForSecondVisit_RetainsClinicalHistoryAndAnthropometricComparison()
    {
        var (deptId, facId) = await EnsureFacilityAndDepartmentAsync();

        // 1. Reception registers walk-in patient visit 1
        await AuthenticateAsync("rec@test.com");
        var walkInReq1 = new WalkInRegistrationRequest
        {
            FullName = "Đặng Thị Tái Khám",
            PhoneNumber = "0933444555",
            DateOfBirth = new DateOnly(1992, 8, 15),
            Gender = Gender.Female,
            Address = "789 Điện Biên Phủ, Q.3, TP.HCM",
            IdentityCardNumber = "079199999888",
            FacilityId = facId,
            DepartmentId = deptId,
            AssignedDoctorId = DoctorEntityId,
            ChiefComplaint = "Đau họng, sốt nhẹ ngày 1",
            Priority = VisitPriority.Normal
        };

        var res1 = await Client.PostAsJsonAsync("/api/v1/patient-visits/walk-in", walkInReq1);
        Assert.Equal(HttpStatusCode.OK, res1.StatusCode);
        var doc1 = JsonDocument.Parse(await res1.Content.ReadAsStringAsync()).RootElement.GetProperty("data");
        var visitId1 = doc1.GetProperty("visitId").GetInt64();
        var patientId = doc1.GetProperty("patientId").GetInt64();

        // 2. Doctor starts consultation 1, records vitals (65kg, 165cm), diagnoses and completes
        await AuthenticateAsync("doc@test.com");
        var startRes1 = await Client.PostAsync($"/api/v1/doctor/visits/{visitId1}/start-consultation", null);
        Assert.Equal(HttpStatusCode.OK, startRes1.StatusCode);

        var vitalsRes1 = await Client.PutAsJsonAsync($"/api/v1/doctor/visits/{visitId1}/vitals", new SaveVitalSignsRequest
        {
            Weight = 65.0m,
            Height = 165.0m,
            HeartRate = 78,
            BloodPressureSystolic = 120,
            BloodPressureDiastolic = 80,
            Temperature = 37.2m,
            SpO2 = 99
        });
        Assert.Equal(HttpStatusCode.OK, vitalsRes1.StatusCode);

        var compRes1 = await Client.PostAsJsonAsync($"/api/v1/doctor/visits/{visitId1}/complete", new CompleteConsultationRequest
        {
            Diagnosis = "Viêm họng cấp",
            Summary = "Hoàn thành khám lần 1, cho đơn thuốc uống 5 ngày"
        });
        Assert.Equal(HttpStatusCode.OK, compRes1.StatusCode);

        // 3. Same patient returns for visit 2 (Walk-in using existingPatientId)
        await AuthenticateAsync("rec@test.com");
        var walkInReq2 = new WalkInRegistrationRequest
        {
            ExistingPatientId = patientId,
            FacilityId = facId,
            DepartmentId = deptId,
            AssignedDoctorId = DoctorEntityId,
            ChiefComplaint = "Tái khám kiểm tra họng",
            Priority = VisitPriority.Normal
        };

        var res2 = await Client.PostAsJsonAsync("/api/v1/patient-visits/walk-in", walkInReq2);
        Assert.Equal(HttpStatusCode.OK, res2.StatusCode);
        var doc2 = JsonDocument.Parse(await res2.Content.ReadAsStringAsync()).RootElement.GetProperty("data");
        var visitId2 = doc2.GetProperty("visitId").GetInt64();

        // 4. Doctor starts consultation 2, records vitals (63.5kg, 165cm: lost 1.5kg)
        await AuthenticateAsync("doc@test.com");
        var startRes2 = await Client.PostAsync($"/api/v1/doctor/visits/{visitId2}/start-consultation", null);
        Assert.Equal(HttpStatusCode.OK, startRes2.StatusCode);

        var vitalsRes2 = await Client.PutAsJsonAsync($"/api/v1/doctor/visits/{visitId2}/vitals", new SaveVitalSignsRequest
        {
            Weight = 63.5m,
            Height = 165.0m,
            HeartRate = 74,
            BloodPressureSystolic = 115,
            BloodPressureDiastolic = 75,
            Temperature = 36.6m,
            SpO2 = 99
        });
        Assert.Equal(HttpStatusCode.OK, vitalsRes2.StatusCode);

        // 5. Doctor retrieves clinical context for visit 2
        var ctxRes = await Client.GetAsync($"/api/v1/doctor/visits/{visitId2}/clinical-context");
        Assert.Equal(HttpStatusCode.OK, ctxRes.StatusCode);

        var ctx = await ctxRes.Content.ReadFromJsonAsync<ApiResponse<PatientClinicalContextDto>>();
        Assert.NotNull(ctx);
        Assert.True(ctx.Success);
        var data = ctx.Data!;

        // Verify patient identity continuity
        Assert.Equal(patientId, data.PatientId);

        // Verify past visits continuity across walk-ins
        Assert.NotEmpty(data.PastVisits);
        var pastVisit = data.PastVisits.FirstOrDefault(p => p.PatientVisitId == visitId1);
        Assert.NotNull(pastVisit);
        Assert.Equal("Viêm họng cấp", pastVisit.Diagnosis);

        // Verify vitals history includes both measurements
        Assert.True(data.VitalHistory.Count >= 2);

        // Verify anthropometric comparison deltas calculated between visit 2 and visit 1
        Assert.NotNull(data.AnthropometricComparison);
        Assert.True(data.AnthropometricComparison.HasComparableData);
        Assert.Equal(63.5m, data.AnthropometricComparison.CurrentMeasurement?.Weight);
        Assert.Equal(65.0m, data.AnthropometricComparison.PreviousMeasurement?.Weight);
        Assert.Equal(-1.5m, data.AnthropometricComparison.WeightDeltaKg);
        Assert.Equal(0.0m, data.AnthropometricComparison.HeightDeltaCm);
    }
}
