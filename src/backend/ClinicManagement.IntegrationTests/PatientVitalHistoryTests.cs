using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using ClinicManagement.Application.Appointments.DTOs.Doctor;
using ClinicManagement.Domain.Enums;
using Xunit;

namespace ClinicManagement.IntegrationTests;

public class PatientVitalHistoryTests : IntegrationTestBase
{
    public PatientVitalHistoryTests(CustomWebApplicationFactory factory) : base(factory) { }

    [Fact]
    public async Task Given_MultipleAppointments_When_VitalsRecorded_Then_CalculatesDeltaAndBmiCorrectly_And_ProvidesPatientHistory()
    {
        // 1. Setup Appointment 1
        var date1 = GetFutureWorkingDate(30);
        var slot1 = await CreateAvailableSlotAsync(DoctorEntityId, date1, new TimeOnly(8, 0, 0), new TimeOnly(8, 30, 0));

        await AuthenticateAsync("pat1@test.com");
        var createRes1 = await Client.PostAsJsonAsync("/api/v1/appointments", new
        {
            DoctorId = DoctorEntityId,
            SpecialtyId = SpecialtyEntityId,
            AppointmentSlotId = slot1.Id,
            Reason = "Khám lần 1"
        });
        Assert.Equal(HttpStatusCode.Created, createRes1.StatusCode);
        var doc1 = JsonDocument.Parse(await createRes1.Content.ReadAsStringAsync());
        var aptId1 = doc1.RootElement.GetProperty("data").GetProperty("id").GetInt64();

        // Reception confirms
        await AuthenticateAsync("rec@test.com");
        var conf1 = await Client.PostAsync($"/api/v1/reception/appointments/{aptId1}/confirm", null);
        Assert.Equal(HttpStatusCode.OK, conf1.StatusCode);

        // Check in & start consultation 1
        await AuthenticateAsync("doc@test.com");
        var checkIn1 = await Client.PostAsync($"/api/v1/doctor/appointments/{aptId1}/check-in", null);
        Assert.Equal(HttpStatusCode.OK, checkIn1.StatusCode);
        var start1 = await Client.PostAsync($"/api/v1/doctor/appointments/{aptId1}/start-consultation", null);
        Assert.Equal(HttpStatusCode.OK, start1.StatusCode);

        // Record vitals for appointment 1: 60kg, 170cm
        var saveVitalsRes1 = await Client.PutAsJsonAsync($"/api/v1/doctor/appointments/{aptId1}/vitals", new SaveVitalSignsRequest
        {
            Weight = 60.0m,
            Height = 170.0m,
            HeartRate = 75,
            BloodPressureSystolic = 120,
            BloodPressureDiastolic = 80,
            Temperature = 36.6m,
            SpO2 = 98
        });
        Assert.Equal(HttpStatusCode.OK, saveVitalsRes1.StatusCode);

        // Complete consultation 1
        var compRes1 = await Client.PostAsJsonAsync($"/api/v1/doctor/appointments/{aptId1}/complete", new CompleteConsultationRequest
        {
            Diagnosis = "Bình thường",
            Summary = "Hoàn thành khám lần 1"
        });
        Assert.Equal(HttpStatusCode.OK, compRes1.StatusCode);

        // 2. Setup Appointment 2
        var date2 = GetFutureWorkingDate(31);
        var slot2 = await CreateAvailableSlotAsync(DoctorEntityId, date2, new TimeOnly(9, 0, 0), new TimeOnly(9, 30, 0));

        await AuthenticateAsync("pat1@test.com");
        var createRes2 = await Client.PostAsJsonAsync("/api/v1/appointments", new
        {
            DoctorId = DoctorEntityId,
            SpecialtyId = SpecialtyEntityId,
            AppointmentSlotId = slot2.Id,
            Reason = "Khám lần 2 tái khám"
        });
        Assert.Equal(HttpStatusCode.Created, createRes2.StatusCode);
        var doc2 = JsonDocument.Parse(await createRes2.Content.ReadAsStringAsync());
        var aptId2 = doc2.RootElement.GetProperty("data").GetProperty("id").GetInt64();

        // Reception confirms
        await AuthenticateAsync("rec@test.com");
        var conf2 = await Client.PostAsync($"/api/v1/reception/appointments/{aptId2}/confirm", null);
        Assert.Equal(HttpStatusCode.OK, conf2.StatusCode);

        // Check in & start consultation 2
        await AuthenticateAsync("doc@test.com");
        await Client.PostAsync($"/api/v1/doctor/appointments/{aptId2}/check-in", null);
        await Client.PostAsync($"/api/v1/doctor/appointments/{aptId2}/start-consultation", null);

        // 3. Before recording vitals on apt 2: Clinical Context should show previous vitals & latest known vitals
        var contextRes = await Client.GetAsync($"/api/v1/doctor/appointments/{aptId2}/patient-context");
        Assert.Equal(HttpStatusCode.OK, contextRes.StatusCode);
        var contextJson = await contextRes.Content.ReadAsStringAsync();
        var contextDoc = JsonDocument.Parse(contextJson);
        var dataProp = contextDoc.RootElement.GetProperty("data");

        // LatestKnownVitals check
        Assert.True(dataProp.TryGetProperty("latestKnownVitals", out var latestKnownProp));
        Assert.False(latestKnownProp.ValueKind == JsonValueKind.Null);
        Assert.Equal(60.0, latestKnownProp.GetProperty("weight").GetDouble(), 1);
        Assert.Equal(170.0, latestKnownProp.GetProperty("height").GetDouble(), 1);

        // AnthropometricComparison check before second vitals: Previous values should exist, Deltas null
        var compProp = dataProp.GetProperty("anthropometricComparison");
        var prevMeasure = compProp.GetProperty("previousMeasurement");
        Assert.Equal(60.0, prevMeasure.GetProperty("weight").GetDouble(), 1);
        Assert.Equal(170.0, prevMeasure.GetProperty("height").GetDouble(), 1);
        Assert.Equal(JsonValueKind.Null, compProp.GetProperty("weightDeltaKg").ValueKind);

        // 4. Record vitals for appointment 2: 63kg, 170cm
        var saveVitalsRes2 = await Client.PutAsJsonAsync($"/api/v1/doctor/appointments/{aptId2}/vitals", new SaveVitalSignsRequest
        {
            Weight = 63.0m,
            Height = 170.0m,
            HeartRate = 80,
            BloodPressureSystolic = 125,
            BloodPressureDiastolic = 82,
            Temperature = 36.8m,
            SpO2 = 99
        });
        Assert.Equal(HttpStatusCode.OK, saveVitalsRes2.StatusCode);

        // Re-check clinical context: Delta should now be computed!
        var contextRes2 = await Client.GetAsync($"/api/v1/doctor/appointments/{aptId2}/patient-context");
        var contextDoc2 = JsonDocument.Parse(await contextRes2.Content.ReadAsStringAsync());
        var compProp2 = contextDoc2.RootElement.GetProperty("data").GetProperty("anthropometricComparison");

        Assert.Equal(3.0, compProp2.GetProperty("weightDeltaKg").GetDouble(), 1);
        Assert.Equal(0.0, compProp2.GetProperty("heightDeltaCm").GetDouble(), 1);
        Assert.True(compProp2.GetProperty("bmiDelta").GetDouble() > 0);

        // Check VitalHistory list: should have appointment 1
        var historyList = contextDoc2.RootElement.GetProperty("data").GetProperty("vitalHistory");
        Assert.True(historyList.GetArrayLength() >= 1);

        // 5. Patient views their vitals via /api/v1/patients/me/vitals
        await AuthenticateAsync("pat1@test.com");
        var myVitalsRes = await Client.GetAsync("/api/v1/patients/me/vitals?limit=10");
        Assert.Equal(HttpStatusCode.OK, myVitalsRes.StatusCode);
        var myVitalsDoc = JsonDocument.Parse(await myVitalsRes.Content.ReadAsStringAsync());
        var vitalsArray = myVitalsDoc.RootElement.GetProperty("data");
        Assert.True(vitalsArray.GetArrayLength() >= 2);
    }

    [Fact]
    public async Task Given_ConcurrentVitalUpdates_When_RowVersionMismatches_Then_Throws409Conflict()
    {
        var date = GetFutureWorkingDate(32);
        var slot = await CreateAvailableSlotAsync(DoctorEntityId, date, new TimeOnly(10, 0, 0), new TimeOnly(10, 30, 0));

        await AuthenticateAsync("pat1@test.com");
        var createRes = await Client.PostAsJsonAsync("/api/v1/appointments", new
        {
            DoctorId = DoctorEntityId,
            SpecialtyId = SpecialtyEntityId,
            AppointmentSlotId = slot.Id,
            Reason = "Khám kiểm tra concurrency"
        });
        var doc = JsonDocument.Parse(await createRes.Content.ReadAsStringAsync());
        var aptId = doc.RootElement.GetProperty("data").GetProperty("id").GetInt64();

        // Reception confirms
        await AuthenticateAsync("rec@test.com");
        await Client.PostAsync($"/api/v1/reception/appointments/{aptId}/confirm", null);

        await AuthenticateAsync("doc@test.com");
        await Client.PostAsync($"/api/v1/doctor/appointments/{aptId}/check-in", null);
        await Client.PostAsync($"/api/v1/doctor/appointments/{aptId}/start-consultation", null);

        // First save vitals
        var saveRes1 = await Client.PutAsJsonAsync($"/api/v1/doctor/appointments/{aptId}/vitals", new SaveVitalSignsRequest
        {
            Weight = 65.0m,
            Height = 175.0m
        });
        Assert.Equal(HttpStatusCode.OK, saveRes1.StatusCode);

        // Second save with stale/fake row version
        var staleRowVersion = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        var saveResConflict = await Client.PutAsJsonAsync($"/api/v1/doctor/appointments/{aptId}/vitals", new SaveVitalSignsRequest
        {
            Weight = 66.0m,
            Height = 175.0m,
            RowVersion = staleRowVersion
        });
        Assert.Equal(HttpStatusCode.Conflict, saveResConflict.StatusCode);
        var conflictJson = await saveResConflict.Content.ReadAsStringAsync();
        Assert.Contains("CONCURRENCY_CONFLICT", conflictJson);
    }

    [Fact]
    public async Task Given_InvalidVitalSigns_When_Saving_Then_ReturnsValidationErrors()
    {
        var date = GetFutureWorkingDate(33);
        var slot = await CreateAvailableSlotAsync(DoctorEntityId, date, new TimeOnly(11, 0, 0), new TimeOnly(11, 30, 0));

        await AuthenticateAsync("pat1@test.com");
        var createRes = await Client.PostAsJsonAsync("/api/v1/appointments", new
        {
            DoctorId = DoctorEntityId,
            SpecialtyId = SpecialtyEntityId,
            AppointmentSlotId = slot.Id,
            Reason = "Khám validation"
        });
        var doc = JsonDocument.Parse(await createRes.Content.ReadAsStringAsync());
        var aptId = doc.RootElement.GetProperty("data").GetProperty("id").GetInt64();

        // Reception confirms
        await AuthenticateAsync("rec@test.com");
        await Client.PostAsync($"/api/v1/reception/appointments/{aptId}/confirm", null);

        await AuthenticateAsync("doc@test.com");
        await Client.PostAsync($"/api/v1/doctor/appointments/{aptId}/check-in", null);
        await Client.PostAsync($"/api/v1/doctor/appointments/{aptId}/start-consultation", null);

        // Case 1: Negative weight
        var res1 = await Client.PutAsJsonAsync($"/api/v1/doctor/appointments/{aptId}/vitals", new SaveVitalSignsRequest
        {
            Weight = -5.0m,
            Height = 170.0m
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, res1.StatusCode);

        // Case 2: Only systolic provided, diastolic missing
        var res2 = await Client.PutAsJsonAsync($"/api/v1/doctor/appointments/{aptId}/vitals", new SaveVitalSignsRequest
        {
            BloodPressureSystolic = 120,
            BloodPressureDiastolic = null
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, res2.StatusCode);

        // Case 3: SpO2 > 100
        var res3 = await Client.PutAsJsonAsync($"/api/v1/doctor/appointments/{aptId}/vitals", new SaveVitalSignsRequest
        {
            SpO2 = 105
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, res3.StatusCode);
    }
}
