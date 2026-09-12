using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using ClinicManagement.Application.Appointments.DTOs.Doctor;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Identity;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ClinicManagement.IntegrationTests;

public class DoctorWorkflowTests : IntegrationTestBase
{
    public DoctorWorkflowTests(CustomWebApplicationFactory factory) : base(factory) { }

    [Fact]
    public async Task Given_InactiveDoctorAccount_When_AccessingDoctorWorkspace_Then_ReturnsForbidden()
    {
        // 1. Create an inactive doctor user and entity
        var inactiveEmail = $"inactive_doc_{Guid.NewGuid():N}@test.com";
        Guid inactiveUserId;
        using (var scope = Factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var inactiveUser = new ApplicationUser
            {
                UserName = inactiveEmail,
                Email = inactiveEmail,
                FullName = "Inactive Doctor",
                PhoneNumber = "0987654321",
                IsActive = true // User active in Identity to obtain auth token
            };
            var createRes = await userManager.CreateAsync(inactiveUser, "Pass@123");
            Assert.True(createRes.Succeeded);
            await userManager.AddToRoleAsync(inactiveUser, "Doctor");
            inactiveUserId = inactiveUser.Id;

            var docEntity = new Doctor
            {
                UserId = inactiveUser.Id,
                Description = "Inactive Doctor Description",
                IsActive = false // Inactive doctor in clinic system
            };
            db.Doctors.Add(docEntity);
            await db.SaveChangesAsync();
        }

        try
        {
            // 2. Authenticate as inactive doctor
            await AuthenticateAsync(inactiveEmail);

            // 3. Attempt to access doctor dashboard
            var response = await Client.GetAsync("/api/v1/doctor/dashboard");
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

            var json = await response.Content.ReadAsStringAsync();
            Assert.Contains("DOCTOR_ACCOUNT_INACTIVE", json);
        }
        finally
        {
            using var cleanupScope = Factory.Services.CreateScope();
            var cleanupDb = cleanupScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var doc = await cleanupDb.Doctors.FirstOrDefaultAsync(d => d.UserId == inactiveUserId);
            if (doc != null) cleanupDb.Doctors.Remove(doc);
            var user = await cleanupDb.Users.FirstOrDefaultAsync(u => u.Id == inactiveUserId);
            if (user != null) cleanupDb.Users.Remove(user);
            await cleanupDb.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Given_Appointment_When_TransitioningStatuses_Then_EnforcesStrictStateMachine()
    {
        // 1. Book appointment
        var date = GetFutureWorkingDate(22);
        var slot = await CreateAvailableSlotAsync(DoctorEntityId, date, new TimeOnly(14, 0, 0), new TimeOnly(14, 30, 0));
        
        await AuthenticateAsync("pat1@test.com");
        var createResponse = await Client.PostAsJsonAsync("/api/v1/appointments", new
        {
            DoctorId = DoctorEntityId,
            SpecialtyId = SpecialtyEntityId,
            AppointmentSlotId = slot.Id,
            Reason = "Khám tổng quát tuần tới"
        });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var createDoc = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var appointmentId = createDoc.RootElement.GetProperty("data").GetProperty("id").GetInt64();

        // 2. Reception confirms appointment
        await AuthenticateAsync("rec@test.com");
        var confirmRes = await Client.PostAsync($"/api/v1/reception/appointments/{appointmentId}/confirm", null);
        Assert.Equal(HttpStatusCode.OK, confirmRes.StatusCode);

        // 3. Doctor logs in
        await AuthenticateAsync("doc@test.com");

        // Attempt Invalid Step A: Complete appointment directly while Confirmed (not InConsultation)
        var invalidCompleteRes = await Client.PostAsJsonAsync($"/api/v1/doctor/appointments/{appointmentId}/complete", new CompleteConsultationRequest
        {
            Diagnosis = "Chẩn đoán sớm",
            Summary = "Kết luận sớm"
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalidCompleteRes.StatusCode);
        var invalidCompleteJson = await invalidCompleteRes.Content.ReadAsStringAsync();
        Assert.Contains("INVALID_STATE_TRANSITION", invalidCompleteJson);

        // Attempt Invalid Step B: Start consultation while Confirmed (not CheckedIn)
        var invalidStartRes = await Client.PostAsync($"/api/v1/doctor/appointments/{appointmentId}/start-consultation", null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalidStartRes.StatusCode);
        var invalidStartJson = await invalidStartRes.Content.ReadAsStringAsync();
        Assert.Contains("INVALID_STATE_TRANSITION", invalidStartJson);

        // Step 1: Check-in appointment (Reception or Doctor)
        var checkInRes = await Client.PostAsync($"/api/v1/doctor/appointments/{appointmentId}/check-in", null);
        Assert.Equal(HttpStatusCode.OK, checkInRes.StatusCode);

        // Step 2: Start consultation (CheckedIn -> InConsultation)
        var startRes = await Client.PostAsync($"/api/v1/doctor/appointments/{appointmentId}/start-consultation", null);
        Assert.Equal(HttpStatusCode.OK, startRes.StatusCode);

        // Step 3: Complete consultation (InConsultation -> Completed)
        var completeRes = await Client.PostAsJsonAsync($"/api/v1/doctor/appointments/{appointmentId}/complete", new CompleteConsultationRequest
        {
            ChiefComplaint = "Đau đầu nhẹ",
            ClinicalFindings = "Huyết áp ổn định, tim đều",
            Diagnosis = "Căng thẳng do thiếu ngủ",
            TreatmentPlan = "Nghỉ ngơi, ngủ đủ giấc",
            Summary = "Bệnh nhân ổn định, không có dấu hiệu bất thường",
            FollowUpInstruction = "Tái khám sau 1 tuần nếu đau đầu tái diễn"
        });
        Assert.Equal(HttpStatusCode.OK, completeRes.StatusCode);

        // Verify final status
        var detailRes = await Client.GetAsync($"/api/v1/doctor/appointments/{appointmentId}");
        Assert.Equal(HttpStatusCode.OK, detailRes.StatusCode);
        var detailDoc = JsonDocument.Parse(await detailRes.Content.ReadAsStringAsync());
        var status = detailDoc.RootElement.GetProperty("data").GetProperty("status").GetString();
        Assert.Equal("Completed", status);
    }

    [Fact]
    public async Task Given_InConsultation_When_CompleteConsultationWithPrescription_Then_AtomicTransactionExecutes()
    {
        // 1. Setup slot, appointment, check-in, start consultation
        var date = GetFutureWorkingDate(23);
        var slot = await CreateAvailableSlotAsync(DoctorEntityId, date, new TimeOnly(15, 0, 0), new TimeOnly(15, 30, 0));

        await AuthenticateAsync("pat1@test.com");
        var createResponse = await Client.PostAsJsonAsync("/api/v1/appointments", new
        {
            DoctorId = DoctorEntityId,
            SpecialtyId = SpecialtyEntityId,
            AppointmentSlotId = slot.Id,
            Reason = "Viêm họng hạt"
        });
        var createDoc = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var appointmentId = createDoc.RootElement.GetProperty("data").GetProperty("id").GetInt64();

        await AuthenticateAsync("rec@test.com");
        await Client.PostAsync($"/api/v1/reception/appointments/{appointmentId}/confirm", null);

        await AuthenticateAsync("doc@test.com");
        await Client.PostAsync($"/api/v1/doctor/appointments/{appointmentId}/check-in", null);
        await Client.PostAsync($"/api/v1/doctor/appointments/{appointmentId}/start-consultation", null);

        // 2. Draft prescription
        var draftRes = await Client.PutAsJsonAsync($"/api/v1/doctor/appointments/{appointmentId}/prescription-draft", new SavePrescriptionDraftRequest
        {
            Notes = "Uống thuốc sau bữa ăn 30 phút",
            Items = new List<SavePrescriptionItemRequest>
            {
                new()
                {
                    MedicineId = MedicineEntityId,
                    Quantity = 10,
                    Dosage = "1 viên",
                    Frequency = "Ngày 2 lần",
                    DurationDays = 5,
                    Instructions = "Uống sau ăn"
                }
            }
        });
        Assert.Equal(HttpStatusCode.OK, draftRes.StatusCode);

        // 3. Complete consultation atomically with prescription issue
        var completeRes = await Client.PostAsJsonAsync($"/api/v1/doctor/appointments/{appointmentId}/complete", new CompleteConsultationRequest
        {
            ChiefComplaint = "Đau rát họng, ho nhẹ",
            ClinicalFindings = "Niêm mạc họng sung huyết",
            Diagnosis = "Viêm họng cấp",
            DiagnosisCode = "J02.9",
            TreatmentPlan = "Dùng kháng sinh, giảm đau, súc họng",
            Summary = "Bệnh nhân đáp ứng tốt, điều trị ngoại trú",
            FollowUpInstruction = "Tái khám sau 5 ngày",
            IssuePrescription = true
        });
        Assert.Equal(HttpStatusCode.OK, completeRes.StatusCode);

        // 4. Verify in database: Appointment is Completed, VisitSummary is saved, Prescription is Issued
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var appointment = await db.Appointments
            .Include(a => a.VisitSummary)
            .Include(a => a.Prescription)
                .ThenInclude(p => p!.Items)
            .FirstOrDefaultAsync(a => a.Id == appointmentId);

        Assert.NotNull(appointment);
        Assert.Equal(AppointmentStatus.Completed, appointment.Status);
        
        Assert.NotNull(appointment.VisitSummary);
        Assert.Equal("Viêm họng cấp", appointment.VisitSummary.Diagnosis);
        Assert.Equal("J02.9", appointment.VisitSummary.DiagnosisCode);
        Assert.NotNull(appointment.VisitSummary.CompletedAtUtc);

        Assert.NotNull(appointment.Prescription);
        Assert.Equal(PrescriptionStatus.Issued, appointment.Prescription.Status);
        Assert.Single(appointment.Prescription.Items);
    }

    [Fact]
    public async Task Given_EncounterOrVitals_When_ConcurrentUpdateWithStaleVersion_Then_ReturnsConflict409()
    {
        // 1. Setup active consultation
        var date = GetFutureWorkingDate(24);
        var slot = await CreateAvailableSlotAsync(DoctorEntityId, date, new TimeOnly(16, 0, 0), new TimeOnly(16, 30, 0));

        await AuthenticateAsync("pat1@test.com");
        var createResponse = await Client.PostAsJsonAsync("/api/v1/appointments", new
        {
            DoctorId = DoctorEntityId,
            SpecialtyId = SpecialtyEntityId,
            AppointmentSlotId = slot.Id,
            Reason = "Kiểm tra huyết áp"
        });
        var createDoc = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var appointmentId = createDoc.RootElement.GetProperty("data").GetProperty("id").GetInt64();

        await AuthenticateAsync("rec@test.com");
        await Client.PostAsync($"/api/v1/reception/appointments/{appointmentId}/confirm", null);

        await AuthenticateAsync("doc@test.com");
        await Client.PostAsync($"/api/v1/doctor/appointments/{appointmentId}/check-in", null);
        await Client.PostAsync($"/api/v1/doctor/appointments/{appointmentId}/start-consultation", null);

        // 2. Doctor saves initial encounter -> captures rowVersion v1
        var initialSaveRes = await Client.PutAsJsonAsync($"/api/v1/doctor/appointments/{appointmentId}/encounter", new SaveEncounterRequest
        {
            Diagnosis = "Tăng huyết áp nhẹ",
            Summary = "Ghi chú ban đầu"
        });
        Assert.Equal(HttpStatusCode.OK, initialSaveRes.StatusCode);
        var initialSaveDoc = JsonDocument.Parse(await initialSaveRes.Content.ReadAsStringAsync());
        var rowVersionV1 = initialSaveDoc.RootElement.GetProperty("data").GetProperty("rowVersion").GetString();
        Assert.NotNull(rowVersionV1);

        // 3. Client A updates encounter with v1 -> succeeds, increments rowVersion to v2
        var update1Res = await Client.PutAsJsonAsync($"/api/v1/doctor/appointments/{appointmentId}/encounter", new SaveEncounterRequest
        {
            Diagnosis = "Tăng huyết áp độ 1",
            Summary = "Cập nhật từ Client A",
            RowVersion = rowVersionV1
        });
        Assert.Equal(HttpStatusCode.OK, update1Res.StatusCode);

        // 4. Client B updates encounter with stale v1 -> returns 409 Conflict
        var update2ConflictRes = await Client.PutAsJsonAsync($"/api/v1/doctor/appointments/{appointmentId}/encounter", new SaveEncounterRequest
        {
            Diagnosis = "Tăng huyết áp độ 2",
            Summary = "Cập nhật cạnh tranh từ Client B",
            RowVersion = rowVersionV1 // Stale!
        });
        Assert.Equal(HttpStatusCode.Conflict, update2ConflictRes.StatusCode);

        // 5. Test vital signs concurrency:
        var vitals1Res = await Client.PutAsJsonAsync($"/api/v1/doctor/appointments/{appointmentId}/vitals", new SaveVitalSignsRequest
        {
            Temperature = 37.0m,
            BloodPressureSystolic = 130,
            BloodPressureDiastolic = 85,
            HeartRate = 78,
            Weight = 70.0m,
            Height = 175.0m
        });
        Assert.Equal(HttpStatusCode.OK, vitals1Res.StatusCode);
        var vitals1Doc = JsonDocument.Parse(await vitals1Res.Content.ReadAsStringAsync());
        var vitalsRowVersionV1 = vitals1Doc.RootElement.GetProperty("data").GetProperty("rowVersion").GetString();
        var bmi = vitals1Doc.RootElement.GetProperty("data").GetProperty("bmi").GetDecimal();
        Assert.True(bmi > 22.0m && bmi < 23.5m); // 70 / (1.75 * 1.75) ≈ 22.86

        // Update vitals with valid rowVersion succeeds
        var vitalsUpdate1Res = await Client.PutAsJsonAsync($"/api/v1/doctor/appointments/{appointmentId}/vitals", new SaveVitalSignsRequest
        {
            Temperature = 36.8m,
            BloodPressureSystolic = 125,
            BloodPressureDiastolic = 80,
            Weight = 70.0m,
            Height = 175.0m,
            RowVersion = vitalsRowVersionV1
        });
        Assert.Equal(HttpStatusCode.OK, vitalsUpdate1Res.StatusCode);

        // Update vitals with stale rowVersion returns 409 Conflict
        var vitalsConflictRes = await Client.PutAsJsonAsync($"/api/v1/doctor/appointments/{appointmentId}/vitals", new SaveVitalSignsRequest
        {
            Temperature = 37.5m,
            RowVersion = vitalsRowVersionV1 // Stale!
        });
        Assert.Equal(HttpStatusCode.Conflict, vitalsConflictRes.StatusCode);
    }

    [Fact]
    public async Task Given_PatientOrOtherDoctor_When_AccessingDoctorWorkspaceOrAppointments_Then_EnforcesRbacAndPrivacy()
    {
        // 1. Patient tries to access doctor dashboard -> 403 Forbidden
        await AuthenticateAsync("pat1@test.com");
        var patDashboardRes = await Client.GetAsync("/api/v1/doctor/dashboard");
        Assert.Equal(HttpStatusCode.Forbidden, patDashboardRes.StatusCode);

        // 2. Doctor 1 has an appointment
        var date = GetFutureWorkingDate(25);
        var slot = await CreateAvailableSlotAsync(DoctorEntityId, date, new TimeOnly(17, 0, 0), new TimeOnly(17, 30, 0));

        await AuthenticateAsync("pat1@test.com");
        var createResponse = await Client.PostAsJsonAsync("/api/v1/appointments", new
        {
            DoctorId = DoctorEntityId,
            SpecialtyId = SpecialtyEntityId,
            AppointmentSlotId = slot.Id,
            Reason = "Khám tim mạch định kỳ"
        });
        var createDoc = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var appointmentId = createDoc.RootElement.GetProperty("data").GetProperty("id").GetInt64();

        // 3. Patient tries to call doctor clinical encounter endpoint -> 403 Forbidden
        var patEncounterRes = await Client.GetAsync($"/api/v1/doctor/appointments/{appointmentId}/encounter");
        Assert.Equal(HttpStatusCode.Forbidden, patEncounterRes.StatusCode);

        // 4. Doctor 2 (another doctor) tries to access Doctor 1's appointment -> 404 NotFound (data isolation)
        // Ensure Doctor 2 exists
        var doc2Email = "doc2@test.com";
        using (var scope = Factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var existingDoc2 = await userManager.FindByEmailAsync(doc2Email);
            if (existingDoc2 == null)
            {
                var user2 = new ApplicationUser
                {
                    UserName = doc2Email,
                    Email = doc2Email,
                    FullName = "Doctor 2 Test",
                    PhoneNumber = "0987654399",
                    IsActive = true
                };
                await userManager.CreateAsync(user2, "Pass@123");
                await userManager.AddToRoleAsync(user2, "Doctor");

                var doc2Entity = new Doctor
                {
                    UserId = user2.Id,
                    Description = "Doctor 2 Description",
                    IsActive = true,
                    ExperienceYears = 5,
                    AcademicTitle = "BS"
                };
                db.Doctors.Add(doc2Entity);
                await db.SaveChangesAsync();
            }
        }

        await AuthenticateAsync(doc2Email);
        var doc2DetailRes = await Client.GetAsync($"/api/v1/doctor/appointments/{appointmentId}");
        Assert.Equal(HttpStatusCode.NotFound, doc2DetailRes.StatusCode);

        // 5. Doctor 1 accesses own appointment -> 200 OK
        await AuthenticateAsync("doc@test.com");
        var doc1DetailRes = await Client.GetAsync($"/api/v1/doctor/appointments/{appointmentId}");
        Assert.Equal(HttpStatusCode.OK, doc1DetailRes.StatusCode);

        // 6. Test leave preview impact endpoint
        var leavePreviewRes = await Client.GetAsync($"/api/v1/doctor/leaves/preview-impact?startDate={date:yyyy-MM-dd}&endDate={date:yyyy-MM-dd}");
        Assert.Equal(HttpStatusCode.OK, leavePreviewRes.StatusCode);
        var leaveDoc = JsonDocument.Parse(await leavePreviewRes.Content.ReadAsStringAsync());
        var affectedCount = leaveDoc.RootElement.GetProperty("data").GetProperty("affectedAppointmentsCount").GetInt32();
        Assert.True(affectedCount >= 1);
    }
}
