using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ClinicManagement.Application.Common.Constants;
using ClinicManagement.Application.Prescriptions.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Identity;
using ClinicManagement.Infrastructure.Persistence;
using ClinicManagement.Infrastructure.Prescriptions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ClinicManagement.IntegrationTests;

// Every case owns its patient, staff, medicines, specialty, facility and encounter.
// Fixed dates are scoped to fresh doctors/facilities; no shared seeded records are changed.
public class PrescriptionSafetyTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>, IDisposable
{
    private readonly HttpClient _client = factory.CreateClient();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private sealed record Actor(Guid Id, string Email);
    private sealed record Fixture(bool IsVisit, long EncounterId, long PatientId, Actor Doctor, Actor OtherDoctor, Actor Patient, Actor Pharmacist, long[] MedicineIds)
    {
        public string Route => $"/api/v1/doctor/{(IsVisit ? "visits" : "appointments")}/{EncounterId}";
    }

    private async Task<Fixture> SeedAsync(bool isVisit, params string?[] ingredients)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        async Task<Actor> User(string role)
        {
            var email = $"safety-{Guid.NewGuid():N}@test.com";
            var entity = new ApplicationUser { Id = Guid.NewGuid(), UserName = email, Email = email, FullName = "Safety " + role,
                PhoneNumber = "09" + Random.Shared.NextInt64(100000000, 999999999), IsActive = true };
            Assert.True((await manager.CreateAsync(entity, "Pass@123")).Succeeded);
            Assert.True((await manager.AddToRoleAsync(entity, role)).Succeeded);
            return new(entity.Id, email);
        }
        var doc = await User(RoleNames.Doctor); var otherDoc = await User(RoleNames.Doctor);
        var patientUser = await User(RoleNames.Patient); var pharmacist = await User(RoleNames.Pharmacist);
        var doctor = new Doctor { UserId = doc.Id, IsActive = true };
        var otherDoctor = new Doctor { UserId = otherDoc.Id, IsActive = true };
        var patient = new Patient { UserId = patientUser.Id, FullName = "Safety patient", MedicalRecordNumber = "PS-" + Guid.NewGuid().ToString("N")[..20] };
        var specialty = new Specialty { SpecialtyCode = "PS-" + Guid.NewGuid().ToString("N")[..12], Name = "Safety specialty", IsActive = true };
        var facility = new Facility { Code = "PS-" + Guid.NewGuid().ToString("N")[..20], Name = "Safety facility", IsActive = true };
        var department = new Department { Facility = facility, Specialty = specialty, Code = "PS-" + Guid.NewGuid().ToString("N")[..20], Name = "Safety department", IsActive = true };
        db.Doctors.AddRange(doctor, otherDoctor); db.Patients.Add(patient); db.Departments.Add(department);
        var medicines = ingredients.Select((ingredient, index) => new Medicine { Code = "PS-" + Guid.NewGuid().ToString("N"), Name = $"Thuốc an toàn {index}",
            Unit = "Viên", ActiveIngredient = ingredient, IsActive = true, StockQuantity = 100, UnitPrice = 1000m }).ToArray();
        db.Medicines.AddRange(medicines);
        await db.SaveChangesAsync();
        db.DoctorSpecialties.Add(new DoctorSpecialty { DoctorId = doctor.Id, SpecialtyId = specialty.Id, IsPrimary = true });
        foreach (var (actor, role) in new[] { (doc, RoleNames.Doctor), (otherDoc, RoleNames.Doctor), (pharmacist, RoleNames.Pharmacist) })
            db.StaffFacilityAssignments.Add(new StaffFacilityAssignment { UserId = actor.Id, FacilityId = facility.Id, DepartmentId = department.Id, Role = role, IsActive = true });
        long encounterId;
        var date = new DateOnly(2045, 4, 17);
        if (isVisit)
        {
            var visit = new PatientVisit { VisitCode = "PS-" + Guid.NewGuid().ToString("N")[..20], PatientId = patient.Id, AssignedDoctorId = doctor.Id,
                FacilityId = facility.Id, DepartmentId = department.Id, VisitDate = date, QueueNumber = 1, Status = VisitStatus.InConsultation, CreatedByUserId = doc.Id };
            db.PatientVisits.Add(visit); await db.SaveChangesAsync(); encounterId = visit.Id;
        }
        else
        {
            var appointment = new Appointment { AppointmentCode = "PS-" + Guid.NewGuid().ToString("N")[..20], PatientId = patient.Id, DoctorId = doctor.Id,
                SpecialtyId = specialty.Id, FacilityId = facility.Id, AppointmentDate = date, StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(9, 30),
                AppointmentSlot = new AppointmentSlot { DoctorId = doctor.Id, SlotDate = date, StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(9, 30), IsBooked = true }, Status = AppointmentStatus.InConsultation };
            db.Appointments.Add(appointment); await db.SaveChangesAsync(); encounterId = appointment.Id;
        }
        await LoginAsync(doc);
        return new(isVisit, encounterId, patient.Id, doc, otherDoc, patientUser, pharmacist, medicines.Select(m => m.Id).ToArray());
    }

    private async Task LoginAsync(Actor actor)
    {
        using var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { emailOrPhone = actor.Email, password = "Pass@123" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = await DataAsync(response);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", data.GetProperty("accessToken").GetString());
    }
    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    private static async Task<JsonElement> DataAsync(HttpResponseMessage response)
    {
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await JsonAsync(response)).GetProperty("data");
    }
    private async Task<PrescriptionSafetyResultDto> CheckAsync(Fixture f, params long[] ids) =>
        (await DataAsync(await _client.PostAsJsonAsync(f.Route + "/prescription/safety-check", new { medicineIds = ids.Length > 0 ? ids : f.MedicineIds }))).Deserialize<PrescriptionSafetyResultDto>(JsonOptions)!;
    private async Task AllergyAsync(Fixture f, string name, AllergenType type = AllergenType.Drug)
    {
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.PatientAllergies.Add(new PatientAllergy { PatientId = f.PatientId, AllergenName = name, AllergenType = type, Severity = AllergySeverity.Severe, ReactionDescription = "Nổi mề đay" });
        await db.SaveChangesAsync();
    }
    private static object[] Items(Fixture f) => f.MedicineIds.Select(id => (object)new { medicineId = id, quantity = 2, dosage = "1 viên", frequency = "Theo chỉ định" }).ToArray();
    private Task<HttpResponseMessage> CompleteAsync(Fixture f, string? reason = null, bool issue = true, bool includeItems = true) =>
        _client.PostAsJsonAsync(f.Route + "/complete", new { diagnosis = "Chẩn đoán kiểm thử", summary = "Kết luận kiểm thử", issuePrescription = issue,
            prescriptionItems = includeItems ? Items(f) : null, prescriptionSafetyOverrideReason = reason });

    [Fact]
    public async Task SeedRulesHaveStableNormalizedKeysAndOrderedUniquePairs()
    {
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(6, await db.DrugClassMembers.CountAsync(c => c.Id >= 1 && c.Id <= 6));
        Assert.True(await db.DrugAllergyCrossReactions.AnyAsync(c => c.Id == 1 && c.AllergenClassKey == "penicillin" && c.RelatedClassKey == "cephalosporin"));
        var rules = await db.DrugInteractionRules.Where(r => r.Id >= 1 && r.Id <= 9).ToListAsync();
        Assert.Equal(9, rules.Count);
        Assert.All(rules, rule => { Assert.True(string.CompareOrdinal(rule.IngredientKeyA, rule.IngredientKeyB) < 0); Assert.True(rule.IsActive); });
        Assert.Equal(9, rules.Select(r => (r.IngredientKeyA, r.IngredientKeyB)).Distinct().Count());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task PenicillinAllergyProducesDangerAndCrossAllergyWarning(bool isVisit)
    {
        var f = await SeedAsync(isVisit, "Amoxicillin trihydrat", "Cefixime");
        await AllergyAsync(f, "  Penicillin ");
        var safety = await CheckAsync(f);
        Assert.Single(safety.Allergies);
        var direct = Assert.Single(safety.Warnings, w => w.Type == "Allergy");
        Assert.Equal("Danger", direct.Level); Assert.Equal(new[] { f.MedicineIds[0] }, direct.MedicineIds); Assert.Contains("nặng", direct.Detail); Assert.Contains("Nổi mề đay", direct.Detail);
        var cross = Assert.Single(safety.Warnings, w => w.Type == "CrossAllergy");
        Assert.Equal("Warning", cross.Level); Assert.Contains("beta-lactam", cross.Detail); Assert.Equal(new[] { f.MedicineIds[1] }, cross.MedicineIds);
        Assert.Equal("Danger", safety.Warnings[0].Level);
    }

    [Theory]
    [InlineData("ibuprofen", AllergenType.Drug, true)]
    [InlineData("Thuốc chống viêm không steroid", AllergenType.Drug, true)]
    [InlineData("ibuprofen", AllergenType.Food, false)]
    [InlineData("ibu", AllergenType.Drug, false)]
    public async Task IngredientAndVietnameseClassAllergiesMatchButFoodAndPartialWordsDoNot(string allergen, AllergenType type, bool danger)
    {
        var f = await SeedAsync(false, "Ibuprofen"); await AllergyAsync(f, allergen, type);
        var safety = await CheckAsync(f);
        Assert.Single(safety.Allergies); Assert.Equal(danger, safety.Warnings.Any(w => w.Level == "Danger"));
    }

    [Theory]
    [InlineData("Ciprofloxacin", "Domperidone", "Danger")]
    [InlineData("Azithromycin", "Domperidone", "Danger")]
    [InlineData("Ibuprofen", "Losartan kali", "Warning")]
    [InlineData("Atorvastatin", "Azithromycin", "Info")]
    [InlineData("Ciprofloxacin", "Calci carbonat", "Warning")]
    [InlineData("Ciprofloxacin", "Nhôm phosphat", "Warning")]
    [InlineData("Ibuprofen", "Prednisolone", "Warning")]
    [InlineData("Ciprofloxacin", "Gliclazide", "Warning")]
    [InlineData("Metformin", "Prednisolone", "Warning")]
    public async Task SeededInteractionsRespectSeverityAndSaltPrefixes(string first, string second, string level)
    {
        var f = await SeedAsync(false, first, second);
        var warning = Assert.Single((await CheckAsync(f)).Warnings, w => w.Type == "Interaction");
        Assert.Equal(level, warning.Level); Assert.Equal(f.MedicineIds.Order(), warning.MedicineIds);
    }

    [Fact]
    public async Task DuplicateIngredientsClassesAndMissingIngredientsAreDeduplicatedAndSorted()
    {
        var f = await SeedAsync(false, "Ibuprofen + IBUPROFEN", "Ibuprofen", "Naproxen", null, "Ciprofloxacin", "Domperidone");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.DrugClassMembers.Add(new DrugClassMember { ClassKey = "nsaid", ClassName = "Thuốc chống viêm không steroid", IngredientKey = "naproxen" });
            await db.SaveChangesAsync();
        }
        var safety = await CheckAsync(f, f.MedicineIds.Concat(f.MedicineIds).ToArray());
        Assert.Single(safety.Warnings, w => w.Type == "DuplicateIngredient");
        Assert.Equal(2, safety.Warnings.Count(w => w.Type == "DuplicateClass"));
        var missing = Assert.Single(safety.Warnings, w => w.Type == "MissingIngredient");
        Assert.Equal("Info", missing.Level); Assert.Equal("Không có hoạt chất để kiểm tra", missing.Title);
        Assert.Equal("Danger", safety.Warnings.First().Level); Assert.Equal("Info", safety.Warnings.Last().Level);
        Assert.Equal(safety.Warnings.Count, safety.Warnings.Select(w => $"{w.Type}|{string.Join(',', w.MedicineIds)}|{w.Detail}").Distinct().Count());
    }

    [Fact]
    public async Task InactiveRulesAreIgnoredAndContraindicatedRulesAreDanger()
    {
        var f = await SeedAsync(false, "ps alpha", "ps beta", "ps gamma");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.DrugInteractionRules.AddRange(new DrugInteractionRule { IngredientKeyA = "ps alpha", IngredientKeyB = "ps beta", Severity = DrugInteractionSeverity.Contraindicated, Description = "Chống chỉ định kiểm thử", Recommendation = "Không phối hợp", IsActive = true },
                new DrugInteractionRule { IngredientKeyA = "ps alpha", IngredientKeyB = "ps gamma", Severity = DrugInteractionSeverity.Major, Description = "Rule không hoạt động", Recommendation = "Bỏ qua", IsActive = false });
            await db.SaveChangesAsync();
        }
        var warning = Assert.Single((await CheckAsync(f)).Warnings);
        Assert.Equal("Danger", warning.Level); Assert.Equal(f.MedicineIds.Take(2), warning.MedicineIds);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task DraftSaveAndGetRecalculateDangerWithoutBlocking(bool isVisit)
    {
        var f = await SeedAsync(isVisit, "Amoxicillin"); await AllergyAsync(f, "Penicillin");
        var draft = await DataAsync(await _client.PutAsJsonAsync(f.Route + "/prescription-draft", new { items = Items(f) }));
        Assert.Equal("Draft", draft.GetProperty("status").GetString());
        Assert.Equal("Danger", Assert.Single(draft.GetProperty("safetyWarnings").EnumerateArray()).GetProperty("level").GetString());
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.PatientAllergies.RemoveRange(await db.PatientAllergies.Where(a => a.PatientId == f.PatientId).ToListAsync()); await db.SaveChangesAsync();
        }
        var fresh = await DataAsync(await _client.GetAsync(f.Route + "/prescription-draft"));
        Assert.Empty(fresh.GetProperty("safetyWarnings").EnumerateArray());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task IssuanceRequiresTrimmedReasonSnapshotsWarningsAndAuditsOverride(bool isVisit)
    {
        var f = await SeedAsync(isVisit, "Amoxicillin"); await AllergyAsync(f, "Penicillin");
        foreach (var reason in new string?[] { null, "   ", "  123456789  " })
        {
            var denied = await CompleteAsync(f, reason);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, denied.StatusCode);
            Assert.Equal("PRESCRIPTION_SAFETY_OVERRIDE_REQUIRED", (await JsonAsync(denied)).GetProperty("errorCode").GetString());
        }
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.False(await db.Prescriptions.AnyAsync(p => p.PatientId == f.PatientId));
            if (isVisit) Assert.Equal(VisitStatus.InConsultation, (await db.PatientVisits.FindAsync(f.EncounterId))!.Status);
            else Assert.Equal(AppointmentStatus.InConsultation, (await db.Appointments.FindAsync(f.EncounterId))!.Status);
        }
        var completed = await CompleteAsync(f, "  Đã đánh giá và xác nhận chuyên môn  ");
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        using var verify = factory.Services.CreateScope(); var context = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        var prescription = await context.Prescriptions.SingleAsync(p => p.PatientId == f.PatientId);
        Assert.Equal(PrescriptionStatus.Issued, prescription.Status); Assert.Equal("Đã đánh giá và xác nhận chuyên môn", prescription.SafetyOverrideReason);
        Assert.Equal(f.Doctor.Id, prescription.SafetyAcknowledgedByUserId); Assert.Equal(DateTimeKind.Utc, prescription.SafetyAcknowledgedAtUtc!.Value.Kind);
        var snapshot = JsonSerializer.Deserialize<List<PrescriptionSafetyWarningDto>>(prescription.SafetyWarningsSnapshot!, JsonOptions)!;
        Assert.Equal("Danger", Assert.Single(snapshot).Level);
        Assert.True(await context.SystemAuditLogs.AnyAsync(a => a.Action == "PrescriptionSafetyOverridden" && a.EntityName == "Prescription" && a.EntityId == prescription.Id.ToString() && a.UserId == f.Doctor.Id));
        // The pharmacy must retain issuance-time evidence even if catalog data changes later.
        (await context.Medicines.FindAsync(f.MedicineIds[0]))!.ActiveIngredient = "Paracetamol";
        await context.SaveChangesAsync();
        await LoginAsync(f.Pharmacist);
        var pharmacy = await DataAsync(await _client.GetAsync($"/api/v1/pharmacy/prescriptions/{prescription.Id}"));
        Assert.Equal(prescription.SafetyOverrideReason, pharmacy.GetProperty("safetyOverrideReason").GetString());
        Assert.Equal("Danger", Assert.Single(pharmacy.GetProperty("safetyWarnings").EnumerateArray()).GetProperty("level").GetString());
        await LoginAsync(f.Patient);
        var patient = await DataAsync(await _client.GetAsync("/api/v1/patients/me/prescriptions"));
        var own = Assert.Single(patient.EnumerateArray(), p => p.GetProperty("id").GetInt64() == prescription.Id);
        Assert.DoesNotContain(own.EnumerateObject(), property => property.Name.StartsWith("safety", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.GetAsync($"/api/v1/pharmacy/prescriptions/{prescription.Id}")).StatusCode);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task NoWarningPrescriptionsCompleteWithoutReasonOrSnapshot(bool isVisit)
    {
        var f = await SeedAsync(isVisit, "Paracetamol");
        Assert.Empty((await CheckAsync(f)).Warnings);
        Assert.Equal(HttpStatusCode.OK, (await CompleteAsync(f)).StatusCode);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var prescription = await db.Prescriptions.SingleAsync(p => p.PatientId == f.PatientId);
        Assert.Null(prescription.SafetyWarningsSnapshot); Assert.Null(prescription.SafetyOverrideReason); Assert.Null(prescription.SafetyAcknowledgedByUserId); Assert.Null(prescription.SafetyAcknowledgedAtUtc);
        Assert.False(await db.SystemAuditLogs.AnyAsync(a => a.Action == "PrescriptionSafetyOverridden" && a.EntityId == prescription.Id.ToString()));
    }

    [Theory]
    [InlineData("Ibuprofen", "Losartan kali")]
    [InlineData("Atorvastatin", "Azithromycin")]
    [InlineData(null, "Paracetamol")]
    public async Task NonDangerWarningsAreSnapshottedWithoutOverrideOrAudit(string? first, string second)
    {
        var f = await SeedAsync(false, first, second);
        Assert.Equal(HttpStatusCode.OK, (await CompleteAsync(f)).StatusCode);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var prescription = await db.Prescriptions.SingleAsync(p => p.PatientId == f.PatientId);
        Assert.NotNull(prescription.SafetyWarningsSnapshot); Assert.Null(prescription.SafetyOverrideReason); Assert.Null(prescription.SafetyAcknowledgedAtUtc);
        Assert.False(await db.SystemAuditLogs.AnyAsync(a => a.Action == "PrescriptionSafetyOverridden" && a.EntityId == prescription.Id.ToString()));
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public async Task ExistingDraftIssuanceCannotBypassSafetyWhenItemsAreOmitted(bool isVisit, bool issue)
    {
        var f = await SeedAsync(isVisit, "Amoxicillin"); await AllergyAsync(f, "Penicillin");
        await DataAsync(await _client.PutAsJsonAsync(f.Route + "/prescription-draft", new { items = Items(f) }));
        var denied = await CompleteAsync(f, issue: issue, includeItems: false);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, denied.StatusCode);
        Assert.Equal("PRESCRIPTION_SAFETY_OVERRIDE_REQUIRED", (await JsonAsync(denied)).GetProperty("errorCode").GetString());
        Assert.Equal(HttpStatusCode.OK, (await CompleteAsync(f, "Đã xác nhận lý do vẫn kê", issue: issue, includeItems: false)).StatusCode);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task OtherDoctorsAndPatientsCannotUseAnotherDoctorsSafetyEndpoint(bool isVisit)
    {
        var f = await SeedAsync(isVisit, "Amoxicillin");
        await LoginAsync(f.OtherDoctor);
        var denied = await _client.PostAsJsonAsync(f.Route + "/prescription/safety-check", new { medicineIds = f.MedicineIds });
        Assert.True(denied.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden);
        await LoginAsync(f.Patient);
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.PostAsJsonAsync(f.Route + "/prescription/safety-check", new { medicineIds = f.MedicineIds })).StatusCode);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task IssuanceRechecksNewAllergiesInsteadOfTrustingClientWarnings(bool isVisit)
    {
        var f = await SeedAsync(isVisit, "Amoxicillin");
        Assert.Empty((await CheckAsync(f)).Warnings);
        await AllergyAsync(f, "Penicillin");
        var response = await _client.PostAsJsonAsync(f.Route + "/complete", new { diagnosis = "Chẩn đoán kiểm thử", summary = "Kết luận kiểm thử",
            issuePrescription = true, prescriptionItems = Items(f), safetyWarnings = Array.Empty<object>() });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("PRESCRIPTION_SAFETY_OVERRIDE_REQUIRED", (await JsonAsync(response)).GetProperty("errorCode").GetString());
    }

    private sealed class QueryCounter : DbCommandInterceptor
    {
        public int Count { get; private set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { Count++; return base.ReaderExecutingAsync(command, eventData, result, cancellationToken); }
    }

    [Fact]
    public async Task SafetyChecksUseFiveBatchedQueriesForManyMedicines()
    {
        var f = await SeedAsync(false, "Paracetamol", "Ibuprofen", "Losartan kali", "Ciprofloxacin", "Domperidone", "Amoxicillin");
        using var scope = factory.Services.CreateScope(); var source = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var counter = new QueryCounter();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(source.Database.GetConnectionString()).AddInterceptors(counter).Options);
        await new PrescriptionSafetyService(db).CheckAsync(f.PatientId, f.MedicineIds);
        Assert.Equal(5, counter.Count);
    }

    public void Dispose() => _client.Dispose();
}
