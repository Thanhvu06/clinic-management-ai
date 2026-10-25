using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClinicManagement.Application.AI.Tools;
using ClinicManagement.Application.Appointments.DTOs.Doctor;
using ClinicManagement.Application.Appointments.Interfaces;
using ClinicManagement.Application.Authentication.Interfaces;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Diagnostics.DTOs;
using ClinicManagement.Application.Diagnostics.Interfaces;
using ClinicManagement.Application.Pharmacy.Interfaces;
using ClinicManagement.Application.Visits.DTOs;
using ClinicManagement.Application.Visits.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.AI.Tools;

public sealed partial class RoleConfirmedActionToolHandler
{
    private async Task<PreviewBuildResult> BuildPharmacyPreviewAsync(
        AiPendingToolAction action,
        JsonElement args,
        string facilityLabel,
        DateTimeOffset validatedAt,
        DateTimeOffset expiresAt,
        string operation,
        CancellationToken ct)
    {
        var prescriptionId = GetLong(args, "prescriptionId")!.Value;
        var prescription = await _db.Prescriptions.AsNoTracking().Where(x => x.Id == prescriptionId)
            .Select(x => new { x.Id, x.PatientId, x.PatientVisitId, x.AppointmentId, x.Status, x.CreatedAt })
            .FirstOrDefaultAsync(ct);
        if (prescription == null)
            return PreviewBuildResult.Invalid("PREVIEW_DATA_UNAVAILABLE", "Đơn thuốc không còn tồn tại để hiển thị preview.");
        var encounter = prescription.PatientVisitId.HasValue
            ? await _db.PatientVisits.AsNoTracking().Where(x => x.Id == prescription.PatientVisitId.Value).Select(x => new { x.VisitCode, x.FacilityId, x.DepartmentId }).FirstOrDefaultAsync(ct)
            : null;
        var appointment = prescription.AppointmentId.HasValue
            ? await _db.Appointments.AsNoTracking().Where(x => x.Id == prescription.AppointmentId.Value).Select(x => new { x.AppointmentCode, x.FacilityId }).FirstOrDefaultAsync(ct)
            : null;
        var currentFacilityId = encounter?.FacilityId ?? appointment?.FacilityId;
        if (!currentFacilityId.HasValue || currentFacilityId != action.FacilityId)
            return PreviewBuildResult.Invalid("PREVIEW_DATA_UNAVAILABLE", "Không xác minh được cơ sở của đơn thuốc.");
        var department = encounter?.DepartmentId is long departmentId ? await PreviewDepartmentAsync(departmentId, action.FacilityId, ct) : null;
        var lines = await _db.PrescriptionItems.AsNoTracking().Where(x => x.PrescriptionId == prescriptionId)
            .Select(x => new { x.MedicineId, x.Quantity, MedicineCode = x.Medicine!.Code, MedicineName = x.Medicine.Name, Unit = x.Medicine.Unit, MedicineActive = x.Medicine.IsActive }).ToListAsync(ct);
        if (lines.Count == 0 || lines.Any(x => !x.MedicineActive || string.IsNullOrWhiteSpace(x.MedicineCode) || string.IsNullOrWhiteSpace(x.MedicineName) || string.IsNullOrWhiteSpace(x.Unit)))
            return PreviewBuildResult.Invalid("PREVIEW_DATA_UNAVAILABLE", "Không đủ dữ liệu thuốc đang hoạt động để hiển thị preview.");
        var resource = new AiToolActionPreviewResource
        {
            Identity = $"Đơn thuốc {AiDisplayText.PrescriptionCode(prescription.Id, prescription.CreatedAt)}",
            Facility = facilityLabel,
            Department = department?.Label,
            Subject = PatientLabel(prescription.PatientId),
            Encounter = encounter?.VisitCode ?? appointment?.AppointmentCode,
            CurrentStatus = prescription.Status.ToString()
        };
        var operationLabel = operation == "reserve" ? "Giữ chỗ thuốc theo đơn" : "Cấp phát đơn thuốc";
        return PreviewBuildResult.Valid(CreatePreview(action, resource,
            $"Sau khi xác nhận, backend sẽ thực hiện {operationLabel.ToLowerInvariant()} theo điều kiện thanh toán và tồn kho hiện tại.",
            "Backend đã tải đơn thuốc, các dòng thuốc, ca khám và cơ sở từ database; trạng thái thanh toán, tồn kho, quyền và version sẽ được kiểm tra lại khi xác nhận.",
            new[] { new AiToolActionPreviewChange { Kind = operation == "reserve" ? "prescription_reservation" : "prescription_dispense", Summary = operationLabel, Items = lines.Select(x => Item($"Thuốc {x.MedicineCode}", x.MedicineName, x.Quantity, x.Unit)).ToList() } },
            validatedAt, expiresAt,
            new[] { new AiToolDataSource("prescriptions", "database"), new AiToolDataSource("medicines", "database"), new AiToolDataSource("patient_visits", "database") }));
    }

}
