using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using ClinicManagement.Application.Authentication.Interfaces;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Diagnostics.DTOs;
using ClinicManagement.Application.Doctors.Interfaces;
using ClinicManagement.Application.Mpi.DTOs;
using ClinicManagement.Application.Mpi.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Common;
using ClinicManagement.Infrastructure.Diagnostics;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit.Abstractions;

namespace ClinicManagement.IntegrationTests;

public class AuditRound4Tests : IntegrationTestBase
{
    private readonly ITestOutputHelper _output;
    public AuditRound4Tests(CustomWebApplicationFactory factory, ITestOutputHelper output) : base(factory) => _output = output;
    private static string Code() => "R4-" + Guid.NewGuid().ToString("N")[..14];

    private async Task<PatientVisit> VisitAsync(AppDbContext db, long? patientId = null, long? facilityId = null)
    {
        var department = facilityId.HasValue
            ? await db.Departments.FirstAsync(x => x.FacilityId == facilityId)
            : await db.Departments.FirstAsync();
        var visit = new PatientVisit { VisitCode = Code(), PatientId = patientId ?? Patient1EntityId,
            FacilityId = department.FacilityId, DepartmentId = department.Id, AssignedDoctorId = DoctorEntityId,
            VisitDate = DateOnly.FromDateTime(DateTime.UtcNow), QueueNumber = TestQueueNumbers.Next(),
            Status = VisitStatus.ConsultationCompleted, CreatedByUserId = ReceptionistId };
        db.PatientVisits.Add(visit);
        await db.SaveChangesAsync();
        return visit;
    }

    private async Task<(Prescription Rx, Medicine Medicine)> PrescriptionAsync(AppDbContext db, PatientVisit visit)
    {
        var medicine = new Medicine { Code = Code(), Name = "Thuốc R4", Unit = "Viên", StockQuantity = 100, UnitPrice = 1000, IsActive = true };
        var rx = new Prescription { PatientVisitId = visit.Id, PatientId = visit.PatientId, DoctorId = DoctorEntityId, Status = PrescriptionStatus.Issued };
        rx.Items.Add(new PrescriptionItem { Medicine = medicine, Quantity = 7, Dosage = "1 viên", Frequency = "Hằng ngày", DurationDays = 7 });
        db.Prescriptions.Add(rx);
        await db.SaveChangesAsync();
        return (rx, medicine);
    }

    private async Task<Invoice> InvoiceAsync(AppDbContext db, Prescription rx, Medicine medicine, bool legacy = false)
    {
        var invoice = new Invoice { InvoiceCode = Code(), PatientId = rx.PatientId, PatientVisitId = rx.PatientVisitId,
            Status = InvoiceStatus.Unpaid, Subtotal = 7000, TotalAmount = 7000 };
        invoice.Items.Add(new InvoiceItem { ItemCode = medicine.Code, Description = medicine.Name, Quantity = 7, UnitPrice = 1000, LineTotal = 7000,
            ReferenceType = legacy ? PrescriptionItemBillingReference.LegacyReferenceType : PrescriptionItemBillingReference.ModernReferenceType,
            ReferenceId = legacy ? rx.Id * 100000 + medicine.Id : PrescriptionItemBillingReference.Encode(rx.Id, medicine.Id) });
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();
        return invoice;
    }

    private static async Task OkAsync(HttpResponseMessage response) => Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    private static async Task<JsonElement> DataAsync(HttpResponseMessage response)
    {
        await OkAsync(response);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
    }
    private static Task<HttpResponseMessage> CancelAsync(HttpClient client, long invoiceId) =>
        client.PatchAsJsonAsync($"/api/v1/reception/billing/invoices/{invoiceId}/cancel", new { reason = "Hủy kiểm thử R4" });

    [Fact]
    public async Task F1_Receptionist_can_confirm_only_in_own_facility_and_other_actions_remain_forbidden()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ownFacility = await db.StaffFacilityAssignments.Where(x => x.UserId == ReceptionistId && x.IsActive).Select(x => x.FacilityId).FirstAsync();
        var (rx, _) = await PrescriptionAsync(db, await VisitAsync(db, facilityId: ownFacility));
        var other = new Facility { Code = Code(), Name = "Cơ sở khác", IsActive = true };
        var department = new Department { Facility = other, Code = Code(), Name = "Khoa khác", SpecialtyId = SpecialtyEntityId, IsActive = true };
        db.Departments.Add(department);
        await db.SaveChangesAsync();
        var (otherRx, _) = await PrescriptionAsync(db, await VisitAsync(db, facilityId: other.Id));
        using var rec = await CreateAuthenticatedClientAsync("rec@test.com");
        Assert.Equal(HttpStatusCode.OK, (await rec.PostAsync($"/api/v1/pharmacy/prescriptions/{rx.Id}/confirm-purchase", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await rec.PostAsync($"/api/v1/pharmacy/prescriptions/{otherRx.Id}/confirm-purchase", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await rec.PostAsync($"/api/v1/pharmacy/prescriptions/{rx.Id}/dispense", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await rec.PostAsJsonAsync("/api/v1/pharmacy/inventory/adjust", new { medicineId = MedicineEntityId, quantity = 1, type = 2 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await rec.GetAsync("/api/v1/pharmacy/dashboard")).StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task F2_Cancellation_releases_reservation_then_reinvoice_pay_dispense_deducts_once(bool legacy)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var visit = await VisitAsync(db);
        var (rx, medicine) = await PrescriptionAsync(db, visit);
        var invoice = await InvoiceAsync(db, rx, medicine, legacy);
        using var pharm = await CreateAuthenticatedClientAsync("pharm@test.com");
        using var rec = await CreateAuthenticatedClientAsync("rec@test.com");
        await OkAsync(await pharm.PostAsync($"/api/v1/pharmacy/prescriptions/{rx.Id}/confirm-purchase", null));
        await db.Entry(medicine).ReloadAsync();
        Assert.Equal(93, medicine.StockQuantity);
        await OkAsync(await CancelAsync(rec, invoice.Id));
        await db.Entry(medicine).ReloadAsync();
        await db.Entry(rx).ReloadAsync();
        await db.Entry(visit).ReloadAsync();
        Assert.Equal(100, medicine.StockQuantity);
        Assert.Equal(PrescriptionStatus.Issued, rx.Status);
        Assert.Equal(VisitStatus.InBilling, visit.Status);
        var release = await db.MedicineStockTransactions.SingleAsync(x => x.PrescriptionId == rx.Id && x.Type == MedicineStockTransactionType.ReservationCancelled);
        Assert.Equal(7, release.QuantityChange);
        Assert.Equal(100, release.BalanceAfter);
        Assert.Equal(ReceptionistId, release.ActorUserId);
        Assert.Equal($"Trả thuốc giữ chỗ do hủy hóa đơn #{invoice.InvoiceCode}", release.Reason);
        var newInvoice = await DataAsync(await rec.PostAsJsonAsync("/api/v1/reception/billing/invoices/visit", new { patientVisitId = visit.Id }));
        var newId = newInvoice.GetProperty("id").GetInt64();
        await OkAsync(await rec.PostAsJsonAsync($"/api/v1/reception/billing/invoices/{newId}/pay", new { amount = newInvoice.GetProperty("totalAmount").GetDecimal(), method = 1 }));
        var paidCancel = await CancelAsync(rec, newId);
        Assert.Contains("CANNOT_CANCEL_PAID", await paidCancel.Content.ReadAsStringAsync());
        await OkAsync(await pharm.PostAsync($"/api/v1/pharmacy/prescriptions/{rx.Id}/dispense", null));
        await db.Entry(medicine).ReloadAsync();
        Assert.Equal(93, medicine.StockQuantity);
        Assert.Equal(-7, (await db.MedicineStockTransactions.SingleAsync(x => x.PrescriptionId == rx.Id && x.Type == MedicineStockTransactionType.Dispense)).QuantityChange);
    }

    [Fact]
    public async Task F2_Other_effective_invoice_keeps_reservation_until_last_cancel_and_purchase_can_reserve_again()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (rx, med) = await PrescriptionAsync(db, await VisitAsync(db));
        var first = await InvoiceAsync(db, rx, med);
        var second = await InvoiceAsync(db, rx, med, true);
        using var pharm = await CreateAuthenticatedClientAsync("pharm@test.com");
        using var rec = await CreateAuthenticatedClientAsync("rec@test.com");
        await OkAsync(await pharm.PostAsync($"/api/v1/pharmacy/prescriptions/{rx.Id}/confirm-purchase", null));
        await OkAsync(await CancelAsync(rec, first.Id));
        await db.Entry(med).ReloadAsync();
        await db.Entry(rx).ReloadAsync();
        Assert.Equal(93, med.StockQuantity);
        Assert.Equal(PrescriptionStatus.ReservedForPurchase, rx.Status);
        Assert.False(await db.MedicineStockTransactions.AnyAsync(x => x.PrescriptionId == rx.Id && x.Type == MedicineStockTransactionType.ReservationCancelled));
        await OkAsync(await CancelAsync(rec, second.Id));
        await db.Entry(med).ReloadAsync();
        Assert.Equal(100, med.StockQuantity);
        await OkAsync(await pharm.PostAsync($"/api/v1/pharmacy/prescriptions/{rx.Id}/confirm-purchase", null));
        await db.Entry(med).ReloadAsync();
        Assert.Equal(93, med.StockQuantity);
        Assert.Equal(2, await db.MedicineStockTransactions.CountAsync(x => x.PrescriptionId == rx.Id && x.Type == MedicineStockTransactionType.Reservation));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task F3_Allergy_delete_requires_doctor_relationship_but_preserves_receptionist_and_admin(bool appointmentLink)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var patient = new Patient { FullName = "Ngoài phạm vi R4", MedicalRecordNumber = Code() };
        var allergy = new PatientAllergy { Patient = patient, AllergenName = "R4", AllergenType = AllergenType.Drug };
        db.PatientAllergies.Add(allergy);
        await db.SaveChangesAsync();
        using var doctor = await CreateAuthenticatedClientAsync("doc@test.com");
        var route = $"/api/v1/mpi/patients/{patient.Id}/allergies/{allergy.Id}";
        Assert.Equal(HttpStatusCode.NotFound, (await doctor.DeleteAsync(route)).StatusCode);
        Assert.True(await db.PatientAllergies.AnyAsync(x => x.Id == allergy.Id));
        if (appointmentLink) await AppointmentAsync(db, patient.Id);
        else await VisitAsync(db, patient.Id);
        await OkAsync(await doctor.DeleteAsync(route));
        foreach (var email in new[] { "rec@test.com", "admin@test.com" })
        {
            var extra = new PatientAllergy { PatientId = patient.Id, AllergenName = Code(), AllergenType = AllergenType.Drug };
            db.PatientAllergies.Add(extra);
            await db.SaveChangesAsync();
            using var client = await CreateAuthenticatedClientAsync(email);
            await OkAsync(await client.DeleteAsync($"/api/v1/mpi/patients/{patient.Id}/allergies/{extra.Id}"));
        }
    }

    [Fact]
    public async Task F4_Mpi_walkin_persists_guardian_and_returns_it_in_id_mrn_and_search()
    {
        using var rec = await CreateAuthenticatedClientAsync("rec@test.com");
        var created = await DataAsync(await rec.PostAsJsonAsync("/api/v1/mpi/patients/walk-in", new { fullName = Code(), emergencyContact = new { fullName = "Mẹ", relationship = "Mẹ", phoneNumber = "0901234567", isGuardian = true } }));
        var id = created.GetProperty("id").GetInt64();
        var mrn = created.GetProperty("medicalRecordNumber").GetString();
        foreach (var route in new[] { $"/api/v1/mpi/patients/{id}", $"/api/v1/mpi/patients/by-mrn/{mrn}" })
            Assert.True((await DataAsync(await rec.GetAsync(route))).GetProperty("emergencyContacts")[0].GetProperty("isGuardian").GetBoolean());
        var search = await DataAsync(await rec.GetAsync($"/api/v1/mpi/patients?medicalRecordNumber={mrn}"));
        Assert.True(search.GetProperty("items")[0].GetProperty("emergencyContacts")[0].GetProperty("isGuardian").GetBoolean());
        using var scope = Factory.Services.CreateScope();
        Assert.True((await scope.ServiceProvider.GetRequiredService<AppDbContext>().EmergencyContacts.SingleAsync(x => x.PatientId == id)).IsGuardian);
    }

    [Theory]
    [InlineData("PhoneNumber", null)]
    [InlineData("PhoneNumber", "  ")]
    [InlineData("Relationship", null)]
    [InlineData("Relationship", "  ")]
    public async Task F4_Incomplete_emergency_contact_has_field_validation_and_creates_no_patient(string field, string? value)
    {
        var name = Code();
        var contact = new EmergencyContactDto { FullName = "Người liên hệ", Relationship = "Mẹ", PhoneNumber = "0901234567" };
        if (field == "PhoneNumber") contact.PhoneNumber = value!; else contact.Relationship = value!;
        using var scope = Factory.Services.CreateScope();
        var request = new RegisterWalkInPatientRequest { FullName = name, EmergencyContact = contact };
        var error = await Assert.ThrowsAsync<ValidationException>(() => scope.ServiceProvider.GetRequiredService<IMpiPatientService>().RegisterWalkInPatientAsync(request));
        Assert.Contains("người liên hệ", Assert.Single(error.Errors[$"EmergencyContact.{field}"]));
        using var rec = await CreateAuthenticatedClientAsync("rec@test.com");
        var response = await rec.PostAsJsonAsync("/api/v1/mpi/patients/walk-in", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(field, await response.Content.ReadAsStringAsync());
        Assert.False(await scope.ServiceProvider.GetRequiredService<AppDbContext>().Patients.AnyAsync(x => x.FullName == name));
    }

    private async Task<Appointment> AppointmentAsync(AppDbContext db, long? patientId = null)
    {
        var appointment = new Appointment { AppointmentCode = Code(), PatientId = patientId ?? Patient1EntityId, DoctorId = DoctorEntityId,
            SpecialtyId = SpecialtyEntityId, AppointmentSlotId = SlotEntityId, AppointmentDate = DateOnly.FromDateTime(DateTime.UtcNow),
            StartTime = new(8, 0), EndTime = new(8, 30), Status = AppointmentStatus.Completed };
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync();
        return appointment;
    }

    [Theory]
    [InlineData("  Bận công việc  ")]
    [InlineData(null)]
    [InlineData("long")]
    public async Task F5_Revisit_rejection_notifies_doctor_once_with_trimmed_bounded_reason_and_checks_patient(string? reason)
    {
        if (reason == "long") reason = new string('a', 700);
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var appointment = await AppointmentAsync(db);
        var request = new RevisitRequest { AppointmentId = appointment.Id, PatientId = Patient1EntityId, DoctorId = DoctorEntityId, SuggestedDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7) };
        db.RevisitRequests.Add(request);
        await db.SaveChangesAsync();
        var route = $"/api/v1/revisit-requests/{request.Id}/reject";
        using var other = await CreateAuthenticatedClientAsync("pat2@test.com");
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync(route, new { reason })).StatusCode);
        using var patient = await CreateAuthenticatedClientAsync("pat1@test.com");
        await OkAsync(await patient.PostAsJsonAsync(route, new { reason }));
        var notification = await db.Notifications.SingleAsync(x => x.DedupeKey == $"revisit_rejected_{request.Id}");
        Assert.Equal(DoctorId, notification.UserId);
        Assert.Equal(NotificationType.Appointment, notification.Type);
        Assert.Equal("Bệnh nhân từ chối đề xuất tái khám", notification.Title);
        Assert.Equal("RevisitRequest", notification.RelatedEntityType);
        Assert.Equal($"/doctor/appointments/{appointment.Id}", notification.Route);
        Assert.Contains(appointment.AppointmentCode, notification.Message);
        Assert.Contains(reason == null ? "Không nêu lý do" : reason.Trim()[..Math.Min(reason.Trim().Length, 20)], notification.Message);
        Assert.InRange(notification.Message.Length, 1, 500);
        Assert.Contains("INVALID_STATE", await (await patient.PostAsJsonAsync(route, new { reason })).Content.ReadAsStringAsync());
        Assert.Equal(1, await db.Notifications.CountAsync(x => x.DedupeKey == $"revisit_rejected_{request.Id}"));
    }

    [Fact]
    public async Task F6_Technician_today_uses_provider_utc_boundaries_at_vietnam_midnight()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var visit = await VisitAsync(db);
        var today = new DateOnly(2035, 1, 2);
        var start = new DateTime(2035, 1, 1, 17, 0, 0, DateTimeKind.Utc);
        foreach (var time in new[] { start.AddMinutes(-30), start, start.AddMinutes(30), start.AddDays(1) })
            db.DiagnosticOrders.Add(new DiagnosticOrder { OrderCode = Code(), PatientVisitId = visit.Id, PatientId = visit.PatientId, OrderingDoctorId = DoctorEntityId,
                FacilityId = visit.FacilityId, Status = DiagnosticOrderStatus.Completed, CompletedAtUtc = time });
        await db.SaveChangesAsync();
        var clock = new Mock<IDateTimeProvider>();
        clock.SetupGet(x => x.VietnamToday).Returns(today);
        clock.Setup(x => x.ConvertVietnamToUtc(It.IsAny<DateTime>())).Returns((DateTime value) => DateTime.SpecifyKind(value.AddHours(-7), DateTimeKind.Utc));
        var service = DiagnosticService(db, TechnicianId, clock.Object);
        Assert.Equal(2, (await service.GetTechnicianStatsAsync()).CompletedTodayCount);
        clock.Verify(x => x.ConvertVietnamToUtc(today.ToDateTime(TimeOnly.MinValue)), Times.Once);
        clock.Verify(x => x.ConvertVietnamToUtc(today.AddDays(1).ToDateTime(TimeOnly.MinValue)), Times.Once);
    }

    private DiagnosticWorkflowService DiagnosticService(AppDbContext db, Guid userId, IDateTimeProvider? clock = null)
    {
        var user = new Mock<ICurrentUserService>();
        user.SetupGet(x => x.UserId).Returns(userId);
        var doctor = new Mock<IDoctorContextService>();
        doctor.Setup(x => x.GetCurrentActiveDoctorAsync()).ReturnsAsync(new Doctor { Id = DoctorEntityId, UserId = DoctorId });
        using var scope = Factory.Services.CreateScope();
        return new(db, doctor.Object, user.Object, clock ?? scope.ServiceProvider.GetRequiredService<IDateTimeProvider>(), NullLogger<DiagnosticWorkflowService>.Instance);
    }

    [Theory]
    [InlineData("visit")]
    [InlineData("appointment")]
    [InlineData("technician")]
    [InlineData("patient")]
    public async Task F7_Twenty_order_lists_match_single_DTOs_and_have_constant_SQL_command_count(string list)
    {
        using var scope = Factory.Services.CreateScope();
        var seed = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var visit = await VisitAsync(seed);
        var appointment = await AppointmentAsync(seed);
        var serviceId = await seed.DiagnosticServices.Select(x => x.Id).FirstAsync();
        var orders = new List<DiagnosticOrder>();
        for (var i = 0; i < 20; i++)
        {
            var order = new DiagnosticOrder { OrderCode = Code(), PatientVisitId = visit.Id, AppointmentId = appointment.Id,
                PatientId = Patient1EntityId, OrderingDoctorId = DoctorEntityId, FacilityId = visit.FacilityId,
                Status = i % 2 == 0 ? DiagnosticOrderStatus.Completed : DiagnosticOrderStatus.InProgress,
                OrderedAtUtc = new DateTime(2040, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(visit.QueueNumber).AddMilliseconds(i),
                StartedByUserId = TechnicianId, CompletedByUserId = i % 2 == 0 ? TechnicianId : null,
                ReviewedByDoctorId = i % 2 == 0 ? DoctorEntityId : null, ReviewedAtUtc = i % 2 == 0 ? DateTime.UtcNow : null };
            order.Items.Add(new DiagnosticOrderItem { DiagnosticServiceId = serviceId, Status = DiagnosticItemStatus.Completed,
                Result = new DiagnosticResult { ResultText = "Kết quả R4", Conclusion = "Bình thường", ResultedByUserId = TechnicianId } });
            orders.Add(order);
        }
        seed.DiagnosticOrders.AddRange(orders);
        await seed.SaveChangesAsync();
        var counter = new SqlCounter();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(seed.Database.GetConnectionString()!).AddInterceptors(counter).Options);
        var service = DiagnosticService(db, list == "patient" ? Patient1Id : list == "technician" ? TechnicianId : DoctorId);
        var single = typeof(DiagnosticWorkflowService).GetMethod("GetOrderDtoByIdAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var redact = typeof(DiagnosticWorkflowService).GetMethod("RedactUnpublishedResultsForPatient", BindingFlags.Static | BindingFlags.NonPublic)!;
        var expected = new List<DiagnosticOrderDto>();
        foreach (var order in orders.OrderByDescending(x => x.OrderedAtUtc))
        {
            var dto = (await (Task<DiagnosticOrderDto?>)single.Invoke(service, new object[] { order.Id })!)!;
            expected.Add(list == "patient" ? (DiagnosticOrderDto)redact.Invoke(null, new object[] { dto })! : dto);
        }
        async Task<List<DiagnosticOrderDto>> LoadAsync(int size) => list switch
        {
            "visit" => await service.GetOrdersByVisitForDoctorAsync(visit.Id),
            "appointment" => await service.GetOrdersByAppointmentForDoctorAsync(appointment.Id),
            "technician" => (await service.GetTechnicianOrdersAsync(null, null, "R4-", 1, size)).Items.ToList(),
            _ => (await service.GetPatientOrdersAsync(1, size)).Items.ToList()
        };
        counter.Count = 0;
        var actual = await LoadAsync(20);
        var twentyCount = counter.Count;
        Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual));
        _output.WriteLine($"SQL {list}: 20 orders = {twentyCount}");
        if (list is "technician" or "patient")
        {
            counter.Count = 0;
            await LoadAsync(1);
            _output.WriteLine($"SQL {list}: 1 order = {counter.Count}");
            Assert.Equal(counter.Count, twentyCount);
        }
        Assert.InRange(twentyCount, 1, 8);
    }

    [Fact]
    public async Task F7_Batched_one_and_twenty_orders_use_same_split_queries_and_one_user_lookup()
    {
        using var scope = Factory.Services.CreateScope();
        var seed = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var visit = await VisitAsync(seed);
        var serviceIds = await seed.DiagnosticServices.OrderBy(x => x.Id).Select(x => x.Id).Take(2).ToListAsync();
        Assert.Equal(2, serviceIds.Count);
        var orders = Enumerable.Range(0, 20).Select(i => new DiagnosticOrder
        {
            OrderCode = Code(), PatientVisitId = visit.Id, PatientId = Patient1EntityId,
            OrderingDoctorId = DoctorEntityId, ReviewedByDoctorId = DoctorEntityId,
            StartedByUserId = DoctorId, CompletedByUserId = TechnicianId,
            Status = DiagnosticOrderStatus.Completed,
            Items = new List<DiagnosticOrderItem>
            {
                new() { DiagnosticServiceId = serviceIds[0], Result = new DiagnosticResult
                    { ResultText = "Kết quả bác sĩ", ResultedByUserId = DoctorId } },
                new() { DiagnosticServiceId = serviceIds[1], Result = new DiagnosticResult
                    { ResultText = "Kết quả kỹ thuật viên", ResultedByUserId = TechnicianId } }
            }
        }).ToList();
        seed.DiagnosticOrders.AddRange(orders);
        await seed.SaveChangesAsync();
        var counter = new SqlCounter();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(seed.Database.GetConnectionString()!).AddInterceptors(counter).Options);
        var service = DiagnosticService(db, DoctorId);
        var batch = typeof(DiagnosticWorkflowService).GetMethod("GetOrderDtosByIdsAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var ids = orders.Select(order => order.Id).Reverse().ToList();
        var twenty = await (Task<List<DiagnosticOrderDto>>)batch.Invoke(service, new object[] { ids })!;
        var twentyCount = counter.Count;
        Assert.Equal(ids, twenty.Select(order => order.Id));
        Assert.Equal(4, twentyCount); // root, doctor specialties, items/results, then Users
        Assert.Single(counter.Commands, command => command.Contains("AspNetUsers", StringComparison.Ordinal));
        Assert.All(counter.Commands.Take(3), command => Assert.DoesNotContain("AspNetUsers", command));
        Assert.All(twenty, order =>
        {
            Assert.Equal(2, order.Items.Count);
            Assert.Equal(order.StartedByUserName, order.ReviewedByDoctorName);
            Assert.Equal(order.StartedByUserName, order.Items[0].Result!.ResultedByUserName);
            Assert.Equal(order.CompletedByUserName, order.Items[1].Result!.ResultedByUserName);
        });
        counter.Count = 0;
        counter.Commands.Clear();
        var one = await (Task<List<DiagnosticOrderDto>>)batch.Invoke(service, new object[] { new List<long> { ids[0] } })!;
        Assert.Equal(twentyCount, counter.Count);
        Assert.Equal(JsonSerializer.Serialize(twenty[0]), JsonSerializer.Serialize(Assert.Single(one)));
        _output.WriteLine($"SQL batch: 20 orders = {twentyCount}, 1 order = {counter.Count}");
    }

    private sealed class SqlCounter : DbCommandInterceptor
    {
        public int Count;
        public List<string> Commands { get; } = new();
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { Count++; Commands.Add(command.CommandText); return ValueTask.FromResult(result); }
    }
}
