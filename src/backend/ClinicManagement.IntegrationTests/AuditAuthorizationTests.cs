using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicManagement.IntegrationTests;

public class AuditAuthorizationTests : IntegrationTestBase
{
    public AuditAuthorizationTests(CustomWebApplicationFactory factory) : base(factory) { }

    private async Task<(Facility Facility, Department Department)> FacilityAsync(bool assigned)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var facility = new Facility { Code = $"A-{Guid.NewGuid():N}"[..18], Name = "Audit facility", IsActive = true };
        db.Facilities.Add(facility);
        await db.SaveChangesAsync();
        var dept = new Department { FacilityId = facility.Id, Code = $"D-{Guid.NewGuid():N}"[..18], Name = "Audit department", SpecialtyId = SpecialtyEntityId, IsActive = true };
        db.Departments.Add(dept);
        if (assigned)
        {
            foreach (var (user, role) in new[] { (ReceptionistId, "Receptionist"), (PharmacistId, "Pharmacist"), (TechnicianId, "DiagnosticTechnician"), (DoctorId, "Doctor") })
                db.StaffFacilityAssignments.Add(new StaffFacilityAssignment { UserId = user, FacilityId = facility.Id, Role = role, IsActive = true });
        }
        await db.SaveChangesAsync();
        return (facility, dept);
    }

    private async Task<PatientVisit> VisitAsync(long facilityId, long departmentId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var visit = new PatientVisit { VisitCode = $"V-{Guid.NewGuid():N}"[..18], PatientId = Patient1EntityId, FacilityId = facilityId, DepartmentId = departmentId, AssignedDoctorId = DoctorEntityId, VisitDate = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>().VietnamToday, QueueNumber = 1, Status = VisitStatus.ConsultationCompleted, CreatedByUserId = ReceptionistId, CheckedInAtUtc = DateTime.UtcNow, CreatedAtUtc = DateTime.UtcNow };
        db.PatientVisits.Add(visit);
        await db.SaveChangesAsync();
        return visit;
    }

    private async Task<Appointment> AppointmentAsync(long? facilityId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var appointment = new Appointment { AppointmentCode = $"A-{Guid.NewGuid():N}"[..18], FacilityId = facilityId, PatientId = Patient1EntityId, DoctorId = DoctorEntityId, SpecialtyId = SpecialtyEntityId, AppointmentSlotId = SlotEntityId, AppointmentDate = DateOnly.FromDateTime(DateTime.UtcNow), StartTime = new(8, 0), EndTime = new(8, 30), Status = AppointmentStatus.Completed };
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync();
        return appointment;
    }

    private async Task<Invoice> InvoiceAsync(long? visitId, long? appointmentId, bool paid = false)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var invoice = new Invoice { InvoiceCode = $"I-{Guid.NewGuid():N}"[..18], PatientId = Patient1EntityId, PatientVisitId = visitId, AppointmentId = appointmentId, Status = paid ? InvoiceStatus.Paid : InvoiceStatus.Unpaid, Subtotal = 2000, TotalAmount = 2000, CreatedByUserId = ReceptionistId, CreatedAtUtc = DateTime.UtcNow, PaidAtUtc = paid ? DateTime.UtcNow : null };
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();
        if (paid) db.Payments.Add(new Payment { InvoiceId = invoice.Id, PaymentCode = $"P-{Guid.NewGuid():N}"[..18], Amount = 2000, Status = PaymentStatus.Succeeded, Method = PaymentMethod.Cash, ReceivedAtUtc = DateTime.UtcNow, ReceivedByUserId = ReceptionistId });
        await db.SaveChangesAsync();
        return invoice;
    }

    private async Task<Prescription> PrescriptionAsync(long? visitId, long? appointmentId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rx = new Prescription { PatientId = Patient1EntityId, DoctorId = DoctorEntityId, PatientVisitId = visitId, AppointmentId = appointmentId, Status = PrescriptionStatus.Issued, CreatedAt = DateTime.UtcNow };
        rx.Items.Add(new PrescriptionItem { MedicineId = MedicineEntityId, Quantity = 1, Dosage = "1", Frequency = "1", DurationDays = 1 });
        db.Prescriptions.Add(rx);
        await db.SaveChangesAsync();
        return rx;
    }

    [Theory]
    [InlineData("rec@test.com")]
    [InlineData("pharm@test.com")]
    [InlineData("tech@test.com")]
    [InlineData("doc@test.com")]
    public async Task A1_Visit_reads_enforce_facility_and_admin_access(string email)
    {
        var own = await FacilityAsync(true);
        var other = await FacilityAsync(false);
        var ownVisit = await VisitAsync(own.Facility.Id, own.Department.Id);
        var otherVisit = await VisitAsync(other.Facility.Id, other.Department.Id);
        using var staff = await CreateAuthenticatedClientAsync(email);
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync($"/api/v1/patient-visits/{ownVisit.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync($"/api/v1/patient-visits/{otherVisit.Id}")).StatusCode);
        using var admin = await CreateAuthenticatedClientAsync("admin@test.com");
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/v1/patient-visits/{otherVisit.Id}")).StatusCode);
    }

    [Theory]
    [InlineData("ticket")]
    [InlineData("queue")]
    public async Task A1_Ticket_and_department_queue_enforce_facility(string operation)
    {
        var own = await FacilityAsync(true);
        var other = await FacilityAsync(false);
        var visit = await VisitAsync(other.Facility.Id, other.Department.Id);
        var ownVisit = await VisitAsync(own.Facility.Id, own.Department.Id);
        string Url(PatientVisit v) => operation == "ticket" ? $"/api/v1/patient-visits/{v.Id}/ticket" : $"/api/v1/patient-visits/department-queue?departmentId={v.DepartmentId}";
        using var rec = await CreateAuthenticatedClientAsync("rec@test.com");
        Assert.Equal(HttpStatusCode.OK, (await rec.GetAsync(Url(ownVisit))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await rec.GetAsync(Url(visit))).StatusCode);
        using var admin = await CreateAuthenticatedClientAsync("admin@test.com");
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(Url(visit))).StatusCode);
    }

    [Fact]
    public async Task A1_Doctor_cannot_read_another_assigned_doctors_visit()
    {
        var own = await FacilityAsync(true);
        var visit = await VisitAsync(own.Facility.Id, own.Department.Id);
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.PatientVisits.FindAsync(visit.Id))!.AssignedDoctorId = Doctor2EntityId;
            await db.SaveChangesAsync();
        }
        using var doctor = await CreateAuthenticatedClientAsync("doc@test.com");
        Assert.Equal(HttpStatusCode.NotFound, (await doctor.GetAsync($"/api/v1/patient-visits/{visit.Id}")).StatusCode);
    }

    [Theory]
    [InlineData("status")] [InlineData("assign")]
    public async Task A1_Callers_of_visit_detail_do_not_mutate_when_doctor_read_is_denied(string operation)
    {
        var own = await FacilityAsync(true);
        var visit = await VisitAsync(own.Facility.Id, own.Department.Id);
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.StaffFacilityAssignments.Add(new StaffFacilityAssignment { UserId = Doctor2UserId, FacilityId = own.Facility.Id, Role = "Doctor", IsActive = true });
            await db.SaveChangesAsync();
        }
        using var doctor = await CreateAuthenticatedClientAsync("doc@test.com");
        var response = operation == "assign"
            ? await doctor.PostAsJsonAsync($"/api/v1/patient-visits/{visit.Id}/assign-doctor", new { doctorId = Doctor2EntityId })
            : await doctor.PutAsync($"/api/v1/patient-visits/{visit.Id}/status?status=InBilling", null);
        if (operation == "status")
        {
            // A doctor assigned to this visit may update it; the denied case is another assigned doctor.
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var scope = Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = (await db.PatientVisits.FindAsync(visit.Id))!;
            row.AssignedDoctorId = Doctor2EntityId;
            await db.SaveChangesAsync();
            response = await doctor.PutAsync($"/api/v1/patient-visits/{visit.Id}/status?status=WaitingForDoctor", null);
        }
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var verify = Factory.Services.CreateScope();
        var saved = await verify.ServiceProvider.GetRequiredService<AppDbContext>().PatientVisits.FindAsync(visit.Id);
        Assert.Equal(operation == "status" ? VisitStatus.InBilling : VisitStatus.ConsultationCompleted, saved!.Status);
        Assert.Equal(operation == "status" ? Doctor2EntityId : DoctorEntityId, saved.AssignedDoctorId);
    }

    [Theory]
    [InlineData("detail", false)] [InlineData("detail", true)]
    [InlineData("pay", false)] [InlineData("pay", true)]
    [InlineData("cancel", false)] [InlineData("cancel", true)]
    public async Task A2_Invoice_operations_enforce_visit_or_appointment_facility(string operation, bool appointmentOnly)
    {
        foreach (var assigned in new[] { true, false })
        {
            var facility = await FacilityAsync(assigned);
            var visit = await VisitAsync(facility.Facility.Id, facility.Department.Id);
            var appointment = await AppointmentAsync(facility.Facility.Id);
            var invoice = await InvoiceAsync(appointmentOnly ? null : visit.Id, appointment.Id);
            using var rec = await CreateAuthenticatedClientAsync("rec@test.com");
            var response = await InvoiceOperationAsync(rec, invoice.Id, operation);
            Assert.Equal(assigned ? HttpStatusCode.OK : HttpStatusCode.Forbidden, response.StatusCode);
            if (!assigned)
            {
                using var admin = await CreateAuthenticatedClientAsync("admin@test.com");
                Assert.Equal(HttpStatusCode.OK, (await InvoiceOperationAsync(admin, invoice.Id, operation)).StatusCode);
            }
        }
    }

    private static Task<HttpResponseMessage> InvoiceOperationAsync(HttpClient client, long id, string operation) => operation switch
    {
        "pay" => client.PostAsJsonAsync($"/api/v1/reception/billing/invoices/{id}/pay", new { amount = 2000, method = (int)PaymentMethod.Cash }),
        "cancel" => client.PatchAsJsonAsync($"/api/v1/reception/billing/invoices/{id}/cancel", new { reason = "Audit cancellation" }),
        _ => client.GetAsync($"/api/v1/reception/billing/invoices/{id}")
    };

    [Theory]
    [InlineData("visit")] [InlineData("appointment")]
    public async Task A2_Invoice_creation_checks_source_facility(string source)
    {
        foreach (var assigned in new[] { true, false })
        {
            var facility = await FacilityAsync(assigned);
            var visit = await VisitAsync(facility.Facility.Id, facility.Department.Id);
            var appointment = await AppointmentAsync(facility.Facility.Id);
            object payload = source == "visit" ? new { patientVisitId = visit.Id } : new { appointmentId = appointment.Id };
            using var rec = await CreateAuthenticatedClientAsync("rec@test.com");
            var url = $"/api/v1/reception/billing/invoices/{source}";
            Assert.Equal(assigned ? HttpStatusCode.Created : HttpStatusCode.Forbidden, (await rec.PostAsJsonAsync(url, payload)).StatusCode);
            if (!assigned)
            {
                using var admin = await CreateAuthenticatedClientAsync("admin@test.com");
                Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync(url, payload)).StatusCode);
            }
        }
    }

    [Fact]
    public async Task A2_Lists_kpi_and_revenue_exclude_outside_invoices()
    {
        var own = await FacilityAsync(true);
        var other = await FacilityAsync(false);
        var ownAppointment = await AppointmentAsync(own.Facility.Id);
        var otherAppointment = await AppointmentAsync(other.Facility.Id);
        var inside = await InvoiceAsync(null, ownAppointment.Id, true);
        var outside = await InvoiceAsync(null, otherAppointment.Id, true);
        using var rec = await CreateAuthenticatedClientAsync("rec@test.com");
        var response = await rec.GetAsync("/api/v1/reception/billing/invoices?pageSize=100");
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var ids = json.RootElement.GetProperty("data").GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetInt64()).ToList();
        Assert.Contains(inside.Id, ids);
        Assert.DoesNotContain(outside.Id, ids);
        using var scope = Factory.Services.CreateScope();
        var accessor = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>();
        accessor.HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext { User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(new[] { new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, ReceptionistId.ToString()) }, "test")) };
        try
        {
            var service = scope.ServiceProvider.GetRequiredService<ClinicManagement.Application.Billing.Interfaces.IBillingService>();
            var kpi = await service.GetTodayKpiAsync();
            var report = await service.GetRevenueReportAsync(null, null);
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var permitted = await db.Payments.Where(p => p.Invoice.Appointment != null && p.Invoice.Appointment.FacilityId == own.Facility.Id).ToListAsync();
            Assert.True(kpi.TodayRevenue < (await db.Payments.ToListAsync()).Sum(p => p.Amount));
            Assert.True(report.TotalRevenue < (await db.Payments.ToListAsync()).Sum(p => p.Amount));
            Assert.True(kpi.TodayRevenue >= permitted.Sum(p => p.Amount));
        }
        finally { accessor.HttpContext = null; }
        using var admin = await CreateAuthenticatedClientAsync("admin@test.com");
        Assert.Contains(outside.InvoiceCode, await (await admin.GetAsync("/api/v1/reception/billing/invoices?pageSize=100")).Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A3_Invoice_guard_falls_back_to_appointment()
    {
        var outside = await FacilityAsync(false);
        var appointment = await AppointmentAsync(outside.Facility.Id);
        var invoice = await InvoiceAsync(null, appointment.Id);
        using var scope = Factory.Services.CreateScope();
        var auth = scope.ServiceProvider.GetRequiredService<IFacilityAuthorizationService>();
        await Assert.ThrowsAsync<ForbiddenException>(() => auth.ValidateInvoiceAccessAsync(ReceptionistId, invoice.Id));
        await auth.ValidateInvoiceAccessAsync(AdminId, invoice.Id);
    }

    [Theory]
    [InlineData("detail", false)] [InlineData("detail", true)]
    [InlineData("confirm-purchase", false)] [InlineData("confirm-purchase", true)]
    [InlineData("dispense", false)] [InlineData("dispense", true)]
    public async Task A4_Prescription_operations_enforce_facility(string operation, bool appointmentOnly)
    {
        foreach (var assigned in new[] { true, false })
        {
            var f = await FacilityAsync(assigned);
            var visit = await VisitAsync(f.Facility.Id, f.Department.Id);
            var appointment = await AppointmentAsync(f.Facility.Id);
            var rx = await PrescriptionAsync(appointmentOnly ? null : visit.Id, appointment.Id);
            using (var scope = Factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var med = (await db.Medicines.FindAsync(MedicineEntityId))!;
                med.StockQuantity = 100;
                var invoice = new Invoice { InvoiceCode = $"RX-{Guid.NewGuid():N}"[..18], PatientId = rx.PatientId, PatientVisitId = rx.PatientVisitId, AppointmentId = rx.AppointmentId, Status = InvoiceStatus.Paid, TotalAmount = 2000, Subtotal = 2000 };
                invoice.Items.Add(new InvoiceItem { ItemCode = med.Code, Description = med.Name, Quantity = 1, UnitPrice = 2000, LineTotal = 2000, ReferenceType = "PrescriptionItem:v2", ReferenceId = rx.Id * 4294967296L + MedicineEntityId });
                db.Invoices.Add(invoice);
                await db.SaveChangesAsync();
            }
            using var pharm = await CreateAuthenticatedClientAsync("pharm@test.com");
            var response = await RxOperationAsync(pharm, rx.Id, operation);
            Assert.Equal(assigned ? HttpStatusCode.OK : HttpStatusCode.Forbidden, response.StatusCode);
            if (!assigned)
            {
                using var admin = await CreateAuthenticatedClientAsync("admin@test.com");
                Assert.Equal(HttpStatusCode.OK, (await RxOperationAsync(admin, rx.Id, operation)).StatusCode);
            }
        }
    }

    private static Task<HttpResponseMessage> RxOperationAsync(HttpClient client, long id, string op) => op == "detail" ? client.GetAsync($"/api/v1/pharmacy/prescriptions/{id}") : client.PostAsync($"/api/v1/pharmacy/prescriptions/{id}/{op}", null);

    [Fact]
    public async Task A4_Prescription_list_filters_visit_and_appointment_sources()
    {
        var f = await FacilityAsync(false);
        var visit = await VisitAsync(f.Facility.Id, f.Department.Id);
        var appointment = await AppointmentAsync(f.Facility.Id);
        var rx = await PrescriptionAsync(visit.Id, null);
        var appointmentRx = await PrescriptionAsync(null, appointment.Id);
        using var staff = await CreateAuthenticatedClientAsync("pharm@test.com");
        var json = await (await staff.GetAsync("/api/v1/pharmacy/prescriptions?pageSize=100")).Content.ReadAsStringAsync();
        var ids = JsonDocument.Parse(json).RootElement.GetProperty("data").GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetInt64()).ToList();
        Assert.DoesNotContain(rx.Id, ids);
        Assert.DoesNotContain(appointmentRx.Id, ids);
        using var admin = await CreateAuthenticatedClientAsync("admin@test.com");
        var adminJson = JsonDocument.Parse(await (await admin.GetAsync("/api/v1/pharmacy/prescriptions?pageSize=100")).Content.ReadAsStringAsync());
        Assert.Contains(rx.Id, adminJson.RootElement.GetProperty("data").GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetInt64()));
    }

    [Theory]
    [InlineData("intake")] [InlineData("walk-in")]
    public async Task A5_Assigned_doctor_requires_active_doctor_facility_assignment(string endpoint)
    {
        var f = await FacilityAsync(true);
        using var rec = await CreateAuthenticatedClientAsync("rec@test.com");
        object Payload(long doctor) => endpoint == "intake" ? new { existingPatientId = Patient1EntityId, facilityId = f.Facility.Id, departmentId = f.Department.Id, assignedDoctorId = doctor, chiefComplaint = "Audit intake", idempotencyKey = Guid.NewGuid().ToString() } : new { existingPatientId = Patient1EntityId, facilityId = f.Facility.Id, departmentId = f.Department.Id, assignedDoctorId = doctor };
        var response = await rec.PostAsJsonAsync($"/api/v1/patient-visits/{endpoint}", Payload(Doctor2EntityId));
        Assert.Contains("FACILITY_SCOPE_DENIED", await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, (await rec.PostAsJsonAsync($"/api/v1/patient-visits/{endpoint}", Payload(DoctorEntityId))).StatusCode);
    }

    [Fact]
    public async Task B1_Cancelled_visit_invoice_can_be_billed_again()
    {
        var f = await FacilityAsync(true);
        var visit = await VisitAsync(f.Facility.Id, f.Department.Id);
        using var rec = await CreateAuthenticatedClientAsync("rec@test.com");
        var create = await rec.PostAsJsonAsync("/api/v1/reception/billing/invoices/visit", new { patientVisitId = visit.Id });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var invoiceId = JsonDocument.Parse(await create.Content.ReadAsStringAsync()).RootElement.GetProperty("data").GetProperty("id").GetInt64();
        Assert.Equal(HttpStatusCode.OK, (await InvoiceOperationAsync(rec, invoiceId, "cancel")).StatusCode);
        var unbilled = JsonDocument.Parse(await (await rec.GetAsync($"/api/v1/reception/billing/unbilled-visits?facilityId={f.Facility.Id}&pageSize=100")).Content.ReadAsStringAsync());
        Assert.Contains(visit.Id, unbilled.RootElement.GetProperty("data").GetProperty("items").EnumerateArray().Select(i => i.GetProperty("visitId").GetInt64()));
        Assert.Equal(HttpStatusCode.Created, (await rec.PostAsJsonAsync("/api/v1/reception/billing/invoices/visit", new { patientVisitId = visit.Id })).StatusCode);
    }

    [Fact]
    public async Task B1_Legacy_cancelled_rows_do_not_hide_unbilled_charges_or_lock_prescription_edits()
    {
        var f = await FacilityAsync(true);
        var visit = await VisitAsync(f.Facility.Id, f.Department.Id);
        var appointment = await AppointmentAsync(f.Facility.Id);
        var rx = await PrescriptionAsync(visit.Id, appointment.Id);
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var invoice = new Invoice { InvoiceCode = $"OLD-{Guid.NewGuid():N}"[..18], PatientId = Patient1EntityId, PatientVisitId = visit.Id, Status = InvoiceStatus.Cancelled, TotalAmount = 2000, Subtotal = 2000 };
            invoice.Items.Add(new InvoiceItem { ItemCode = "KHAM", Description = "Consultation", Quantity = 1, UnitPrice = 2000, LineTotal = 2000, ReferenceType = "Consultation", ReferenceId = visit.Id, IsCancelled = false });
            invoice.Items.Add(new InvoiceItem { ItemCode = "MED", Description = "Medicine", Quantity = 1, UnitPrice = 2000, LineTotal = 2000, ReferenceType = "PrescriptionItem:v2", ReferenceId = rx.Id * 4294967296L + MedicineEntityId, IsCancelled = false });
            db.Invoices.Add(invoice);
            await db.SaveChangesAsync();
        }
        using var rec = await CreateAuthenticatedClientAsync("rec@test.com");
        var stats = JsonDocument.Parse(await (await rec.GetAsync($"/api/v1/reception/stats?facilityId={f.Facility.Id}")).Content.ReadAsStringAsync());
        Assert.Equal(1, stats.RootElement.GetProperty("data").GetProperty("unbilledCount").GetInt32());
        // Exercise the canonical billing-lock guard with real persisted rows.
        using var verify = Factory.Services.CreateScope();
        var service = verify.ServiceProvider.GetRequiredService<ClinicManagement.Application.Appointments.Interfaces.IDoctorAppointmentService>();
        var method = service.GetType().GetMethod("ValidatePrescriptionNotBilledOrLockedAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        await (Task)method.Invoke(service, new object[] { rx })!;
        Assert.Equal(HttpStatusCode.Created, (await rec.PostAsJsonAsync("/api/v1/reception/billing/invoices/visit", new { patientVisitId = visit.Id })).StatusCode);
    }

    [Fact]
    public async Task B7_Twenty_concurrent_new_patient_intakes_have_unique_mrns()
    {
        var f = await FacilityAsync(true);
        using var rec = await CreateAuthenticatedClientAsync("rec@test.com");
        for (var round = 0; round < 10; round++)
        {
            var batch = Guid.NewGuid().ToString("N");
            var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(i => rec.PostAsJsonAsync("/api/v1/patient-visits/intake", new { facilityId = f.Facility.Id, departmentId = f.Department.Id, chiefComplaint = "Audit intake", idempotencyKey = $"{batch}-{i}", newPatient = new { fullName = $"Audit {batch} {i}", phoneNumber = "0912345678", dateOfBirth = "1990-01-01", gender = (int)Gender.Male } })));
            foreach (var response in responses) Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            using var scope = Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var patients = await db.Patients.Where(p => p.FullName != null && p.FullName.Contains(batch)).ToListAsync();
            Assert.Equal(20, patients.Count);
            Assert.Equal(20, patients.Select(p => p.MedicalRecordNumber).Distinct().Count());
        }
    }
}
