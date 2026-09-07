using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using ClinicManagement.Application.Appointments.DTOs.Doctor;
using ClinicManagement.Application.Diagnostics.DTOs;
using ClinicManagement.Domain.Enums;
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
    public async Task Given_InvalidService_When_CreatingOrder_Then_TransactionRollsBackAtomically()
    {
        var date = GetFutureWorkingDate(38);
        var slot = await CreateAvailableSlotAsync(DoctorEntityId, date, new TimeOnly(16, 0, 0), new TimeOnly(16, 30, 0));

        await AuthenticateAsync("pat1@test.com");
        var createAptRes = await Client.PostAsJsonAsync("/api/v1/appointments", new
        {
            DoctorId = DoctorEntityId,
            SpecialtyId = SpecialtyEntityId,
            AppointmentSlotId = slot.Id,
            Reason = "Khám kiểm tra tính nguyên tử của giao dịch"
        });
        var aptDoc = JsonDocument.Parse(await createAptRes.Content.ReadAsStringAsync());
        var appointmentId = aptDoc.RootElement.GetProperty("data").GetProperty("id").GetInt64();

        await AuthenticateAsync("rec@test.com");
        await Client.PostAsync($"/api/v1/reception/appointments/{appointmentId}/confirm", null);

        await AuthenticateAsync("doc@test.com");
        await Client.PostAsync($"/api/v1/doctor/appointments/{appointmentId}/check-in", null);
        await Client.PostAsync($"/api/v1/doctor/appointments/{appointmentId}/start-consultation", null);

        // Try creating order with a non-existent service ID (99999999)
        var invalidOrderRes = await Client.PostAsJsonAsync($"/api/v1/doctor/appointments/{appointmentId}/diagnostic-orders", new CreateDiagnosticOrderRequest
        {
            ClinicalIndication = "Chỉ định dịch vụ không tồn tại",
            ServiceIds = new List<long> { 99999999 }
        });
        Assert.False(invalidOrderRes.IsSuccessStatusCode);

        // Verify that no diagnostic order was created for this appointment
        var getOrdersRes = await Client.GetAsync($"/api/v1/doctor/appointments/{appointmentId}/diagnostic-orders");
        Assert.Equal(HttpStatusCode.OK, getOrdersRes.StatusCode);
        var ordersDoc = JsonDocument.Parse(await getOrdersRes.Content.ReadAsStringAsync());
        var orders = ordersDoc.RootElement.GetProperty("data");
        Assert.Equal(0, orders.GetArrayLength());
    }
}

