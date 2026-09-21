using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using ClinicManagement.Application.Appointments.DTOs.Doctor;
using ClinicManagement.Application.Billing.DTOs;
using ClinicManagement.Application.Prescriptions.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Common;
using ClinicManagement.Infrastructure.Persistence;
using ClinicManagement.Infrastructure.Visits;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ClinicManagement.IntegrationTests;

public class OutpatientHardenFlowTests : IntegrationTestBase
{
    public OutpatientHardenFlowTests(CustomWebApplicationFactory factory) : base(factory) { }

    private async Task<Medicine> CreateTestMedicineAsync(string prefix, int initialStock, decimal unitPrice = 15000m)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var med = new Medicine
        {
            Code = $"MED-{prefix}-{Guid.NewGuid():N}"[..15].ToUpper(),
            Name = $"Thuốc Test {prefix}",
            Unit = "Viên",
            StockQuantity = initialStock,
            UnitPrice = unitPrice,
            ReorderLevel = 5,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.Medicines.Add(med);
        await db.SaveChangesAsync();
        return med;
    }

    // ------------------------------------------------------------------------------------------------
    // SECTION 6 TEST 1: Ca có phí khám chưa lập hóa đơn -> không được Complete
    // ------------------------------------------------------------------------------------------------
    [Fact]
    public async Task Test01_Given_UnbilledConsultationFee_When_CompletionEvaluated_Then_CannotComplete()
    {
        long visitId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var patient = await db.Patients.FirstAsync();
            var doc = await db.Doctors.FirstAsync();
            var dept = await db.Departments.FirstAsync();

            var visit = new PatientVisit
            {
                VisitCode = $"VIS-UNB-{Guid.NewGuid():N}"[..18].ToUpper(),
                PatientId = patient.Id,
                FacilityId = dept.FacilityId,
                DepartmentId = dept.Id,
                AssignedDoctorId = doc.Id,
                Status = VisitStatus.InConsultation,
                Priority = VisitPriority.Normal,
                QueueNumber = Random.Shared.Next(1000, 999999),
                VisitDate = DateOnly.FromDateTime(DateTime.UtcNow)
            };
            db.PatientVisits.Add(visit);
            await db.SaveChangesAsync();
            visitId = visit.Id;

            // Doctor has completed consultation summary
            db.VisitSummaries.Add(new VisitSummary
            {
                PatientVisitId = visit.Id,
                DoctorId = doc.Id,
                ChiefComplaint = "Đau khớp gối",
                Diagnosis = "Thoái hóa khớp gối",
                CompletedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        // Evaluate completion: unbilled consultation fee exists (no invoice created)
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var (canComplete, reason) = await VisitCompletionCoordinator.CanCompleteVisitAsync(visitId, db);
            Assert.False(canComplete);
            Assert.Contains("tiền khám", reason, StringComparison.OrdinalIgnoreCase);

            var updated = await VisitCompletionCoordinator.TryUpdateVisitProgressAsync(visitId, db, DateTime.UtcNow);
            Assert.NotEqual(VisitStatus.Completed, updated);

            var visit = await db.PatientVisits.FindAsync(visitId);
            Assert.NotEqual(VisitStatus.Completed, visit!.Status);
            Assert.Null(visit.CompletedAtUtc);
        }
    }

    // ------------------------------------------------------------------------------------------------
    // SECTION 6 TEST 2: Ca có CLS đã có kết quả nhưng chưa lập hóa đơn -> không được Complete
    // ------------------------------------------------------------------------------------------------
    [Fact]
    public async Task Test02_Given_CompletedDiagnosticOrderUnbilled_When_CompletionEvaluated_Then_CannotComplete()
    {
        long visitId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var patient = await db.Patients.FirstAsync();
            var doc = await db.Doctors.FirstAsync();
            var dept = await db.Departments.FirstAsync();
            var diagService = await db.DiagnosticServices.FirstAsync();

            var visit = new PatientVisit
            {
                VisitCode = $"VIS-UNBCLS-{Guid.NewGuid():N}"[..18].ToUpper(),
                PatientId = patient.Id,
                FacilityId = dept.FacilityId,
                DepartmentId = dept.Id,
                AssignedDoctorId = doc.Id,
                Status = VisitStatus.InConsultation,
                Priority = VisitPriority.Normal,
                QueueNumber = Random.Shared.Next(1000, 999999),
                VisitDate = DateOnly.FromDateTime(DateTime.UtcNow)
            };
            db.PatientVisits.Add(visit);
            await db.SaveChangesAsync();
            visitId = visit.Id;

            db.VisitSummaries.Add(new VisitSummary
            {
                PatientVisitId = visit.Id,
                DoctorId = doc.Id,
                ChiefComplaint = "Ho kéo dài",
                Diagnosis = "Viêm phế quản",
                CompletedAtUtc = DateTime.UtcNow
            });

            // Consultation fee paid
            var consultInv = new Invoice
            {
                InvoiceCode = $"INV-C-{Guid.NewGuid():N}"[..18].ToUpper(),
                PatientId = patient.Id,
                PatientVisitId = visit.Id,
                Status = InvoiceStatus.Paid,
                Subtotal = 100000m,
                TotalAmount = 100000m,
                PaidAtUtc = DateTime.UtcNow,
                CreatedAtUtc = DateTime.UtcNow,
                Items = new List<InvoiceItem>
                {
                    new()
                    {
                        ItemCode = "EXAM-FEE",
                        Description = "Phí khám",
                        Quantity = 1,
                        UnitPrice = 100000m,
                        LineTotal = 100000m,
                        ReferenceType = "Consultation",
                        ReferenceId = visit.Id,
                        IsCancelled = false
                    }
                }
            };
            db.Invoices.Add(consultInv);

            // Completed and reviewed diagnostic order, but unbilled
            var order = new DiagnosticOrder
            {
                PatientVisitId = visit.Id,
                PatientId = patient.Id,
                OrderingDoctorId = doc.Id,
                Status = DiagnosticOrderStatus.Completed,
                OrderCode = $"ORD-CLS-{Guid.NewGuid():N}"[..15].ToUpper(),
                OrderedAtUtc = DateTime.UtcNow,
                ReviewedAtUtc = DateTime.UtcNow,
                ReviewedByDoctorId = doc.Id,
                Items = new List<DiagnosticOrderItem>
                {
                    new()
                    {
                        DiagnosticServiceId = diagService.Id,
                        Status = DiagnosticItemStatus.Completed
                    }
                }
            };
            db.DiagnosticOrders.Add(order);
            await db.SaveChangesAsync();
        }

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var (canComplete, reason) = await VisitCompletionCoordinator.CanCompleteVisitAsync(visitId, db);
            Assert.False(canComplete);
            Assert.Contains("chỉ định cận lâm sàng", reason, StringComparison.OrdinalIgnoreCase);

            var updated = await VisitCompletionCoordinator.TryUpdateVisitProgressAsync(visitId, db, DateTime.UtcNow);
            Assert.NotEqual(VisitStatus.Completed, updated);
        }
    }

    // ------------------------------------------------------------------------------------------------
    // SECTION 6 TEST 3: Ca có CLS đã có kết quả nhưng bác sĩ chưa duyệt (ReviewedAtUtc == null) -> không được Complete
    // ------------------------------------------------------------------------------------------------
    [Fact]
    public async Task Test03_Given_CompletedDiagnosticOrderUnreviewed_When_CompletionEvaluated_Then_CannotComplete()
    {
        long visitId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var patient = await db.Patients.FirstAsync();
            var doc = await db.Doctors.FirstAsync();
            var dept = await db.Departments.FirstAsync();
            var diagService = await db.DiagnosticServices.FirstAsync();

            var visit = new PatientVisit
            {
                VisitCode = $"VIS-UNREV-{Guid.NewGuid():N}"[..18].ToUpper(),
                PatientId = patient.Id,
                FacilityId = dept.FacilityId,
                DepartmentId = dept.Id,
                AssignedDoctorId = doc.Id,
                Status = VisitStatus.InConsultation,
                Priority = VisitPriority.Normal,
                QueueNumber = Random.Shared.Next(1000, 999999),
                VisitDate = DateOnly.FromDateTime(DateTime.UtcNow)
            };
            db.PatientVisits.Add(visit);
            await db.SaveChangesAsync();
            visitId = visit.Id;

            db.VisitSummaries.Add(new VisitSummary
            {
                PatientVisitId = visit.Id,
                DoctorId = doc.Id,
                ChiefComplaint = "Đau bụng",
                Diagnosis = "Đau dạ dày",
                CompletedAtUtc = DateTime.UtcNow
            });

            // Diagnostic order completed with results, but ReviewedAtUtc is null!
            var order = new DiagnosticOrder
            {
                PatientVisitId = visit.Id,
                PatientId = patient.Id,
                OrderingDoctorId = doc.Id,
                Status = DiagnosticOrderStatus.Completed,
                OrderCode = $"ORD-REV-{Guid.NewGuid():N}"[..15].ToUpper(),
                OrderedAtUtc = DateTime.UtcNow,
                ReviewedAtUtc = null, // NOT reviewed
                Items = new List<DiagnosticOrderItem>
                {
                    new()
                    {
                        DiagnosticServiceId = diagService.Id,
                        Status = DiagnosticItemStatus.Completed
                    }
                }
            };
            db.DiagnosticOrders.Add(order);
            await db.SaveChangesAsync();

            // All charges billed & paid
            var inv = new Invoice
            {
                InvoiceCode = $"INV-ALL-{Guid.NewGuid():N}"[..18].ToUpper(),
                PatientId = patient.Id,
                PatientVisitId = visit.Id,
                Status = InvoiceStatus.Paid,
                Subtotal = 220000m,
                TotalAmount = 220000m,
                PaidAtUtc = DateTime.UtcNow,
                CreatedAtUtc = DateTime.UtcNow,
                Items = new List<InvoiceItem>
                {
                    new()
                    {
                        ItemCode = "EXAM-FEE",
                        Description = "Phí khám",
                        Quantity = 1,
                        UnitPrice = 100000m,
                        LineTotal = 100000m,
                        ReferenceType = "Consultation",
                        ReferenceId = visit.Id,
                        IsCancelled = false
                    },
                    new()
                    {
                        ItemCode = diagService.Code,
                        Description = diagService.Name,
                        Quantity = 1,
                        UnitPrice = 120000m,
                        LineTotal = 120000m,
                        ReferenceType = "DiagnosticItem",
                        ReferenceId = order.Items.First().Id,
                        IsCancelled = false
                    }
                }
            };
            db.Invoices.Add(inv);
            await db.SaveChangesAsync();
        }

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var (canComplete, reason) = await VisitCompletionCoordinator.CanCompleteVisitAsync(visitId, db);
            Assert.False(canComplete);
            Assert.Contains("chưa được bác sĩ", reason, StringComparison.OrdinalIgnoreCase);

            var updated = await VisitCompletionCoordinator.TryUpdateVisitProgressAsync(visitId, db, DateTime.UtcNow);
            Assert.NotEqual(VisitStatus.Completed, updated);
        }
    }

    // ------------------------------------------------------------------------------------------------
    // SECTION 6 TEST 4: Ca có nhiều khoản phí, đã thanh toán 1 hóa đơn nhưng còn khoản khác chưa thanh toán -> không được Complete
    // ------------------------------------------------------------------------------------------------
    [Fact]
    public async Task Test04_Given_MultipleInvoicesWithOnePaidAndOneUnpaid_When_CompletionEvaluated_Then_CannotComplete()
    {
        var med = await CreateTestMedicineAsync("UNPAIDINV", 50);
        long visitId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var patient = await db.Patients.FirstAsync();
            var doc = await db.Doctors.FirstAsync();
            var dept = await db.Departments.FirstAsync();

            var visit = new PatientVisit
            {
                VisitCode = $"VIS-2INV-{Guid.NewGuid():N}"[..18].ToUpper(),
                PatientId = patient.Id,
                FacilityId = dept.FacilityId,
                DepartmentId = dept.Id,
                AssignedDoctorId = doc.Id,
                Status = VisitStatus.InPharmacy,
                Priority = VisitPriority.Normal,
                QueueNumber = Random.Shared.Next(1000, 999999),
                VisitDate = DateOnly.FromDateTime(DateTime.UtcNow)
            };
            db.PatientVisits.Add(visit);
            await db.SaveChangesAsync();
            visitId = visit.Id;

            db.VisitSummaries.Add(new VisitSummary
            {
                PatientVisitId = visit.Id,
                DoctorId = doc.Id,
                ChiefComplaint = "Cảm cúm",
                Diagnosis = "Nhiễm siêu vi",
                CompletedAtUtc = DateTime.UtcNow
            });

            // Invoice 1: Consultation fee (Paid)
            db.Invoices.Add(new Invoice
            {
                InvoiceCode = $"INV-P1-{Guid.NewGuid():N}"[..18].ToUpper(),
                PatientId = patient.Id,
                PatientVisitId = visit.Id,
                Status = InvoiceStatus.Paid,
                Subtotal = 100000m,
                TotalAmount = 100000m,
                PaidAtUtc = DateTime.UtcNow,
                CreatedAtUtc = DateTime.UtcNow,
                Items = new List<InvoiceItem>
                {
                    new()
                    {
                        ItemCode = "EXAM-FEE",
                        Description = "Phí khám",
                        Quantity = 1,
                        UnitPrice = 100000m,
                        LineTotal = 100000m,
                        ReferenceType = "Consultation",
                        ReferenceId = visit.Id,
                        IsCancelled = false
                    }
                }
            });

            var rx = new Prescription
            {
                PatientVisitId = visit.Id,
                PatientId = patient.Id,
                DoctorId = doc.Id,
                Status = PrescriptionStatus.Issued,
                CreatedAt = DateTime.UtcNow,
                Items = new List<PrescriptionItem>
                {
                    new() { MedicineId = med.Id, Quantity = 5, Dosage = "1 viên", Frequency = "1 lần/ngày", DurationDays = 5 }
                }
            };
            db.Prescriptions.Add(rx);
            await db.SaveChangesAsync();

            // Invoice 2: Prescription fee (Unpaid)
            db.Invoices.Add(new Invoice
            {
                InvoiceCode = $"INV-UP2-{Guid.NewGuid():N}"[..18].ToUpper(),
                PatientId = patient.Id,
                PatientVisitId = visit.Id,
                Status = InvoiceStatus.Unpaid,
                Subtotal = 75000m,
                TotalAmount = 75000m,
                CreatedAtUtc = DateTime.UtcNow,
                Items = new List<InvoiceItem>
                {
                    new()
                    {
                        ItemCode = med.Code,
                        Description = med.Name,
                        Quantity = 5,
                        UnitPrice = 15000m,
                        LineTotal = 75000m,
                        ReferenceType = PrescriptionItemBillingReference.ModernReferenceType,
                        ReferenceId = PrescriptionItemBillingReference.Encode(rx.Id, med.Id),
                        IsCancelled = false
                    }
                }
            });
            await db.SaveChangesAsync();
        }

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var (canComplete, reason) = await VisitCompletionCoordinator.CanCompleteVisitAsync(visitId, db);
            Assert.False(canComplete);
            Assert.Contains("chưa thanh toán", reason, StringComparison.OrdinalIgnoreCase);

            var updated = await VisitCompletionCoordinator.TryUpdateVisitProgressAsync(visitId, db, DateTime.UtcNow);
            Assert.NotEqual(VisitStatus.Completed, updated);
        }
    }

    // ------------------------------------------------------------------------------------------------
    // SECTION 6 TEST 5: Ca không có thuốc và không có CLS, sau khi kết luận và thanh toán tiền khám -> Complete thành công
    // ------------------------------------------------------------------------------------------------
    [Fact]
    public async Task Test05_Given_ConsultationOnlyVisitPaid_When_CompletionEvaluated_Then_CompletesSuccessfully()
    {
        long visitId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var patient = await db.Patients.FirstAsync();
            var doc = await db.Doctors.FirstAsync();
            var dept = await db.Departments.FirstAsync();

            var visit = new PatientVisit
            {
                VisitCode = $"VIS-NO-RX-{Guid.NewGuid():N}"[..18].ToUpper(),
                PatientId = patient.Id,
                FacilityId = dept.FacilityId,
                DepartmentId = dept.Id,
                AssignedDoctorId = doc.Id,
                Status = VisitStatus.InConsultation,
                Priority = VisitPriority.Normal,
                QueueNumber = Random.Shared.Next(1000, 999999),
                VisitDate = DateOnly.FromDateTime(DateTime.UtcNow)
            };
            db.PatientVisits.Add(visit);
            await db.SaveChangesAsync();
            visitId = visit.Id;

            db.VisitSummaries.Add(new VisitSummary
            {
                PatientVisitId = visit.Id,
                DoctorId = doc.Id,
                ChiefComplaint = "Tư vấn sức khỏe định kỳ",
                Diagnosis = "Sức khỏe bình thường",
                CompletedAtUtc = DateTime.UtcNow
            });

            // Consultation fee paid
            db.Invoices.Add(new Invoice
            {
                InvoiceCode = $"INV-CLEAN-{Guid.NewGuid():N}"[..18].ToUpper(),
                PatientId = patient.Id,
                PatientVisitId = visit.Id,
                Status = InvoiceStatus.Paid,
                Subtotal = 100000m,
                TotalAmount = 100000m,
                PaidAtUtc = DateTime.UtcNow,
                CreatedAtUtc = DateTime.UtcNow,
                Items = new List<InvoiceItem>
                {
                    new()
                    {
                        ItemCode = "EXAM-FEE",
                        Description = "Phí khám",
                        Quantity = 1,
                        UnitPrice = 100000m,
                        LineTotal = 100000m,
                        ReferenceType = "Consultation",
                        ReferenceId = visit.Id,
                        IsCancelled = false
                    }
                }
            });
            await db.SaveChangesAsync();
        }

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var (canComplete, reason) = await VisitCompletionCoordinator.CanCompleteVisitAsync(visitId, db);
            Assert.True(canComplete, reason);

            var updated = await VisitCompletionCoordinator.TryUpdateVisitProgressAsync(visitId, db, DateTime.UtcNow);
            Assert.Equal(VisitStatus.Completed, updated);

            var visit = await db.PatientVisits.FindAsync(visitId);
            Assert.Equal(VisitStatus.Completed, visit!.Status);
            Assert.NotNull(visit.CompletedAtUtc);
        }
    }

    // ------------------------------------------------------------------------------------------------
    // SECTION 6 TEST 6: Cố tình gọi API đổi trạng thái lượt khám sang Completed khi chưa đủ điều kiện -> bị từ chối (VISIT_CANNOT_COMPLETE), không lưu một phần
    // ------------------------------------------------------------------------------------------------
    [Fact]
    public async Task Test06_Given_IncompleteVisit_When_PutStatusCompletedDirectly_Then_RejectedWithVisitCannotComplete()
    {
        long visitId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var patient = await db.Patients.FirstAsync();
            var doc = await db.Doctors.FirstAsync();
            var dept = await db.Departments.FirstAsync();

            var visit = new PatientVisit
            {
                VisitCode = $"VIS-BYPASS-{Guid.NewGuid():N}"[..18].ToUpper(),
                PatientId = patient.Id,
                FacilityId = dept.FacilityId,
                DepartmentId = dept.Id,
                AssignedDoctorId = doc.Id,
                Status = VisitStatus.InConsultation,
                Priority = VisitPriority.Normal,
                QueueNumber = Random.Shared.Next(1000, 999999),
                VisitDate = DateOnly.FromDateTime(DateTime.UtcNow)
            };
            db.PatientVisits.Add(visit);
            await db.SaveChangesAsync();
            visitId = visit.Id;
        }

        await AuthenticateAsync("doc@test.com");
        var res = await Client.PutAsync($"/api/v1/patient-visits/{visitId}/status?status=Completed", null);

        Assert.True(res.StatusCode == HttpStatusCode.UnprocessableEntity || res.StatusCode == HttpStatusCode.BadRequest);
        var body = await res.Content.ReadAsStringAsync();
        Assert.Contains("VISIT_CANNOT_COMPLETE", body);

        // Verify visit status in DB remains untouched
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var visit = await db.PatientVisits.FindAsync(visitId);
            Assert.Equal(VisitStatus.InConsultation, visit!.Status);
            Assert.Null(visit.CompletedAtUtc);
        }
    }

    // ------------------------------------------------------------------------------------------------
    // SECTION 6 TEST 7: Ca đã hủy (Cancelled) không thể chuyển sang Completed -> bị từ chối (VISIT_ALREADY_CANCELLED)
    // ------------------------------------------------------------------------------------------------
    [Fact]
    public async Task Test07_Given_CancelledVisit_When_PutStatusCompleted_Then_RejectedWithVisitAlreadyCancelled()
    {
        long visitId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var patient = await db.Patients.FirstAsync();
            var doc = await db.Doctors.FirstAsync();
            var dept = await db.Departments.FirstAsync();

            var visit = new PatientVisit
            {
                VisitCode = $"VIS-CANC-{Guid.NewGuid():N}"[..18].ToUpper(),
                PatientId = patient.Id,
                FacilityId = dept.FacilityId,
                DepartmentId = dept.Id,
                AssignedDoctorId = doc.Id,
                Status = VisitStatus.Cancelled,
                CancelledAtUtc = DateTime.UtcNow,
                CancellationReason = "Bệnh nhân yêu cầu hủy",
                Priority = VisitPriority.Normal,
                QueueNumber = Random.Shared.Next(1000, 999999),
                VisitDate = DateOnly.FromDateTime(DateTime.UtcNow)
            };
            db.PatientVisits.Add(visit);
            await db.SaveChangesAsync();
            visitId = visit.Id;
        }

        await AuthenticateAsync("doc@test.com");
        var res = await Client.PutAsync($"/api/v1/patient-visits/{visitId}/status?status=Completed", null);

        Assert.True(res.StatusCode == HttpStatusCode.UnprocessableEntity || res.StatusCode == HttpStatusCode.BadRequest);
        var body = await res.Content.ReadAsStringAsync();
        Assert.Contains("VISIT_ALREADY_CANCELLED", body);

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var visit = await db.PatientVisits.FindAsync(visitId);
            Assert.Equal(VisitStatus.Cancelled, visit!.Status);
            Assert.Null(visit.CompletedAtUtc);
        }
    }

    // ------------------------------------------------------------------------------------------------
    // SECTION 6 TEST 8: Người dùng ngoài facility không thể cập nhật trạng thái lượt khám
    // ------------------------------------------------------------------------------------------------
    [Fact]
    public async Task Test08_Given_UserOutsideFacility_When_UpdatingVisitStatus_Then_Forbidden()
    {
        long visitId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var patient = await db.Patients.FirstAsync();

            // Create isolated other facility
            var otherFac = new Facility
            {
                Code = $"FAC-OTHER-{Guid.NewGuid():N}"[..14].ToUpper(),
                Name = "Cơ sở Biệt Lập Ngoại Vi Test",
                Address = "999 Đường Rừng",
                City = "Bình Dương",
                Phone = "02749999999",
                IsActive = true
            };
            db.Facilities.Add(otherFac);
            await db.SaveChangesAsync();

            var otherDept = new Department
            {
                FacilityId = otherFac.Id,
                Code = $"DEP-OTH-{Guid.NewGuid():N}"[..14].ToUpper(),
                Name = "Khoa Ngoại Vi",
                DepartmentType = DepartmentType.Clinical,
                IsActive = true
            };
            db.Departments.Add(otherDept);
            await db.SaveChangesAsync();

            var visit = new PatientVisit
            {
                VisitCode = $"VIS-OTH-{Guid.NewGuid():N}"[..18].ToUpper(),
                PatientId = patient.Id,
                FacilityId = otherFac.Id,
                DepartmentId = otherDept.Id,
                AssignedDoctorId = null,
                Status = VisitStatus.InConsultation,
                Priority = VisitPriority.Normal,
                QueueNumber = Random.Shared.Next(1000, 999999),
                VisitDate = DateOnly.FromDateTime(DateTime.UtcNow)
            };
            db.PatientVisits.Add(visit);
            await db.SaveChangesAsync();
            visitId = visit.Id;
        }

        // Authenticate as base doctor (who does NOT have assignment to otherFac)
        await AuthenticateAsync("doc@test.com");
        var res = await Client.PutAsync($"/api/v1/patient-visits/{visitId}/status?status=Completed", null);

        Assert.True(res.StatusCode == HttpStatusCode.Forbidden || res.StatusCode == HttpStatusCode.NotFound || res.StatusCode == HttpStatusCode.Unauthorized);
    }

    // ------------------------------------------------------------------------------------------------
    // SECTION 6 TEST 9: Counterexample ReferenceId collision: kê đơn Rx=1 Med=32705 và Rx=42950 Med=1 (cùng sinh 4295000001 nếu không có versioning) -> chứng minh thanh toán ca này không bị nhận nhầm sang ca kia
    // ------------------------------------------------------------------------------------------------
    [Fact]
    public void Test09_Given_ReferenceIdCollisionCounterexample_When_Versioned_Then_PaymentCannotCrossClaim()
    {
        // Counterexample:
        // Prescription A: Rx = 1, Med = 32705 -> Bitshift: (1L << 32) | 32705 = 4295000001
        // Prescription B: Rx = 42950, Med = 1 -> Decimal: 42950 * 100000 + 1 = 4295000001
        // Both evaluate to identical raw numeric value 4295000001!

        var collisionRefId = 4295000001L;

        // Verify collision math
        Assert.Equal(collisionRefId, PrescriptionItemBillingReference.Encode(1L, 32705L));
        Assert.Equal(42950L * 100000L + 1L, collisionRefId);

        // 1. With Modern versioning ("PrescriptionItem:v2"):
        // Only matches Rx=1, Med=32705; NEVER matches Rx=42950!
        Assert.True(PrescriptionItemBillingReference.MatchesPrescription(PrescriptionItemBillingReference.ModernReferenceType, collisionRefId, 1L));
        Assert.False(PrescriptionItemBillingReference.MatchesPrescription(PrescriptionItemBillingReference.ModernReferenceType, collisionRefId, 42950L));

        Assert.True(PrescriptionItemBillingReference.TryDecodeMedicineId(PrescriptionItemBillingReference.ModernReferenceType, collisionRefId, 1L, out var medA));
        Assert.Equal(32705L, medA);
        Assert.False(PrescriptionItemBillingReference.TryDecodeMedicineId(PrescriptionItemBillingReference.ModernReferenceType, collisionRefId, 42950L, out _));

        // 2. With Legacy versioning ("PrescriptionItem"):
        // Only matches Rx=42950, Med=1; NEVER matches Rx=1!
        Assert.True(PrescriptionItemBillingReference.MatchesPrescription(PrescriptionItemBillingReference.LegacyReferenceType, collisionRefId, 42950L));
        Assert.False(PrescriptionItemBillingReference.MatchesPrescription(PrescriptionItemBillingReference.LegacyReferenceType, collisionRefId, 1L));

        Assert.True(PrescriptionItemBillingReference.TryDecodeMedicineId(PrescriptionItemBillingReference.LegacyReferenceType, collisionRefId, 42950L, out var medB));
        Assert.Equal(1L, medB);
        Assert.False(PrescriptionItemBillingReference.TryDecodeMedicineId(PrescriptionItemBillingReference.LegacyReferenceType, collisionRefId, 1L, out _));
    }

    // ------------------------------------------------------------------------------------------------
    // SECTION 6 TEST 10: Ca có hóa đơn thanh toán của bệnh nhân/lượt khám khác không được tính để cấp thuốc
    // ------------------------------------------------------------------------------------------------
    [Fact]
    public async Task Test10_Given_InvoiceFromDifferentPatientOrVisit_When_Dispensed_Then_Rejected()
    {
        var med = await CreateTestMedicineAsync("CROSSPAT", 50);
        long targetRxId;

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var doc = await db.Doctors.FirstAsync();
            var dept = await db.Departments.FirstAsync();
            var patient1 = await db.Patients.FirstAsync();
            var patient2 = await db.Patients.OrderBy(p => p.Id).Skip(1).FirstAsync();

            // Visit 1 for Patient 1
            var visit1 = new PatientVisit
            {
                VisitCode = $"VIS-PAT1-{Guid.NewGuid():N}"[..18].ToUpper(),
                PatientId = patient1.Id,
                FacilityId = dept.FacilityId,
                DepartmentId = dept.Id,
                AssignedDoctorId = doc.Id,
                Status = VisitStatus.InPharmacy,
                Priority = VisitPriority.Normal,
                QueueNumber = Random.Shared.Next(1000, 999999),
                VisitDate = DateOnly.FromDateTime(DateTime.UtcNow)
            };
            db.PatientVisits.Add(visit1);

            // Visit 2 for Patient 2
            var visit2 = new PatientVisit
            {
                VisitCode = $"VIS-PAT2-{Guid.NewGuid():N}"[..18].ToUpper(),
                PatientId = patient2.Id,
                FacilityId = dept.FacilityId,
                DepartmentId = dept.Id,
                AssignedDoctorId = doc.Id,
                Status = VisitStatus.InPharmacy,
                Priority = VisitPriority.Normal,
                QueueNumber = Random.Shared.Next(1000, 999999),
                VisitDate = DateOnly.FromDateTime(DateTime.UtcNow)
            };
            db.PatientVisits.Add(visit2);
            await db.SaveChangesAsync();

            // Prescription for Visit 2 (Patient 2)
            var rx2 = new Prescription
            {
                PatientVisitId = visit2.Id,
                PatientId = patient2.Id,
                DoctorId = doc.Id,
                Status = PrescriptionStatus.Issued,
                CreatedAt = DateTime.UtcNow,
                Items = new List<PrescriptionItem>
                {
                    new() { MedicineId = med.Id, Quantity = 5, Dosage = "1 viên", Frequency = "1 lần/ngày", DurationDays = 5 }
                }
            };
            db.Prescriptions.Add(rx2);
            await db.SaveChangesAsync();
            targetRxId = rx2.Id;

            // Invoice mistakenly belongs to Visit 1 / Patient 1, even though it references rx2 items
            var modernRefId = PrescriptionItemBillingReference.Encode(rx2.Id, med.Id);
            var inv1 = new Invoice
            {
                InvoiceCode = $"INV-P1M-{Guid.NewGuid():N}"[..18].ToUpper(),
                PatientId = patient1.Id, // Patient 1!
                PatientVisitId = visit1.Id, // Visit 1!
                Status = InvoiceStatus.Paid,
                Subtotal = 75000m,
                TotalAmount = 75000m,
                PaidAtUtc = DateTime.UtcNow,
                CreatedAtUtc = DateTime.UtcNow,
                Items = new List<InvoiceItem>
                {
                    new()
                    {
                        ItemCode = med.Code,
                        Description = med.Name,
                        Quantity = 5,
                        UnitPrice = 15000m,
                        LineTotal = 75000m,
                        ReferenceType = PrescriptionItemBillingReference.ModernReferenceType,
                        ReferenceId = modernRefId,
                        IsCancelled = false
                    }
                }
            };
            db.Invoices.Add(inv1);
            await db.SaveChangesAsync();
        }

        await AuthenticateAsync("pharm@test.com");
        var res = await Client.PostAsync($"/api/v1/pharmacy/prescriptions/{targetRxId}/dispense", null);

        // Dispensing must be rejected because payment in visit 1 does NOT cover visit 2
        Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
        var body = await res.Content.ReadAsStringAsync();
        Assert.Contains("PRESCRIPTION_NOT_PAID", body);
    }

    // ------------------------------------------------------------------------------------------------
    // SECTION 6 TEST 11: Đơn thuốc có dữ liệu cũ dạng decimal hoặc không map được toàn đơn -> hành vi đúng như thiết kế đã chốt
    // ------------------------------------------------------------------------------------------------
    [Fact]
    public async Task Test11_Given_LegacyDecimalDataVsUnmappedWholeRx_When_Dispensed_Then_BehavesCorrectly()
    {
        var med = await CreateTestMedicineAsync("LEG11", 50);
        long legacyDecimalRxId;
        long legacyWholeRxId;

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var patient = await db.Patients.FirstAsync();
            var doc = await db.Doctors.FirstAsync();
            var dept = await db.Departments.FirstAsync();

            var visit = new PatientVisit
            {
                VisitCode = $"VIS-L11-{Guid.NewGuid():N}"[..18].ToUpper(),
                PatientId = patient.Id,
                FacilityId = dept.FacilityId,
                DepartmentId = dept.Id,
                AssignedDoctorId = doc.Id,
                Status = VisitStatus.InPharmacy,
                Priority = VisitPriority.Normal,
                QueueNumber = Random.Shared.Next(1000, 999999),
                VisitDate = DateOnly.FromDateTime(DateTime.UtcNow)
            };
            db.PatientVisits.Add(visit);
            await db.SaveChangesAsync();

            // Rx 1: Legacy Decimal Reference
            var rx1 = new Prescription
            {
                PatientVisitId = visit.Id,
                PatientId = patient.Id,
                DoctorId = doc.Id,
                Status = PrescriptionStatus.Issued,
                CreatedAt = DateTime.UtcNow,
                Items = new List<PrescriptionItem>
                {
                    new() { MedicineId = med.Id, Quantity = 4, Dosage = "1 viên", Frequency = "1 lần/ngày", DurationDays = 4 }
                }
            };
            db.Prescriptions.Add(rx1);

            // Rx 2: Legacy Whole Rx (unmapped item)
            var rx2 = new Prescription
            {
                PatientVisitId = visit.Id,
                PatientId = patient.Id,
                DoctorId = doc.Id,
                Status = PrescriptionStatus.Issued,
                CreatedAt = DateTime.UtcNow,
                Items = new List<PrescriptionItem>
                {
                    new() { MedicineId = med.Id, Quantity = 3, Dosage = "1 viên", Frequency = "1 lần/ngày", DurationDays = 3 }
                }
            };
            db.Prescriptions.Add(rx2);
            await db.SaveChangesAsync();

            legacyDecimalRxId = rx1.Id;
            legacyWholeRxId = rx2.Id;

            // Invoice for Rx 1 has item-level decimal encoding: rx1.Id * 100000L + med.Id
            var inv1 = new Invoice
            {
                InvoiceCode = $"INV-DEC11-{Guid.NewGuid():N}"[..18].ToUpper(),
                PatientId = patient.Id,
                PatientVisitId = visit.Id,
                Status = InvoiceStatus.Paid,
                Subtotal = 60000m,
                TotalAmount = 60000m,
                PaidAtUtc = DateTime.UtcNow,
                CreatedAtUtc = DateTime.UtcNow,
                Items = new List<InvoiceItem>
                {
                    new()
                    {
                        ItemCode = med.Code,
                        Description = med.Name,
                        Quantity = 4,
                        UnitPrice = 15000m,
                        LineTotal = 60000m,
                        ReferenceType = PrescriptionItemBillingReference.LegacyReferenceType,
                        ReferenceId = rx1.Id * 100000L + med.Id,
                        IsCancelled = false
                    }
                }
            };
            db.Invoices.Add(inv1);

            // Invoice for Rx 2 only has whole prescription id: ReferenceId = rx2.Id (WITHOUT medicineId)
            var inv2 = new Invoice
            {
                InvoiceCode = $"INV-WHL11-{Guid.NewGuid():N}"[..18].ToUpper(),
                PatientId = patient.Id,
                PatientVisitId = visit.Id,
                Status = InvoiceStatus.Paid,
                Subtotal = 45000m,
                TotalAmount = 45000m,
                PaidAtUtc = DateTime.UtcNow,
                CreatedAtUtc = DateTime.UtcNow,
                Items = new List<InvoiceItem>
                {
                    new()
                    {
                        ItemCode = "MED-WHOLE",
                        Description = "Whole rx",
                        Quantity = 1,
                        UnitPrice = 45000m,
                        LineTotal = 45000m,
                        ReferenceType = PrescriptionItemBillingReference.LegacyReferenceType,
                        ReferenceId = rx2.Id,
                        IsCancelled = false
                    }
                }
            };
            db.Invoices.Add(inv2);
            await db.SaveChangesAsync();
        }

        await AuthenticateAsync("pharm@test.com");

        // 11a: Legacy decimal data succeeds!
        var res1 = await Client.PostAsync($"/api/v1/pharmacy/prescriptions/{legacyDecimalRxId}/dispense", null);
        Assert.Equal(HttpStatusCode.OK, res1.StatusCode);

        // 11b: Legacy whole prescription without item-level payment is rejected
        var res2 = await Client.PostAsync($"/api/v1/pharmacy/prescriptions/{legacyWholeRxId}/dispense", null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, res2.StatusCode);
        var body2 = await res2.Content.ReadAsStringAsync();
        Assert.Contains("PRESCRIPTION_NOT_PAID", body2);
    }

    // ------------------------------------------------------------------------------------------------
    // SECTION 6 TEST 12: Xử lý đồng thời/trùng lặp thanh toán và cấp phát không sinh trạng thái bất nhất
    // ------------------------------------------------------------------------------------------------
    [Fact]
    public async Task Test12_Given_ConcurrentOrDuplicatePaymentAndDispense_When_Executed_Then_MaintainsConsistentState()
    {
        var med = await CreateTestMedicineAsync("CONCURR12", 50);
        long invoiceId;
        long rxId;

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var patient = await db.Patients.FirstAsync();
            var doc = await db.Doctors.FirstAsync();
            var dept = await db.Departments.FirstAsync();

            var visit = new PatientVisit
            {
                VisitCode = $"VIS-CC12-{Guid.NewGuid():N}"[..18].ToUpper(),
                PatientId = patient.Id,
                FacilityId = dept.FacilityId,
                DepartmentId = dept.Id,
                AssignedDoctorId = doc.Id,
                Status = VisitStatus.InPharmacy,
                Priority = VisitPriority.Normal,
                QueueNumber = Random.Shared.Next(1000, 999999),
                VisitDate = DateOnly.FromDateTime(DateTime.UtcNow)
            };
            db.PatientVisits.Add(visit);
            await db.SaveChangesAsync();

            var rx = new Prescription
            {
                PatientVisitId = visit.Id,
                PatientId = patient.Id,
                DoctorId = doc.Id,
                Status = PrescriptionStatus.Issued,
                CreatedAt = DateTime.UtcNow,
                Items = new List<PrescriptionItem>
                {
                    new() { MedicineId = med.Id, Quantity = 5, Dosage = "1 viên", Frequency = "1 lần/ngày", DurationDays = 5 }
                }
            };
            db.Prescriptions.Add(rx);
            await db.SaveChangesAsync();
            rxId = rx.Id;

            var inv = new Invoice
            {
                InvoiceCode = $"INV-CC12-{Guid.NewGuid():N}"[..18].ToUpper(),
                PatientId = patient.Id,
                PatientVisitId = visit.Id,
                Status = InvoiceStatus.Unpaid,
                Subtotal = 75000m,
                TotalAmount = 75000m,
                CreatedAtUtc = DateTime.UtcNow,
                Items = new List<InvoiceItem>
                {
                    new()
                    {
                        ItemCode = med.Code,
                        Description = med.Name,
                        Quantity = 5,
                        UnitPrice = 15000m,
                        LineTotal = 75000m,
                        ReferenceType = PrescriptionItemBillingReference.ModernReferenceType,
                        ReferenceId = PrescriptionItemBillingReference.Encode(rx.Id, med.Id),
                        IsCancelled = false
                    }
                }
            };
            db.Invoices.Add(inv);
            await db.SaveChangesAsync();
            invoiceId = inv.Id;
        }

        // 1. Pay Invoice
        await AuthenticateAsync("rec@test.com");
        var payReq = new ProcessPaymentRequest { Amount = 75000m, Method = PaymentMethod.Cash, ReferenceCode = "PAY-CC-12" };
        var pay1 = await Client.PostAsJsonAsync($"/api/v1/reception/billing/invoices/{invoiceId}/pay", payReq);
        Assert.Equal(HttpStatusCode.OK, pay1.StatusCode);

        // Duplicate payment attempt: rejected or handled safely without double payment
        var pay2 = await Client.PostAsJsonAsync($"/api/v1/reception/billing/invoices/{invoiceId}/pay", payReq);
        Assert.True(pay2.StatusCode == HttpStatusCode.BadRequest || pay2.StatusCode == HttpStatusCode.UnprocessableEntity || pay2.StatusCode == HttpStatusCode.OK);

        // 2. Dispense Prescription
        await AuthenticateAsync("pharm@test.com");
        var disp1 = await Client.PostAsync($"/api/v1/pharmacy/prescriptions/{rxId}/dispense", null);
        Assert.Equal(HttpStatusCode.OK, disp1.StatusCode);

        // Duplicate dispense attempt: must be rejected with ALREADY_DISPENSED / 409 / 422
        var disp2 = await Client.PostAsync($"/api/v1/pharmacy/prescriptions/{rxId}/dispense", null);
        Assert.True(disp2.StatusCode == HttpStatusCode.Conflict || disp2.StatusCode == HttpStatusCode.UnprocessableEntity || disp2.StatusCode == HttpStatusCode.BadRequest);

        // Verify final inventory: stock was deducted EXACTLY ONCE (50 - 5 = 45)
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var updatedMed = await db.Medicines.FindAsync(med.Id);
            Assert.Equal(45, updatedMed!.StockQuantity);
        }
    }

    // ------------------------------------------------------------------------------------------------
    // SECTION 6 BONUS: Modern 64-bit encoded prescription dispensing succeeds
    // ------------------------------------------------------------------------------------------------
    [Fact]
    public async Task Given_PrescriptionWithModern64BitEncodedItem_When_Dispensed_Then_Succeeds()
    {
        var med = await CreateTestMedicineAsync("MOD64", 50);
        long rxId;

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var patient = await db.Patients.FirstAsync();
            var doc = await db.Doctors.FirstAsync();
            var dept = await db.Departments.FirstAsync();

            var visit = new PatientVisit
            {
                VisitCode = $"VIS-M64-{Guid.NewGuid():N}"[..18].ToUpper(),
                PatientId = patient.Id,
                FacilityId = dept.FacilityId,
                DepartmentId = dept.Id,
                AssignedDoctorId = doc.Id,
                Status = VisitStatus.InPharmacy,
                Priority = VisitPriority.Normal,
                QueueNumber = Random.Shared.Next(1000, 999999),
                VisitDate = DateOnly.FromDateTime(DateTime.UtcNow)
            };
            db.PatientVisits.Add(visit);
            await db.SaveChangesAsync();

            var rx = new Prescription
            {
                PatientVisitId = visit.Id,
                PatientId = patient.Id,
                DoctorId = doc.Id,
                Status = PrescriptionStatus.Issued,
                CreatedAt = DateTime.UtcNow
            };
            db.Prescriptions.Add(rx);
            await db.SaveChangesAsync();
            rxId = rx.Id;

            db.PrescriptionItems.Add(new PrescriptionItem
            {
                PrescriptionId = rx.Id,
                MedicineId = med.Id,
                Quantity = 5,
                Dosage = "1 viên",
                Frequency = "2 lần/ngày",
                DurationDays = 5
            });

            var modernRefId = PrescriptionItemBillingReference.Encode(rx.Id, med.Id);
            var inv = new Invoice
            {
                InvoiceCode = $"INV-M64-{Guid.NewGuid():N}"[..18].ToUpper(),
                PatientId = patient.Id,
                PatientVisitId = visit.Id,
                Status = InvoiceStatus.Paid,
                Subtotal = 75000m,
                TotalAmount = 75000m,
                PaidAtUtc = DateTime.UtcNow,
                CreatedAtUtc = DateTime.UtcNow,
                Items = new List<InvoiceItem>
                {
                    new()
                    {
                        ItemCode = med.Code,
                        Description = med.Name,
                        Quantity = 5,
                        UnitPrice = 15000m,
                        LineTotal = 75000m,
                        ReferenceType = PrescriptionItemBillingReference.ModernReferenceType,
                        ReferenceId = modernRefId,
                        IsCancelled = false
                    }
                }
            };
            db.Invoices.Add(inv);
            await db.SaveChangesAsync();
        }

        await AuthenticateAsync("pharm@test.com");
        var res = await Client.PostAsync($"/api/v1/pharmacy/prescriptions/{rxId}/dispense", null);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var updatedMed = await db.Medicines.FindAsync(med.Id);
            Assert.Equal(45, updatedMed!.StockQuantity);

            var updatedRx = await db.Prescriptions.FindAsync(rxId);
            Assert.Equal(PrescriptionStatus.Dispensed, updatedRx!.Status);
        }
    }

    // ------------------------------------------------------------------------------------------------
    // SECTION 6 BONUS: Prescription draft locked when billed
    // ------------------------------------------------------------------------------------------------
    [Fact]
    public async Task Given_PrescriptionDraft_When_BilledInActiveInvoice_Then_DoctorCannotEditPrescriptionDraft()
    {
        var med = await CreateTestMedicineAsync("LOCKMED", 50);
        long visitId;
        long rxId;

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var patient = await db.Patients.FirstAsync();
            var doc = await db.Doctors.FirstAsync();
            var dept = await db.Departments.FirstAsync();

            var visit = new PatientVisit
            {
                VisitCode = $"VIS-LCK-{Guid.NewGuid():N}"[..18].ToUpper(),
                PatientId = patient.Id,
                FacilityId = dept.FacilityId,
                DepartmentId = dept.Id,
                AssignedDoctorId = doc.Id,
                Status = VisitStatus.InConsultation,
                Priority = VisitPriority.Normal,
                QueueNumber = Random.Shared.Next(1000, 999999),
                VisitDate = DateOnly.FromDateTime(DateTime.UtcNow)
            };
            db.PatientVisits.Add(visit);
            await db.SaveChangesAsync();
            visitId = visit.Id;

            var rx = new Prescription
            {
                PatientVisitId = visit.Id,
                PatientId = patient.Id,
                DoctorId = doc.Id,
                Status = PrescriptionStatus.Issued,
                CreatedAt = DateTime.UtcNow
            };
            db.Prescriptions.Add(rx);
            await db.SaveChangesAsync();
            rxId = rx.Id;

            db.PrescriptionItems.Add(new PrescriptionItem
            {
                PrescriptionId = rx.Id,
                MedicineId = med.Id,
                Quantity = 3,
                Dosage = "1 viên",
                Frequency = "1 lần/ngày",
                DurationDays = 3
            });

            var refId = PrescriptionItemBillingReference.Encode(rx.Id, med.Id);
            var inv = new Invoice
            {
                InvoiceCode = $"INV-LCK-{Guid.NewGuid():N}"[..18].ToUpper(),
                PatientId = patient.Id,
                PatientVisitId = visit.Id,
                Status = InvoiceStatus.Unpaid,
                Subtotal = 45000m,
                TotalAmount = 45000m,
                CreatedAtUtc = DateTime.UtcNow,
                Items = new List<InvoiceItem>
                {
                    new()
                    {
                        ItemCode = med.Code,
                        Description = med.Name,
                        Quantity = 3,
                        UnitPrice = 15000m,
                        LineTotal = 45000m,
                        ReferenceType = PrescriptionItemBillingReference.ModernReferenceType,
                        ReferenceId = refId,
                        IsCancelled = false
                    }
                }
            };
            db.Invoices.Add(inv);
            await db.SaveChangesAsync();
        }

        // Doctor attempts to edit prescription draft
        await AuthenticateAsync("doc@test.com");
        var editRes = await Client.PutAsJsonAsync($"/api/v1/doctor/visits/{visitId}/prescription-draft", new SavePrescriptionDraftRequest
        {
            Notes = "Cập nhật liều dùng",
            Items = new List<SavePrescriptionItemRequest>
            {
                new()
                {
                    MedicineId = med.Id,
                    Quantity = 5,
                    Dosage = "2 viên",
                    Frequency = "2 lần/ngày",
                    DurationDays = 5
                }
            }
        });

        Assert.True(editRes.StatusCode == HttpStatusCode.BadRequest || editRes.StatusCode == HttpStatusCode.UnprocessableEntity);
        var errBody = await editRes.Content.ReadAsStringAsync();
        Assert.Contains("PRESCRIPTION_ALREADY_BILLED", errBody);
    }

    // ------------------------------------------------------------------------------------------------
    // SECTION 6 BONUS: Partial payment rejected with insufficient quantity
    // ------------------------------------------------------------------------------------------------
    [Fact]
    public async Task Given_PrescriptionItemPartiallyPaid_When_Dispensed_Then_RejectedWithInsufficientPaidQuantity()
    {
        var med = await CreateTestMedicineAsync("PARTIAL", 50);
        long rxId;

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var patient = await db.Patients.FirstAsync();
            var doc = await db.Doctors.FirstAsync();
            var dept = await db.Departments.FirstAsync();

            var visit = new PatientVisit
            {
                VisitCode = $"VIS-PART-{Guid.NewGuid():N}"[..18].ToUpper(),
                PatientId = patient.Id,
                FacilityId = dept.FacilityId,
                DepartmentId = dept.Id,
                AssignedDoctorId = doc.Id,
                Status = VisitStatus.InPharmacy,
                Priority = VisitPriority.Normal,
                QueueNumber = Random.Shared.Next(1000, 999999),
                VisitDate = DateOnly.FromDateTime(DateTime.UtcNow)
            };
            db.PatientVisits.Add(visit);
            await db.SaveChangesAsync();

            var rx = new Prescription
            {
                PatientVisitId = visit.Id,
                PatientId = patient.Id,
                DoctorId = doc.Id,
                Status = PrescriptionStatus.Issued,
                CreatedAt = DateTime.UtcNow
            };
            db.Prescriptions.Add(rx);
            await db.SaveChangesAsync();
            rxId = rx.Id;

            // Prescription requests 10 units
            db.PrescriptionItems.Add(new PrescriptionItem
            {
                PrescriptionId = rx.Id,
                MedicineId = med.Id,
                Quantity = 10,
                Dosage = "1 viên",
                Frequency = "2 lần/ngày",
                DurationDays = 5
            });

            // Invoice only covers 6 units
            var refId = PrescriptionItemBillingReference.Encode(rx.Id, med.Id);
            var inv = new Invoice
            {
                InvoiceCode = $"INV-PART-{Guid.NewGuid():N}"[..18].ToUpper(),
                PatientId = patient.Id,
                PatientVisitId = visit.Id,
                Status = InvoiceStatus.Paid,
                Subtotal = 90000m,
                TotalAmount = 90000m,
                PaidAtUtc = DateTime.UtcNow,
                CreatedAtUtc = DateTime.UtcNow,
                Items = new List<InvoiceItem>
                {
                    new()
                    {
                        ItemCode = med.Code,
                        Description = med.Name,
                        Quantity = 6, // only 6 of 10 paid
                        UnitPrice = 15000m,
                        LineTotal = 90000m,
                        ReferenceType = PrescriptionItemBillingReference.ModernReferenceType,
                        ReferenceId = refId,
                        IsCancelled = false
                    }
                }
            };
            db.Invoices.Add(inv);
            await db.SaveChangesAsync();
        }

        await AuthenticateAsync("pharm@test.com");
        var res = await Client.PostAsync($"/api/v1/pharmacy/prescriptions/{rxId}/dispense", null);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
        var body = await res.Content.ReadAsStringAsync();
        using var jsonDoc = JsonDocument.Parse(body);
        var msg = jsonDoc.RootElement.GetProperty("message").GetString();
        Assert.Contains("chưa được thanh toán đủ số lượng", msg);
    }
}
