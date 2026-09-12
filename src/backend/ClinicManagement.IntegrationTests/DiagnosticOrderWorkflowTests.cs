using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using ClinicManagement.Application.Appointments.DTOs.Doctor;
using ClinicManagement.Application.Diagnostics.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ClinicManagement.IntegrationTests;

public class DiagnosticOrderWorkflowTests : IntegrationTestBase
{
    public DiagnosticOrderWorkflowTests(CustomWebApplicationFactory factory) : base(factory) { }

    [Fact]
    public async Task Given_DoctorAndPatient_When_FullDiagnosticOrderWorkflowExecuted_Then_TransitionsCorrectly_And_BlocksConsultationUntilReviewed()
    {
        // 1. Setup Appointment in consultation
        var date = GetFutureWorkingDate(34);
        var slot = await CreateAvailableSlotAsync(DoctorEntityId, date, new TimeOnly(8, 0, 0), new TimeOnly(8, 30, 0));

        await AuthenticateAsync("pat1@test.com");
        var createAptRes = await Client.PostAsJsonAsync("/api/v1/appointments", new
        {
            DoctorId = DoctorEntityId,
            SpecialtyId = SpecialtyEntityId,
            AppointmentSlotId = slot.Id,
            Reason = "Đau bụng lâm sàng cần cận lâm sàng"
        });
        Assert.Equal(HttpStatusCode.Created, createAptRes.StatusCode);
        var aptDoc = JsonDocument.Parse(await createAptRes.Content.ReadAsStringAsync());
        var appointmentId = aptDoc.RootElement.GetProperty("data").GetProperty("id").GetInt64();

        // Reception confirms appointment
        await AuthenticateAsync("rec@test.com");
        var confRes = await Client.PostAsync($"/api/v1/reception/appointments/{appointmentId}/confirm", null);
        Assert.Equal(HttpStatusCode.OK, confRes.StatusCode);

        // Doctor check in & start consultation
        await AuthenticateAsync("doc@test.com");
        await Client.PostAsync($"/api/v1/doctor/appointments/{appointmentId}/check-in", null);
        await Client.PostAsync($"/api/v1/doctor/appointments/{appointmentId}/start-consultation", null);

        // 2. Retrieve diagnostic catalog to get valid service IDs
        var catRes = await Client.GetAsync("/api/v1/diagnostic-services");
        Assert.Equal(HttpStatusCode.OK, catRes.StatusCode);
        var catDoc = JsonDocument.Parse(await catRes.Content.ReadAsStringAsync());
        var servicesArray = catDoc.RootElement.GetProperty("data");
        Assert.True(servicesArray.GetArrayLength() >= 2);
        var s1Id = servicesArray[0].GetProperty("id").GetInt64();
        var s2Id = servicesArray[1].GetProperty("id").GetInt64();

        // 3. Doctor creates diagnostic order
        var orderReq = new CreateDiagnosticOrderRequest
        {
            ClinicalIndication = "Nghi ngờ viêm gan cấp",
            Note = "Thực hiện sớm trong sáng nay",
            ServiceIds = new List<long> { s1Id, s2Id }
        };
        var createOrderRes = await Client.PostAsJsonAsync($"/api/v1/doctor/appointments/{appointmentId}/diagnostic-orders", orderReq);
        Assert.Equal(HttpStatusCode.Created, createOrderRes.StatusCode);
        var orderDoc = JsonDocument.Parse(await createOrderRes.Content.ReadAsStringAsync());
        var orderData = orderDoc.RootElement.GetProperty("data");
        var orderId = orderData.GetProperty("id").GetInt64();
        var orderCode = orderData.GetProperty("orderCode").GetString();
        Assert.StartsWith("DX-", orderCode);
        Assert.Equal("Ordered", orderData.GetProperty("status").GetString());
        var items = orderData.GetProperty("items");
        Assert.Equal(2, items.GetArrayLength());
        var item1Id = items[0].GetProperty("id").GetInt64();
        var item2Id = items[1].GetProperty("id").GetInt64();

        // 4. Technician views queue and starts order
        await AuthenticateAsync("tech@test.com");
        var queueRes = await Client.GetAsync("/api/v1/diagnostics/orders");
        Assert.Equal(HttpStatusCode.OK, queueRes.StatusCode);
        var queueDoc = JsonDocument.Parse(await queueRes.Content.ReadAsStringAsync());
        var queueItems = queueDoc.RootElement.GetProperty("data").GetProperty("items");
        Assert.True(queueItems.GetArrayLength() > 0);

        // Technician starts order
        var startRes = await Client.PostAsync($"/api/v1/diagnostics/orders/{orderId}/start", null);
        Assert.Equal(HttpStatusCode.OK, startRes.StatusCode);
        var startDoc = JsonDocument.Parse(await startRes.Content.ReadAsStringAsync());
        Assert.Equal("InProgress", startDoc.RootElement.GetProperty("data").GetProperty("status").GetString());

        // 5. Guard check: Doctor attempts to complete consultation while order is InProgress -> MUST FAIL
        await AuthenticateAsync("doc@test.com");
        var earlyCompleteRes = await Client.PostAsJsonAsync($"/api/v1/doctor/appointments/{appointmentId}/complete", new CompleteConsultationRequest
        {
            Diagnosis = "Chưa có kết quả xét nghiệm",
            Summary = "Cố hoàn thành sớm"
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, earlyCompleteRes.StatusCode);
        var earlyCompleteJson = await earlyCompleteRes.Content.ReadAsStringAsync();
        Assert.Contains("PENDING_DIAGNOSTIC_RESULTS", earlyCompleteJson);

        // 6. Technician records results for both items
        await AuthenticateAsync("tech@test.com");
        var result1Res = await Client.PutAsJsonAsync($"/api/v1/diagnostics/orders/{orderId}/items/{item1Id}/result", new RecordDiagnosticResultRequest
        {
            ResultText = "Chỉ số men gan AST: 35 U/L, ALT: 40 U/L",
            Conclusion = "Men gan trong giới hạn bình thường",
            ReferenceRange = "AST < 40, ALT < 40",
            Unit = "U/L"
        });
        Assert.Equal(HttpStatusCode.OK, result1Res.StatusCode);

        var result2Res = await Client.PutAsJsonAsync($"/api/v1/diagnostics/orders/{orderId}/items/{item2Id}/result", new RecordDiagnosticResultRequest
        {
            ResultText = "Nhu mô gan đồng nhất, bờ đều, không có khối khu trú",
            Conclusion = "Hình ảnh siêu âm gan bình thường",
            ReferenceRange = "Bình thường"
        });
        Assert.Equal(HttpStatusCode.OK, result2Res.StatusCode);

        // Complete order
        var completeOrderRes = await Client.PostAsync($"/api/v1/diagnostics/orders/{orderId}/complete", null);
        Assert.Equal(HttpStatusCode.OK, completeOrderRes.StatusCode);
        var completeOrderDoc = JsonDocument.Parse(await completeOrderRes.Content.ReadAsStringAsync());
        Assert.Equal("Completed", completeOrderDoc.RootElement.GetProperty("data").GetProperty("status").GetString());

        // 7. Guard check: Doctor attempts to complete consultation while order is Completed but NOT Reviewed -> MUST FAIL
        await AuthenticateAsync("doc@test.com");
        var unreviewedCompleteRes = await Client.PostAsJsonAsync($"/api/v1/doctor/appointments/{appointmentId}/complete", new CompleteConsultationRequest
        {
            Diagnosis = "Đã có kết quả xét nghiệm nhưng chưa ký review",
            Summary = "Cố hoàn thành khi chưa review"
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, unreviewedCompleteRes.StatusCode);
        var unreviewedCompleteJson = await unreviewedCompleteRes.Content.ReadAsStringAsync();
        Assert.Contains("UNREVIEWED_DIAGNOSTIC_RESULTS", unreviewedCompleteJson);

        // 8. Doctor reviews the diagnostic order
        var reviewRes = await Client.PostAsJsonAsync($"/api/v1/doctor/diagnostic-orders/{orderId}/review", new TransitionDiagnosticOrderRequest());
        Assert.Equal(HttpStatusCode.OK, reviewRes.StatusCode);
        var reviewDoc = JsonDocument.Parse(await reviewRes.Content.ReadAsStringAsync());
        Assert.Equal("Completed", reviewDoc.RootElement.GetProperty("data").GetProperty("status").GetString());
        Assert.NotNull(reviewDoc.RootElement.GetProperty("data").GetProperty("reviewedAtUtc").GetString());

        // 9. Doctor now completes the appointment successfully
        var finalCompleteRes = await Client.PostAsJsonAsync($"/api/v1/doctor/appointments/{appointmentId}/complete", new CompleteConsultationRequest
        {
            ChiefComplaint = "Đau bụng nhẹ",
            ClinicalFindings = "Kết quả xét nghiệm và siêu âm bình thường",
            Diagnosis = "Rối loạn tiêu hóa cơ năng",
            TreatmentPlan = "Điều chỉnh chế độ ăn uống",
            Summary = "Khám và cận lâm sàng đầy đủ, sức khỏe ổn định"
        });
        Assert.Equal(HttpStatusCode.OK, finalCompleteRes.StatusCode);

        // Verify appointment status is Completed
        var aptDetailRes = await Client.GetAsync($"/api/v1/doctor/appointments/{appointmentId}");
        var aptDetailDoc = JsonDocument.Parse(await aptDetailRes.Content.ReadAsStringAsync());
        Assert.Equal("Completed", aptDetailDoc.RootElement.GetProperty("data").GetProperty("status").GetString());

        // 10. Patient views their diagnostic orders
        await AuthenticateAsync("pat1@test.com");
        var patientOrdersRes = await Client.GetAsync("/api/v1/patients/me/diagnostic-orders");
        Assert.Equal(HttpStatusCode.OK, patientOrdersRes.StatusCode);
        var patientOrdersDoc = JsonDocument.Parse(await patientOrdersRes.Content.ReadAsStringAsync());
        var patientOrders = patientOrdersDoc.RootElement.GetProperty("data").GetProperty("items");
        Assert.True(patientOrders.GetArrayLength() > 0);

        var patientDetailRes = await Client.GetAsync($"/api/v1/patients/me/diagnostic-orders/{orderId}");
        Assert.Equal(HttpStatusCode.OK, patientDetailRes.StatusCode);
        var patientDetailDoc = JsonDocument.Parse(await patientDetailRes.Content.ReadAsStringAsync());
        Assert.Equal(orderId, patientDetailDoc.RootElement.GetProperty("data").GetProperty("id").GetInt64());
        Assert.Equal("Completed", patientDetailDoc.RootElement.GetProperty("data").GetProperty("status").GetString());
        Assert.NotNull(patientDetailDoc.RootElement.GetProperty("data").GetProperty("reviewedAtUtc").GetString());
    }

    [Fact]
    public async Task Given_PendingOrder_When_DoctorCancels_Then_StatusBecomesCancelled()
    {
        var date = GetFutureWorkingDate(35);
        var slot = await CreateAvailableSlotAsync(DoctorEntityId, date, new TimeOnly(9, 0, 0), new TimeOnly(9, 30, 0));

        await AuthenticateAsync("pat1@test.com");
        var createAptRes = await Client.PostAsJsonAsync("/api/v1/appointments", new
        {
            DoctorId = DoctorEntityId,
            SpecialtyId = SpecialtyEntityId,
            AppointmentSlotId = slot.Id,
            Reason = "Khám hủy chỉ định"
        });
        var aptDoc = JsonDocument.Parse(await createAptRes.Content.ReadAsStringAsync());
        var appointmentId = aptDoc.RootElement.GetProperty("data").GetProperty("id").GetInt64();

        // Reception confirms appointment
        await AuthenticateAsync("rec@test.com");
        var confRes = await Client.PostAsync($"/api/v1/reception/appointments/{appointmentId}/confirm", null);
        Assert.Equal(HttpStatusCode.OK, confRes.StatusCode);

        // Doctor check in & start consultation
        await AuthenticateAsync("doc@test.com");
        await Client.PostAsync($"/api/v1/doctor/appointments/{appointmentId}/check-in", null);
        await Client.PostAsync($"/api/v1/doctor/appointments/{appointmentId}/start-consultation", null);

        var catRes = await Client.GetAsync("/api/v1/diagnostic-services");
        var catDoc = JsonDocument.Parse(await catRes.Content.ReadAsStringAsync());
        var sId = catDoc.RootElement.GetProperty("data")[0].GetProperty("id").GetInt64();

        var createOrderRes = await Client.PostAsJsonAsync($"/api/v1/doctor/appointments/{appointmentId}/diagnostic-orders", new CreateDiagnosticOrderRequest
        {
            ClinicalIndication = "Chỉ định nhầm",
            ServiceIds = new List<long> { sId }
        });
        Assert.Equal(HttpStatusCode.Created, createOrderRes.StatusCode);
        var orderDoc = JsonDocument.Parse(await createOrderRes.Content.ReadAsStringAsync());
        var orderId = orderDoc.RootElement.GetProperty("data").GetProperty("id").GetInt64();

        // Doctor cancels order
        var cancelRes = await Client.PostAsJsonAsync($"/api/v1/doctor/diagnostic-orders/{orderId}/cancel", new CancelDiagnosticOrderRequest
        {
            Reason = "Bệnh nhân không đồng ý thực hiện cận lâm sàng"
        });
        Assert.Equal(HttpStatusCode.OK, cancelRes.StatusCode);
        var cancelDoc = JsonDocument.Parse(await cancelRes.Content.ReadAsStringAsync());
        Assert.Equal("Cancelled", cancelDoc.RootElement.GetProperty("data").GetProperty("status").GetString());

        // Cancelled order should NOT block completion
        var completeRes = await Client.PostAsJsonAsync($"/api/v1/doctor/appointments/{appointmentId}/complete", new CompleteConsultationRequest
        {
            Diagnosis = "Hoàn tất khám sau hủy CLS",
            Summary = "Đã giải thích bệnh nhân"
        });
        Assert.Equal(HttpStatusCode.OK, completeRes.StatusCode);
    }

    [Fact]
    public async Task Given_DiagnosticOrder_When_SerializedToJson_Then_ContractMatchesFrontendExpectations()
    {
        // Setup consultation
        var date = GetFutureWorkingDate(36);
        var slot = await CreateAvailableSlotAsync(DoctorEntityId, date, new TimeOnly(14, 0, 0), new TimeOnly(14, 30, 0));

        await AuthenticateAsync("pat1@test.com");
        var createAptRes = await Client.PostAsJsonAsync("/api/v1/appointments", new
        {
            DoctorId = DoctorEntityId,
            SpecialtyId = SpecialtyEntityId,
            AppointmentSlotId = slot.Id,
            Reason = "Kiểm tra hợp đồng DTO cận lâm sàng"
        });
        Assert.Equal(HttpStatusCode.Created, createAptRes.StatusCode);
        var aptDoc = JsonDocument.Parse(await createAptRes.Content.ReadAsStringAsync());
        var appointmentId = aptDoc.RootElement.GetProperty("data").GetProperty("id").GetInt64();

        await AuthenticateAsync("rec@test.com");
        await Client.PostAsync($"/api/v1/reception/appointments/{appointmentId}/confirm", null);

        await AuthenticateAsync("doc@test.com");
        await Client.PostAsync($"/api/v1/doctor/appointments/{appointmentId}/check-in", null);
        await Client.PostAsync($"/api/v1/doctor/appointments/{appointmentId}/start-consultation", null);

        // 1. Verify Catalog contract
        var catRes = await Client.GetAsync("/api/v1/diagnostic-services");
        Assert.Equal(HttpStatusCode.OK, catRes.StatusCode);
        var catJson = await catRes.Content.ReadAsStringAsync();
        Assert.Contains("\"category\":", catJson);
        Assert.Contains("\"preparationInstructions\":", catJson);
        Assert.DoesNotContain("\"defaultPrice\":", catJson);

        var catDoc = JsonDocument.Parse(catJson);
        var serviceId = catDoc.RootElement.GetProperty("data")[0].GetProperty("id").GetInt64();

        // 2. Doctor creates order
        var createOrderRes = await Client.PostAsJsonAsync($"/api/v1/doctor/appointments/{appointmentId}/diagnostic-orders", new CreateDiagnosticOrderRequest
        {
            ClinicalIndication = "Kiểm tra contract canonical",
            ServiceIds = new List<long> { serviceId }
        });
        Assert.Equal(HttpStatusCode.Created, createOrderRes.StatusCode);
        var orderJson = await createOrderRes.Content.ReadAsStringAsync();

        // Must contain canonical names, must NOT contain old mismatched names
        Assert.Contains("\"specialtyName\":", orderJson);
        Assert.Contains("\"category\":", orderJson);
        Assert.DoesNotContain("\"orderingDoctorSpecialty\":", orderJson);
        Assert.DoesNotContain("\"serviceCategory\":", orderJson);

        var orderDoc = JsonDocument.Parse(orderJson);
        var orderData = orderDoc.RootElement.GetProperty("data");
        var specialtyName = orderData.GetProperty("specialtyName").GetString();
        Assert.False(string.IsNullOrWhiteSpace(specialtyName));

        var item = orderData.GetProperty("items")[0];
        var category = item.GetProperty("category").GetString();
        Assert.False(string.IsNullOrWhiteSpace(category));
    }

    [Fact]
    public async Task Given_CrossTenantUsers_When_AccessingDiagnosticOrder_Then_IsolationIsEnforcedWith404OrExclusion()
    {
        var date = GetFutureWorkingDate(37);
        var slot = await CreateAvailableSlotAsync(DoctorEntityId, date, new TimeOnly(15, 0, 0), new TimeOnly(15, 30, 0));

        await AuthenticateAsync("pat1@test.com");
        var createAptRes = await Client.PostAsJsonAsync("/api/v1/appointments", new
        {
            DoctorId = DoctorEntityId,
            SpecialtyId = SpecialtyEntityId,
            AppointmentSlotId = slot.Id,
            Reason = "Khám cô lập bảo mật đa người dùng"
        });
        var aptDoc = JsonDocument.Parse(await createAptRes.Content.ReadAsStringAsync());
        var appointmentId = aptDoc.RootElement.GetProperty("data").GetProperty("id").GetInt64();

        await AuthenticateAsync("rec@test.com");
        await Client.PostAsync($"/api/v1/reception/appointments/{appointmentId}/confirm", null);

        await AuthenticateAsync("doc@test.com");
        await Client.PostAsync($"/api/v1/doctor/appointments/{appointmentId}/check-in", null);
        await Client.PostAsync($"/api/v1/doctor/appointments/{appointmentId}/start-consultation", null);

        var catRes = await Client.GetAsync("/api/v1/diagnostic-services");
        var catDoc = JsonDocument.Parse(await catRes.Content.ReadAsStringAsync());
        var serviceId = catDoc.RootElement.GetProperty("data")[0].GetProperty("id").GetInt64();

        var createOrderRes = await Client.PostAsJsonAsync($"/api/v1/doctor/appointments/{appointmentId}/diagnostic-orders", new CreateDiagnosticOrderRequest
        {
            ClinicalIndication = "Chỉ định của bác sĩ 1 cho bệnh nhân 1",
            ServiceIds = new List<long> { serviceId }
        });
        Assert.Equal(HttpStatusCode.Created, createOrderRes.StatusCode);
        var orderDoc = JsonDocument.Parse(await createOrderRes.Content.ReadAsStringAsync());
        var orderId = orderDoc.RootElement.GetProperty("data").GetProperty("id").GetInt64();

        // Doctor B (doc2@test.com) CANNOT access Doctor A's order
        await AuthenticateAsync("doc2@test.com");
        var doc2GetRes = await Client.GetAsync($"/api/v1/doctor/diagnostic-orders/{orderId}");
        Assert.Equal(HttpStatusCode.NotFound, doc2GetRes.StatusCode);

        var doc2CancelRes = await Client.PostAsJsonAsync($"/api/v1/doctor/diagnostic-orders/{orderId}/cancel", new CancelDiagnosticOrderRequest
        {
            Reason = "Bác sĩ khác cố hủy"
        });
        Assert.Equal(HttpStatusCode.NotFound, doc2CancelRes.StatusCode);

        var doc2ReviewRes = await Client.PostAsJsonAsync($"/api/v1/doctor/diagnostic-orders/{orderId}/review", new TransitionDiagnosticOrderRequest());
        Assert.Equal(HttpStatusCode.NotFound, doc2ReviewRes.StatusCode);

        // Patient B (pat2@test.com) CANNOT access Patient A's order
        await AuthenticateAsync("pat2@test.com");
        var pat2GetRes = await Client.GetAsync($"/api/v1/patients/me/diagnostic-orders/{orderId}");
        Assert.Equal(HttpStatusCode.NotFound, pat2GetRes.StatusCode);

        var pat2ListRes = await Client.GetAsync("/api/v1/patients/me/diagnostic-orders");
        Assert.Equal(HttpStatusCode.OK, pat2ListRes.StatusCode);
        var pat2ListDoc = JsonDocument.Parse(await pat2ListRes.Content.ReadAsStringAsync());
        var pat2Items = pat2ListDoc.RootElement.GetProperty("data").GetProperty("items");
        for (int i = 0; i < pat2Items.GetArrayLength(); i++)
        {
            Assert.NotEqual(orderId, pat2Items[i].GetProperty("id").GetInt64());
        }
    }

    [Fact]
    public async Task Given_RoleSecurity_When_UnauthorizedRolesAccessEndpoints_Then_ReturnsForbiddenOrUnauthorized()
    {
        // 1. Unauthenticated request to technician queue -> 401
        Client.DefaultRequestHeaders.Authorization = null;
        var unauthRes = await Client.GetAsync("/api/v1/diagnostics/orders");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthRes.StatusCode);

        // 2. Patient attempting to access technician dashboard -> 403
        await AuthenticateAsync("pat1@test.com");
        var patAccessTechRes = await Client.GetAsync("/api/v1/diagnostics/orders");
        Assert.Equal(HttpStatusCode.Forbidden, patAccessTechRes.StatusCode);

        // 3. Receptionist attempting to access technician dashboard -> 403
        await AuthenticateAsync("rec@test.com");
        var recAccessTechRes = await Client.GetAsync("/api/v1/diagnostics/orders");
        Assert.Equal(HttpStatusCode.Forbidden, recAccessTechRes.StatusCode);

        // 4. Pharmacist attempting to access technician dashboard -> 403
        await AuthenticateAsync("pharm@test.com");
        var pharmAccessTechRes = await Client.GetAsync("/api/v1/diagnostics/orders");
        Assert.Equal(HttpStatusCode.Forbidden, pharmAccessTechRes.StatusCode);

        // 5. Technician attempting to access doctor order creation -> 403
        await AuthenticateAsync("tech@test.com");
        var techAccessDocRes = await Client.PostAsJsonAsync("/api/v1/doctor/appointments/1/diagnostic-orders", new CreateDiagnosticOrderRequest
        {
            ClinicalIndication = "Kỹ thuật viên cố tạo chỉ định",
            ServiceIds = new List<long> { 1 }
        });
        Assert.Equal(HttpStatusCode.Forbidden, techAccessDocRes.StatusCode);
    }

    [Fact]
    public async Task Given_InvalidService_When_CreatingOrder_Then_ValidationRejects_Before_Transaction()
    {
        // This test verifies INPUT VALIDATION, not DB transaction atomicity.
        // The INVALID_SERVICE exception is thrown BEFORE BeginTransactionAsync because the
        // service-existence check runs eagerly. No transaction is ever started in this path.
        var date = GetFutureWorkingDate(38);
        var slot = await CreateAvailableSlotAsync(DoctorEntityId, date, new TimeOnly(16, 0, 0), new TimeOnly(16, 30, 0));

        await AuthenticateAsync("pat1@test.com");
        var createAptRes = await Client.PostAsJsonAsync("/api/v1/appointments", new
        {
            DoctorId = DoctorEntityId,
            SpecialtyId = SpecialtyEntityId,
            AppointmentSlotId = slot.Id,
            Reason = "Khám kiểm tra validation dịch vụ không hợp lệ"
        });
        var aptDoc = System.Text.Json.JsonDocument.Parse(await createAptRes.Content.ReadAsStringAsync());
        var appointmentId = aptDoc.RootElement.GetProperty("data").GetProperty("id").GetInt64();

        await AuthenticateAsync("rec@test.com");
        await Client.PostAsync($"/api/v1/reception/appointments/{appointmentId}/confirm", null);

        await AuthenticateAsync("doc@test.com");
        await Client.PostAsync($"/api/v1/doctor/appointments/{appointmentId}/check-in", null);
        await Client.PostAsync($"/api/v1/doctor/appointments/{appointmentId}/start-consultation", null);

        // Request with a non-existent service ID — rejected at validation layer, before any DB write
        var invalidOrderRes = await Client.PostAsJsonAsync($"/api/v1/doctor/appointments/{appointmentId}/diagnostic-orders", new CreateDiagnosticOrderRequest
        {
            ClinicalIndication = "Chỉ định dịch vụ không tồn tại",
            ServiceIds = new List<long> { 99999999 }
        });
        Assert.False(invalidOrderRes.IsSuccessStatusCode);
        var errorBody = await invalidOrderRes.Content.ReadAsStringAsync();
        Assert.Contains("INVALID_SERVICE", errorBody);

        // No diagnostic order written to DB for this appointment
        var getOrdersRes = await Client.GetAsync($"/api/v1/doctor/appointments/{appointmentId}/diagnostic-orders");
        Assert.Equal(HttpStatusCode.OK, getOrdersRes.StatusCode);
        var ordersDoc = System.Text.Json.JsonDocument.Parse(await getOrdersRes.Content.ReadAsStringAsync());
        var orders = ordersDoc.RootElement.GetProperty("data");
        Assert.Equal(0, orders.GetArrayLength());
    }

    [Fact]
    public async Task Given_TransactionRollback_When_AuditLogSaveFailsAfterOrderInserted_Then_NoDiagnosticDataPersisted()
    {
        // This is the TRUE atomicity test.
        // We use a SaveChangesInterceptor (test-only, registered via AtomicityTestWebApplicationFactory) that
        // throws after the first SaveChanges executed/flushed inside the still-uncommitted transaction
        // (DiagnosticOrders and DiagnosticOrderItems written), but before the second SaveChangesAsync
        // (audit log + notifications) commits.
        // We record baseline counts before the order creation request and verify that after failure and rollback,
        // all entity counts exactly match baseline (no partial DiagnosticOrder, DiagnosticOrderItem,
        // SystemAuditLog, or Notification leaked).

        // Setup: use a dedicated factory that injects the failure interceptor
        await using var atomicFactory = new AtomicityTestWebApplicationFactory();
        var atomicClient = atomicFactory.CreateClient();

        // Authenticate into the atomic factory's own isolated DB
        async Task AuthAtomic(string email)
        {
            var loginRes = await atomicClient.PostAsJsonAsync("/api/v1/auth/login",
                new { emailOrPhone = email, password = "Pass@123" });
            var doc = System.Text.Json.JsonDocument.Parse(await loginRes.Content.ReadAsStringAsync());
            var token = doc.RootElement.GetProperty("data").GetProperty("accessToken").GetString()!;
            atomicClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }

        // Seed minimal data directly into the atomic factory DB
        long atomicDoctorId, atomicSpecialtyId, atomicService1Id;
        using (var scope = atomicFactory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ClinicManagement.Infrastructure.Persistence.AppDbContext>();
            db.Database.EnsureCreated();
            await SeedAtomicTestDataAsync(db, scope.ServiceProvider, atomicFactory);
            atomicDoctorId = db.Doctors.First().Id;
            atomicSpecialtyId = db.Specialties.First().Id;
            atomicService1Id = db.DiagnosticServices.First().Id;
        }

        // Create a slot, book, confirm, check-in and start consultation
        var date = GetFutureWorkingDate(39);
        AppointmentSlot atomicSlot;
        using (var scope = atomicFactory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ClinicManagement.Infrastructure.Persistence.AppDbContext>();
            var schedule = new ClinicManagement.Domain.Entities.DoctorWorkSchedule
            {
                DoctorId = atomicDoctorId,
                WorkDate = date,
                StartTime = new TimeOnly(10, 0, 0),
                EndTime = new TimeOnly(20, 0, 0),
                IsActive = true
            };
            db.DoctorWorkSchedules.Add(schedule);
            await db.SaveChangesAsync();
            atomicSlot = new ClinicManagement.Domain.Entities.AppointmentSlot
            {
                DoctorId = atomicDoctorId,
                SlotDate = date,
                StartTime = new TimeOnly(10, 0, 0),
                EndTime = new TimeOnly(10, 30, 0),
                IsBooked = false
            };
            db.AppointmentSlots.Add(atomicSlot);
            await db.SaveChangesAsync();
        }

        // 1. Assert setup requests: booking, confirm, check-in, start consultation
        await AuthAtomic("pat1@test.com");
        var aptRes = await atomicClient.PostAsJsonAsync("/api/v1/appointments", new
        {
            DoctorId = atomicDoctorId,
            SpecialtyId = atomicSpecialtyId,
            AppointmentSlotId = atomicSlot.Id,
            Reason = "Kiểm thử tính nguyên tử giao dịch"
        });
        Assert.Equal(HttpStatusCode.Created, aptRes.StatusCode);
        var aptDoc = System.Text.Json.JsonDocument.Parse(await aptRes.Content.ReadAsStringAsync());
        var appointmentId = aptDoc.RootElement.GetProperty("data").GetProperty("id").GetInt64();

        await AuthAtomic("rec@test.com");
        var confRes = await atomicClient.PostAsync($"/api/v1/reception/appointments/{appointmentId}/confirm", null);
        Assert.Equal(HttpStatusCode.OK, confRes.StatusCode);

        await AuthAtomic("doc@test.com");
        var checkinRes = await atomicClient.PostAsync($"/api/v1/doctor/appointments/{appointmentId}/check-in", null);
        Assert.Equal(HttpStatusCode.OK, checkinRes.StatusCode);
        var startRes = await atomicClient.PostAsync($"/api/v1/doctor/appointments/{appointmentId}/start-consultation", null);
        Assert.Equal(HttpStatusCode.OK, startRes.StatusCode);

        // 2. Record baseline entity counts before attempting order creation
        int baselineOrders, baselineItems, baselineAudits, baselineNotifs;
        using (var scope = atomicFactory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ClinicManagement.Infrastructure.Persistence.AppDbContext>();
            baselineOrders = await db.DiagnosticOrders.CountAsync();
            baselineItems = await db.DiagnosticOrderItems.CountAsync();
            baselineAudits = await db.SystemAuditLogs.CountAsync();
            baselineNotifs = await db.Notifications.CountAsync();
        }

        // 3. Reset and arm interceptor to fail on save call #2
        atomicFactory.Interceptor.Reset();
        atomicFactory.Interceptor.FailOnSaveNumber = 2;

        var createOrderRes = await atomicClient.PostAsJsonAsync(
            $"/api/v1/doctor/appointments/{appointmentId}/diagnostic-orders",
            new CreateDiagnosticOrderRequest
            {
                ClinicalIndication = "Kiểm thử rollback nguyên tử",
                ServiceIds = new List<long> { atomicService1Id }
            });

        // 4. Request must fail
        Assert.False(createOrderRes.IsSuccessStatusCode,
            $"Expected failure but got HTTP {(int)createOrderRes.StatusCode}");

        // 5. Assert interceptor was indeed triggered on save call 2
        Assert.True(atomicFactory.Interceptor.WasTriggered, "Interceptor must have been triggered on save call 2.");
        Assert.Equal(2, atomicFactory.Interceptor.SaveCallCount);

        // 6. Verify complete rollback: all counts match baseline exactly
        using (var scope = atomicFactory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ClinicManagement.Infrastructure.Persistence.AppDbContext>();

            var currentOrders = await db.DiagnosticOrders.CountAsync();
            var currentItems = await db.DiagnosticOrderItems.CountAsync();
            var currentAudits = await db.SystemAuditLogs.CountAsync();
            var currentNotifs = await db.Notifications.CountAsync();

            Assert.Equal(baselineOrders, currentOrders);
            Assert.Equal(baselineItems, currentItems);
            Assert.Equal(baselineAudits, currentAudits);
            Assert.Equal(baselineNotifs, currentNotifs);

            var appointmentOrderCount = await db.DiagnosticOrders
                .CountAsync(o => o.AppointmentId == appointmentId);
            Assert.Equal(0, appointmentOrderCount);
        }
    }

    [Fact]
    public async Task Given_NonOrderCodeDbUpdateException_When_CreatingOrder_Then_RethrownAndNotMappedToCollision()
    {
        // This test proves that DbUpdateExceptions NOT caused by DiagnosticOrders.OrderCode unique constraint
        // are NOT swallowed or converted to HTTP 409 Conflict (ORDER_CODE_COLLISION).
        // Instead, they are rethrown, resulting in HTTP 500 (INTERNAL_SERVER_ERROR),
        // and the transaction is atomically rolled back with zero dangling state.

        await using var atomicFactory = new AtomicityTestWebApplicationFactory();
        var atomicClient = atomicFactory.CreateClient();

        async Task AuthAtomic(string email)
        {
            var loginRes = await atomicClient.PostAsJsonAsync("/api/v1/auth/login",
                new { emailOrPhone = email, password = "Pass@123" });
            var doc = System.Text.Json.JsonDocument.Parse(await loginRes.Content.ReadAsStringAsync());
            var token = doc.RootElement.GetProperty("data").GetProperty("accessToken").GetString()!;
            atomicClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }

        long atomicDoctorId, atomicSpecialtyId, atomicService1Id;
        using (var scope = atomicFactory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ClinicManagement.Infrastructure.Persistence.AppDbContext>();
            db.Database.EnsureCreated();
            await SeedAtomicTestDataAsync(db, scope.ServiceProvider, atomicFactory);
            atomicDoctorId = db.Doctors.First().Id;
            atomicSpecialtyId = db.Specialties.First().Id;
            atomicService1Id = db.DiagnosticServices.First().Id;
        }

        var date = GetFutureWorkingDate(40);
        AppointmentSlot atomicSlot;
        using (var scope = atomicFactory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ClinicManagement.Infrastructure.Persistence.AppDbContext>();
            var schedule = new ClinicManagement.Domain.Entities.DoctorWorkSchedule
            {
                DoctorId = atomicDoctorId,
                WorkDate = date,
                StartTime = new TimeOnly(11, 0, 0),
                EndTime = new TimeOnly(20, 0, 0),
                IsActive = true
            };
            db.DoctorWorkSchedules.Add(schedule);
            await db.SaveChangesAsync();
            atomicSlot = new ClinicManagement.Domain.Entities.AppointmentSlot
            {
                DoctorId = atomicDoctorId,
                SlotDate = date,
                StartTime = new TimeOnly(11, 0, 0),
                EndTime = new TimeOnly(11, 30, 0),
                IsBooked = false
            };
            db.AppointmentSlots.Add(atomicSlot);
            await db.SaveChangesAsync();
        }

        // 1. Setup appointment to InConsultation
        await AuthAtomic("pat1@test.com");
        var aptRes = await atomicClient.PostAsJsonAsync("/api/v1/appointments", new
        {
            DoctorId = atomicDoctorId,
            SpecialtyId = atomicSpecialtyId,
            AppointmentSlotId = atomicSlot.Id,
            Reason = "Kiểm thử DbUpdateException không phải collision"
        });
        Assert.Equal(HttpStatusCode.Created, aptRes.StatusCode);
        var aptDoc = System.Text.Json.JsonDocument.Parse(await aptRes.Content.ReadAsStringAsync());
        var appointmentId = aptDoc.RootElement.GetProperty("data").GetProperty("id").GetInt64();

        await AuthAtomic("rec@test.com");
        var confRes = await atomicClient.PostAsync($"/api/v1/reception/appointments/{appointmentId}/confirm", null);
        Assert.Equal(HttpStatusCode.OK, confRes.StatusCode);

        await AuthAtomic("doc@test.com");
        var checkinRes = await atomicClient.PostAsync($"/api/v1/doctor/appointments/{appointmentId}/check-in", null);
        Assert.Equal(HttpStatusCode.OK, checkinRes.StatusCode);
        var startRes = await atomicClient.PostAsync($"/api/v1/doctor/appointments/{appointmentId}/start-consultation", null);
        Assert.Equal(HttpStatusCode.OK, startRes.StatusCode);

        // 2. Record baseline entity counts
        int baselineOrders, baselineItems, baselineAudits, baselineNotifs;
        using (var scope = atomicFactory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ClinicManagement.Infrastructure.Persistence.AppDbContext>();
            baselineOrders = await db.DiagnosticOrders.CountAsync();
            baselineItems = await db.DiagnosticOrderItems.CountAsync();
            baselineAudits = await db.SystemAuditLogs.CountAsync();
            baselineNotifs = await db.Notifications.CountAsync();
        }

        // 3. Arm interceptor to throw a DbUpdateException unrelated to OrderCode
        atomicFactory.Interceptor.Reset();
        atomicFactory.Interceptor.FailOnSaveNumber = 1;
        atomicFactory.Interceptor.ExceptionFactory = () => new DbUpdateException(
            "An error occurred while updating entries. Unique constraint on DiagnosticServices.Code.",
            new Exception("UNIQUE constraint failed: DiagnosticServices.Code"));

        var createOrderRes = await atomicClient.PostAsJsonAsync(
            $"/api/v1/doctor/appointments/{appointmentId}/diagnostic-orders",
            new CreateDiagnosticOrderRequest
            {
                ClinicalIndication = "Kiểm thử non-OrderCode DbUpdateException",
                ServiceIds = new List<long> { atomicService1Id }
            });

        // 4. Interceptor was triggered on save #1
        Assert.True(atomicFactory.Interceptor.WasTriggered, "Interceptor must have been triggered.");
        Assert.Equal(1, atomicFactory.Interceptor.SaveCallCount);

        // 5. Must NOT be HTTP 409 Conflict, but HTTP 500
        Assert.NotEqual(HttpStatusCode.Conflict, createOrderRes.StatusCode);
        Assert.Equal(HttpStatusCode.InternalServerError, createOrderRes.StatusCode);

        // 6. Response body must NOT contain ORDER_CODE_COLLISION
        var responseBody = await createOrderRes.Content.ReadAsStringAsync();
        Assert.DoesNotContain("ORDER_CODE_COLLISION", responseBody);

        // 7. Verify rollback atomicity: counts match baseline exactly
        using (var scope = atomicFactory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ClinicManagement.Infrastructure.Persistence.AppDbContext>();

            var currentOrders = await db.DiagnosticOrders.CountAsync();
            var currentItems = await db.DiagnosticOrderItems.CountAsync();
            var currentAudits = await db.SystemAuditLogs.CountAsync();
            var currentNotifs = await db.Notifications.CountAsync();

            Assert.Equal(baselineOrders, currentOrders);
            Assert.Equal(baselineItems, currentItems);
            Assert.Equal(baselineAudits, currentAudits);
            Assert.Equal(baselineNotifs, currentNotifs);

            var appointmentOrderCount = await db.DiagnosticOrders
                .CountAsync(o => o.AppointmentId == appointmentId);
            Assert.Equal(0, appointmentOrderCount);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Infrastructure helpers for the atomicity test
    // ──────────────────────────────────────────────────────────────────────────

    private static async Task SeedAtomicTestDataAsync(
        ClinicManagement.Infrastructure.Persistence.AppDbContext db,
        IServiceProvider sp,
        AtomicityTestWebApplicationFactory factory)
    {
        if (await db.Users.AnyAsync()) return;

        var userManager = sp.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<ClinicManagement.Infrastructure.Identity.ApplicationUser>>();
        var roleManager = sp.GetRequiredService<Microsoft.AspNetCore.Identity.RoleManager<Microsoft.AspNetCore.Identity.IdentityRole<Guid>>>();

        string[] roles = { "Admin", "Doctor", "Patient", "Receptionist", "DiagnosticTechnician" };
        foreach (var role in roles)
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new Microsoft.AspNetCore.Identity.IdentityRole<Guid>(role));

        var docId = Guid.NewGuid();
        var recId = Guid.NewGuid();
        var pat1Id = Guid.NewGuid();

        var doc = new ClinicManagement.Infrastructure.Identity.ApplicationUser { Id = docId, UserName = "doc@test.com", Email = "doc@test.com", FullName = "Doctor 1", PhoneNumber = "0123456782", IsActive = true };
        await userManager.CreateAsync(doc, "Pass@123");
        await userManager.AddToRoleAsync(doc, "Doctor");

        var rec = new ClinicManagement.Infrastructure.Identity.ApplicationUser { Id = recId, UserName = "rec@test.com", Email = "rec@test.com", FullName = "Receptionist", PhoneNumber = "0123456783", IsActive = true };
        await userManager.CreateAsync(rec, "Pass@123");
        await userManager.AddToRoleAsync(rec, "Receptionist");

        var pat1 = new ClinicManagement.Infrastructure.Identity.ApplicationUser { Id = pat1Id, UserName = "pat1@test.com", Email = "pat1@test.com", FullName = "Patient 1", PhoneNumber = "0123456784", IsActive = true };
        await userManager.CreateAsync(pat1, "Pass@123");
        await userManager.AddToRoleAsync(pat1, "Patient");

        var doctor = new ClinicManagement.Domain.Entities.Doctor { UserId = docId, IsActive = true, AcademicTitle = "BS", ExperienceYears = 5 };
        db.Doctors.Add(doctor);
        var patient = new ClinicManagement.Domain.Entities.Patient { UserId = pat1Id, DateOfBirth = new DateOnly(1990, 1, 1), Gender = ClinicManagement.Domain.Enums.Gender.Male };
        db.Patients.Add(patient);
        var spec = new ClinicManagement.Domain.Entities.Specialty { SpecialtyCode = "SP-AT", Name = "Chuyên Khoa Test Nguyên Tử", IsActive = true };
        db.Specialties.Add(spec);
        var svc = new ClinicManagement.Domain.Entities.DiagnosticService { Code = "AT-01", Name = "Dịch vụ test nguyên tử", Category = ClinicManagement.Domain.Enums.DiagnosticCategory.Laboratory, IsActive = true };
        db.DiagnosticServices.Add(svc);
        await db.SaveChangesAsync();
        db.DoctorSpecialties.Add(new ClinicManagement.Domain.Entities.DoctorSpecialty { DoctorId = doctor.Id, SpecialtyId = spec.Id, IsPrimary = true });
        await db.SaveChangesAsync();
    }
}

