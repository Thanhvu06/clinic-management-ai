using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using ClinicManagement.Infrastructure.Appointments;
using ClinicManagement.Infrastructure.AI;
using ClinicManagement.Application.Common.Interfaces;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Moq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicManagement.IntegrationTests;

public class FacilityPrivacyTests(CustomWebApplicationFactory factory) : IntegrationTestBase(factory), IDisposable
{
    private readonly List<long> _facilities = [];
    public void Dispose()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var facility in db.Facilities.Where(x => _facilities.Contains(x.Id))) facility.IsActive = false;
        db.SaveChanges();
    }

    [Theory]
    [InlineData(0, "error")]
    [InlineData(1, "reason")]
    [InlineData(2, "facility")]
    public async Task A2_Wizard_resolves_zero_one_or_two_eligible_facilities(int count, string expected)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var old = await db.StaffFacilityAssignments.Where(x => x.UserId == DoctorId && x.Role == "Doctor" && x.IsActive).ToListAsync();
        foreach (var assignment in old) assignment.IsActive = false;
        await db.SaveChangesAsync();
        try
        {
            for (var i = 0; i < count; i++) await FacilityAsync(db, true);
            using var client = await CreateAuthenticatedClientAsync("pat1@test.com");
            var slot = await CreateAvailableSlotAsync(DoctorEntityId, GetFutureWorkingDate(3), new(10 + count, 17), new(10 + count, 47));
            var session = "p5_" + Guid.NewGuid().ToString("N");
            var specialty = await db.Specialties.FindAsync(SpecialtyEntityId);
            var name = await db.Users.Where(x => x.Id == DoctorId).Select(x => x.FullName).SingleAsync();
            var state = await StepAsync(client, session, "start");
            state = await PickAsync(client, session, state, specialty!.Name);
            state = await PickAsync(client, session, state, name);
            state = await PickAsync(client, session, state, slot.SlotDate.ToString("dd/MM/yyyy"));
            state = await PickAsync(client, session, state, $"{slot.StartTime:HH:mm} – {slot.EndTime:HH:mm}");
            Assert.Equal(expected, state.GetProperty("step").GetString());
            if (count == 0) Assert.Equal("FACILITY_NOT_AVAILABLE", state.GetProperty("errorCode").GetString());
            if (count == 2)
            {
                Assert.True(state.GetProperty("canGoBack").GetBoolean());
                Assert.Equal(2, state.GetProperty("options").GetArrayLength());
                state = await PickAsync(client, session, state, "Cơ sở P5 1");
                Assert.Equal("reason", state.GetProperty("step").GetString());
            }
            if (count > 0)
            {
                var review = await StepAsync(client, session, "reason", state.GetProperty("reasonToken").GetString(), "Khám tổng quát kiểm thử cơ sở");
                Assert.Equal("review", review.GetProperty("step").GetString());
                var payload = review.GetProperty("reviewAction").GetProperty("payload");
                Assert.True(payload.TryGetProperty("facilityId", out var selectedFacility), "Review must carry its selected facility.");
                Assert.Equal(_facilities[0], selectedFacility.GetInt64());
                var confirmationId = payload.GetProperty("confirmationId").GetString();
                var confirmation = await db.AiBookingConfirmations.AsNoTracking().SingleAsync(x => x.ConfirmationId == confirmationId);
                Assert.Equal(_facilities[0], (long?)confirmation.GetType().GetProperty("FacilityId")!.GetValue(confirmation));
                var booking = payload.EnumerateObject().ToDictionary(property => property.Name, property => (object?)property.Value.Clone());
                booking["appointmentSlotId"] = payload.GetProperty("slotId").GetInt64();
                if (count == 2)
                {
                    booking["facilityId"] = _facilities[1];
                    var mismatch = await client.PostAsJsonAsync("/api/v1/appointments", booking);
                    Assert.Equal(HttpStatusCode.Conflict, mismatch.StatusCode);
                    Assert.Contains("CONFIRMATION_PAYLOAD_MISMATCH", await mismatch.Content.ReadAsStringAsync());
                }
                booking["facilityId"] = _facilities[0];
                var booked = await client.PostAsJsonAsync("/api/v1/appointments", booking);
                Assert.True(booked.IsSuccessStatusCode, await booked.Content.ReadAsStringAsync());
                Assert.Equal(_facilities[0], (await db.Appointments.SingleAsync(appointment => appointment.AppointmentSlotId == slot.Id)).FacilityId);
            }
        }
        finally
        {
            foreach (var assignment in old) assignment.IsActive = true;
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public void A5_Token_v2_binds_facility_and_rejects_v1_tampering_expiry_and_other_sessions()
    {
        var now = DateTime.UtcNow;
        var clock = new Mock<IDateTimeProvider>();
        clock.SetupGet(x => x.UtcNow).Returns(() => now);
        var protection = new EphemeralDataProtectionProvider();
        var tokens = new AiBookingWizardTokens(protection, clock.Object);
        var state = new BookingWizardSelection(Guid.NewGuid(), BookingWizardStage.Reason, SpecialtyEntityId, DoctorEntityId, 1, 2, FacilityId: 42);
        var token = tokens.Issue(Patient1Id, "p5", state, BookingWizardOperation.Reason);
        Assert.Equal(state, tokens.Read(token, Patient1Id, "p5", "reason"));
        Assert.Null(tokens.Read(token, Patient2Id, "p5", "reason"));
        Assert.Null(tokens.Read(token, Patient1Id, "other", "reason"));
        var protector = protection.CreateProtector("ClinicManagement.AI.BookingWizard.v1").ToTimeLimitedDataProtector();
        var bytes = protector.Unprotect(WebEncoders.Base64UrlDecode(token), out _);
        Assert.Equal(2, bytes[0]);
        bytes[0] = 1;
        var old = WebEncoders.Base64UrlEncode(protector.Protect(bytes, TimeSpan.FromMinutes(15)));
        Assert.Null(tokens.Read(old, Patient1Id, "p5", "reason"));
        var forged = WebEncoders.Base64UrlDecode(token);
        forged[^1] ^= 1;
        Assert.Null(tokens.Read(WebEncoders.Base64UrlEncode(forged), Patient1Id, "p5", "reason"));
        now = now.AddMinutes(15);
        Assert.Null(tokens.Read(token, Patient1Id, "p5", "reason"));
    }

    [Fact]
    public async Task A5_Authenticated_token_cannot_select_a_facility_outside_the_eligible_list()
    {
        using var scope = Factory.Services.CreateScope();
        var slot = await CreateAvailableSlotAsync(DoctorEntityId, GetFutureWorkingDate(4), new(12, 9), new(12, 39));
        var session = "p5_" + Guid.NewGuid().ToString("N");
        var tokens = scope.ServiceProvider.GetRequiredService<AiBookingWizardTokens>();
        var token = tokens.Issue(Patient1Id, session, new(Guid.NewGuid(), BookingWizardStage.Reason, SpecialtyEntityId, DoctorEntityId, slot.SlotDate.DayNumber, slot.Id, FacilityId: long.MaxValue), BookingWizardOperation.Reason);
        using var client = await CreateAuthenticatedClientAsync("pat1@test.com");
        var response = await StepAsync(client, session, "reason", token, "Khám tổng quát kiểm thử");
        Assert.Equal("FACILITY_SCOPE_DENIED", response.GetProperty("errorCode").GetString());
    }

    [Theory]
    [InlineData("valid", true)]
    [InlineData("role", false)]
    [InlineData("assignment", false)]
    [InlineData("facility", false)]
    [InlineData("department", false)]
    [InlineData("specialty", false)]
    [InlineData("department-binding", false)]
    public async Task A1_Shared_eligibility_keeps_live_role_department_and_specialty_conditions(string variant, bool eligible)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var facilityId = await FacilityAsync(db, true);
        var assignment = await db.StaffFacilityAssignments.SingleAsync(x => x.FacilityId == facilityId);
        var department = await db.Departments.SingleAsync(x => x.FacilityId == facilityId);
        assignment.IsPrimary = true;
        if (variant == "role") assignment.Role = "Receptionist";
        if (variant == "assignment") assignment.IsActive = false;
        if (variant == "facility") (await db.Facilities.FindAsync(facilityId))!.IsActive = false;
        if (variant == "department") department.IsActive = false;
        if (variant == "specialty") department.SpecialtyId = CardiologySpecialtyId;
        if (variant == "department-binding") assignment.DepartmentId = await db.Departments.Where(x => x.FacilityId != facilityId).Select(x => x.Id).FirstAsync();
        await db.SaveChangesAsync();
        var result = await AppointmentFacilityResolver.GetEligibleAsync(db, DoctorEntityId, SpecialtyEntityId);
        Assert.Equal(eligible, result.Any(x => x.Id == facilityId));
        if (eligible) Assert.True(result.Single(x => x.Id == facilityId).IsPrimary);
    }

    [Theory]
    [InlineData("phone-only")]
    [InlineData("code-only")]
    [InlineData("wrong-phone")]
    [InlineData("partial-code")]
    [InlineData("normalized-phone")]
    public async Task B_Lookup_requires_exact_code_and_normalized_owner_phone(string variant)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var source = await db.AppointmentSlots.FindAsync(SlotEntityId);
        var phone = await db.Users.Where(x => x.Id == Patient1Id).Select(x => x.PhoneNumber).SingleAsync();
        var appointment = new Appointment { AppointmentCode = "P5-" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant(), PatientId = Patient1EntityId, DoctorId = DoctorEntityId, SpecialtyId = SpecialtyEntityId, AppointmentSlotId = source!.Id, AppointmentDate = source.SlotDate, StartTime = source.StartTime, EndTime = source.EndTime, Reason = "Kiểm thử tra cứu" };
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync();
        var query = variant == "phone-only" ? phone : variant == "partial-code" ? appointment.AppointmentCode[..6] : appointment.AppointmentCode;
        var enteredPhone = variant == "code-only" ? "" : variant == "wrong-phone" ? "0999999999" : " " + phone![..3] + "-" + phone[3..] + " ";
        var result = await Client.GetFromJsonAsync<JsonElement>($"/api/v1/appointments/lookup?query={Uri.EscapeDataString(query!)}&phone={Uri.EscapeDataString(enteredPhone)}");
        Assert.Equal(variant == "normalized-phone" ? 1 : 0, result.GetProperty("data").GetArrayLength());
    }

    [Theory]
    [InlineData("doc@test.com", "none", false)]
    [InlineData("doc@test.com", "visit", true)]
    [InlineData("doc@test.com", "appointment", true)]
    [InlineData("doc@test.com", "other-appointment", false)]
    [InlineData("doc@test.com", "other-visit", false)]
    [InlineData("rec@test.com", "none", true)]
    [InlineData("admin@test.com", "none", true)]
    public async Task C_Mpi_read_and_allergy_scope_matches_doctor_relationship(string email, string link, bool visible)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var code = "P5-" + Guid.NewGuid().ToString("N")[..10];
        var patient = new Patient { MedicalRecordNumber = code, FullName = "Bệnh nhân P5", PhoneNumber = "0901234567" };
        db.Patients.Add(patient);
        await db.SaveChangesAsync();
        var department = await db.Departments.FirstAsync(x => x.IsActive);
        if (link is "visit" or "other-visit") db.PatientVisits.Add(new() { VisitCode = code, PatientId = patient.Id, AssignedDoctorId = link == "visit" ? DoctorEntityId : Doctor2EntityId, FacilityId = department.FacilityId, DepartmentId = department.Id, VisitDate = GetFutureWorkingDate(210), QueueNumber = (int)patient.Id });
        if (link is "appointment" or "other-appointment")
        {
            var slot = await db.AppointmentSlots.FindAsync(SlotEntityId);
            db.Appointments.Add(new() { AppointmentCode = code, PatientId = patient.Id, DoctorId = link == "appointment" ? DoctorEntityId : Doctor2EntityId, SpecialtyId = SpecialtyEntityId, AppointmentSlotId = slot!.Id, AppointmentDate = slot.SlotDate, StartTime = slot.StartTime, EndTime = slot.EndTime, Reason = "Khám kiểm thử" });
        }
        await db.SaveChangesAsync();
        using var client = await CreateAuthenticatedClientAsync(email);
        var search = await client.GetFromJsonAsync<JsonElement>($"/api/v1/mpi/patients?medicalRecordNumber={code}");
        Assert.Equal(visible ? 1 : 0, search.GetProperty("data").GetProperty("items").GetArrayLength());
        Assert.Equal(visible ? HttpStatusCode.OK : HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/mpi/patients/{patient.Id}")).StatusCode);
        Assert.Equal(visible ? HttpStatusCode.OK : HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/mpi/patients/by-mrn/{code}")).StatusCode);
        var add = await client.PostAsJsonAsync($"/api/v1/mpi/patients/{patient.Id}/allergies", new { allergenType = 0, allergenName = "Penicillin", severity = 1 });
        Assert.Equal(visible ? HttpStatusCode.OK : HttpStatusCode.NotFound, add.StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task D_Walk_in_saves_details_only_for_new_patients(bool existing)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var facilityId = await FacilityAsync(db, false);
        var department = await db.Departments.SingleAsync(x => x.FacilityId == facilityId);
        using var client = await CreateAuthenticatedClientAsync("rec@test.com");
        var beforeAllergies = await db.PatientAllergies.CountAsync(x => x.PatientId == Patient1EntityId);
        var beforeContacts = await db.EmergencyContacts.CountAsync(x => x.PatientId == Patient1EntityId);
        var response = await client.PostAsJsonAsync("/api/v1/patient-visits/walk-in", new { existingPatientId = existing ? (long?)Patient1EntityId : null, fullName = "Người bệnh " + Guid.NewGuid().ToString("N"), phoneNumber = "0901112233", facilityId, departmentId = department.Id, chiefComplaint = "Khám tổng quát", allergies = new[] { new { allergen = " Penicillin ", severity = "Severe", reaction = " Phát ban " } }, emergencyContact = new { contactName = " Người giám hộ ", relationship = " Mẹ ", phoneNumber = " 0904445566 ", isGuardian = true } });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var visitId = body.GetProperty("data").GetProperty("visitId").GetInt64();
        var patientId = await db.PatientVisits.Where(x => x.Id == visitId).Select(x => x.PatientId).SingleAsync();
        if (existing)
        {
            Assert.Equal(beforeAllergies, await db.PatientAllergies.CountAsync(x => x.PatientId == patientId));
            Assert.Equal(beforeContacts, await db.EmergencyContacts.CountAsync(x => x.PatientId == patientId));
        }
        else
        {
            var allergy = Assert.Single(await db.PatientAllergies.Where(x => x.PatientId == patientId).ToListAsync());
            Assert.Equal("Penicillin", allergy.AllergenName);
            Assert.Equal(AllergySeverity.Severe, allergy.Severity);
            Assert.Equal("Phát ban", allergy.ReactionDescription);
            var contact = await db.EmergencyContacts.SingleAsync(x => x.PatientId == patientId);
            Assert.Equal("Người giám hộ", contact.FullName);
            Assert.Equal("0904445566", contact.PhoneNumber);
            Assert.True(contact.IsGuardian);
            Assert.True(contact.IsPrimary);
        }
    }

    private async Task<long> FacilityAsync(AppDbContext db, bool doctor)
    {
        var facility = new Facility { Code = "P5-" + Guid.NewGuid().ToString("N")[..10], Name = $"Cơ sở P5 {_facilities.Count + 1}", IsActive = true };
        db.Facilities.Add(facility);
        await db.SaveChangesAsync();
        _facilities.Add(facility.Id);
        db.Departments.Add(new() { FacilityId = facility.Id, Code = facility.Code, Name = "Khoa P5", SpecialtyId = SpecialtyEntityId, IsActive = true });
        db.StaffFacilityAssignments.Add(new() { UserId = doctor ? DoctorId : ReceptionistId, FacilityId = facility.Id, Role = doctor ? "Doctor" : "Receptionist", IsActive = true });
        await db.SaveChangesAsync();
        return facility.Id;
    }
    private static async Task<JsonElement> StepAsync(HttpClient client, string sessionId, string step, string? optionToken = null, string? reason = null)
    {
        var result = await client.PostAsJsonAsync("/api/v1/ai/booking-wizard", new { sessionId, step, optionToken, reason });
        Assert.True(result.IsSuccessStatusCode, await result.Content.ReadAsStringAsync());
        return (await result.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
    }
    private static Task<JsonElement> PickAsync(HttpClient client, string session, JsonElement state, string label) =>
        StepAsync(client, session, "pick", state.GetProperty("options").EnumerateArray().Single(x => x.GetProperty("label").GetString() == label).GetProperty("token").GetString());
}
