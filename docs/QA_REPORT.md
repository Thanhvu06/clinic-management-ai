# Báo Cáo Kiểm Thử (QA Report) - Phân Hệ Lâm Sàng & Cận Lâm Sàng

**Thời điểm thực hiện:** Final Patch (PR #1 - `fix/ci-billing-hardening`)  
**Target Base:** `feat/doctor-clinical-workspace`  
**Phạm vi:** Backend (.NET 10), Frontend (React 19 + TypeScript), EF Core Migrations, Longitudinal Anthropometrics & Vitals, Diagnostic Catalog & Orders, Technician Workflow, Doctor Review, Patient Diagnostic Results, Consultation Completion Guards.

---

## 1. Kết Quả Kiểm Thử Tự Động Hóa Trong CI (GitHub Actions)

Các kiểm thử dưới đây chạy hoàn toàn tự động trong CI, không phụ thuộc service ngoài:

| Hạng mục | Công cụ | Kết quả | Chi tiết |
|---|---|---|---|
| **Backend Build** | `dotnet build -c Release --no-incremental` | **PASS — 0 errors, 0 warnings** | Sạch cảnh báo trên tất cả 5 projects |
| **Backend Integration Tests** | `dotnet test -c Release` | **137/137 PASSED** | SQLite In-Memory, ~29s, 0 EF Core sentinel warnings |
| **EF Core Model Drift** | `dotnet ef migrations has-pending-model-changes` | **PASS — 0 pending changes** | Migration `20260908072449_RemoveDiagnosticStatusDefaultValues` đồng bộ ModelSnapshot |
| **Frontend Build** | `npm run build` (tsc + Vite) | **PASS — 0 errors** | Bundle dist thành công, typing nghiêm ngặt |
| **Frontend Linter** | `npm run lint` (Oxlint) | **PASS — 80 warnings, 0 errors** | Dưới ngưỡng 81 warnings |
| **Frontend Unit/Component Tests** | `npm test -- --run` (Vitest) | **70/70 PASSED — 12 suites** | 0 failed, 0 skipped |

> **Lưu ý cảnh báo:** Các con số trên phản ánh riêng từng hạng mục:  
> • "0 warnings" ở Backend Build = compiler warnings (.NET/Roslyn).  
> • "80 warnings" ở Frontend Linter = Oxlint lint warnings (không phải compiler errors).  
> • EF Core runtime warnings về `DiagnosticOrder.Status` và `DiagnosticOrderItem.Status` đã được loại bỏ triệt để bằng migration `RemoveDiagnosticStatusDefaultValues`, loại bỏ `HasDefaultValue()` cả trong configuration và ModelSnapshot.

---

## 2. Chi Tiết Test Suites Trọng Tâm

### `DiagnosticOrderWorkflowTests` — Đúng 8 [Fact] methods

1. **`Given_DoctorAndPatient_When_FullDiagnosticOrderWorkflowExecuted_Then_TransitionsCorrectly_And_BlocksConsultationUntilReviewed`**  
   Bác sĩ chỉ định → KTV tiếp nhận (`InProgress`) → KTV nhập kết quả từng dịch vụ → KTV hoàn tất (`Completed`) → Bác sĩ đối chiếu và xác nhận → Hoàn tất ca khám → Bệnh nhân tra cứu kết quả.
2. **`Given_PendingOrder_When_DoctorCancels_Then_StatusBecomesCancelled`**  
   Bác sĩ hủy chỉ định ở trạng thái `Ordered`; ca khám được phép hoàn tất bình thường sau khi hủy.
3. **`Given_DiagnosticOrder_When_SerializedToJson_Then_ContractMatchesFrontendExpectations`**  
   JSON payload trả về chứa chính xác `specialtyName`, `category`, `preparationInstructions`; không chứa `orderingDoctorSpecialty` hoặc `serviceCategory`.
4. **`Given_CrossTenantUsers_When_AccessingDiagnosticOrder_Then_IsolationIsEnforcedWith404OrExclusion`**  
   Bác sĩ B không thể xem/hủy/duyệt chỉ định của Bác sĩ A (HTTP 404). Bệnh nhân B không thể xem chỉ định của Bệnh nhân A.
5. **`Given_RoleSecurity_When_UnauthorizedRolesAccessEndpoints_Then_ReturnsForbiddenOrUnauthorized`**  
   Client chưa xác thực → 401; Bệnh nhân/Lễ tân/Dược sĩ truy cập API Kỹ thuật viên → 403; Kỹ thuật viên tạo chỉ định bác sĩ → 403.
6. **`Given_InvalidService_When_CreatingOrder_Then_ValidationRejects_Before_Transaction`**  
   Kiểm chứng rằng validation layer từ chối request trước khi bất kỳ transaction nào được mở; không để lại bản ghi rác.
7. **`Given_TransactionRollback_When_AuditLogSaveFailsAfterOrderInserted_Then_NoDiagnosticDataPersisted`**  
   Atomicity test chuẩn xác: Sử dụng `SaveFailureInterceptor` (EF Core `SaveChangesInterceptor`) inject lỗi có kiểm soát tại lần `SaveChangesAsync` thứ hai (sau khi `DiagnosticOrder` và `DiagnosticOrderItem` đã flush vào transaction nhưng trước khi audit log + notification commit). Test assert interceptor đã kích hoạt (`WasTriggered == true`, `SaveCallCount == 2`) và so sánh đối chiếu toàn bộ số lượng bản ghi DB sau rollback bằng đúng baseline trước request (không dùng assertion lỏng lẻo).
8. **`Given_NonOrderCodeDbUpdateException_When_CreatingOrder_Then_RethrownAndNotMappedToCollision`**  
   Kiểm chứng rằng `DbUpdateException` không liên quan đến unique constraint `OrderCode` (ví dụ lỗi trên bảng khác) KHÔNG bị nuốt hay chuyển thành HTTP 409 `ORDER_CODE_COLLISION`. Request trả về HTTP 500 `INTERNAL_SERVER_ERROR`, transaction rollback sạch sẽ về đúng baseline ban đầu.

### `PatientVitalHistoryTests`
- Kiểm chứng đối chiếu nhân trắc dọc (`AnthropometricComparisonDto`).
- Kiểm chứng tính ΔWeight và ΔBMI giữa 2 lần khám liên tiếp.

### `bmiCalculation.test.ts` — 6 tests (Vitest)
- BMI formula: 68.5 kg / (1.72 m)² = 23.2 → nhãn *"Bình thường"* (< 25.0).
- Ngưỡng phân loại: Thiếu cân (< 18.5), Bình thường (< 25.0), Thừa cân / Tiền béo phì (< 30.0), Béo phì (≥ 30.0).

### `diagnosticWorkflow.test.tsx` — 5 tests (Vitest)
- `TechnicianDashboard`: Hàng đợi chỉ định và thẻ KPI (Ordered, InProgress, Completed).
- `DiagnosticOrderPrint`: Phiếu in chuẩn, hiển thị `preparationInstructions`, fallback giới tính "Chưa cập nhật", fallback chuyên khoa "---" hoặc "Chưa cập nhật" (không dùng dữ liệu suy đoán).
- `PatientDiagnosticResults`: Accordion kết quả, nhãn "Đã có kết luận bác sĩ", khoảng tham chiếu.
- API error state handling.

---

## 3. Kiểm Thử Thực Tế Trên Môi Trường Local (Local API Verification)

Script [`../scripts/e2e/diagnostic-workflow.mjs`](../scripts/e2e/diagnostic-workflow.mjs) giao tiếp HTTP API với backend chạy local (SQL Server / Dev DB).

> ⚠️ **Phạm vi:** Script chỉ kiểm thử **HTTP API endpoints**. Nó KHÔNG kiểm thử giao diện React, render trang in, hay bất kỳ thành phần frontend nào.  
> ⚠️ **Hàng rào an toàn nghiêm ngặt:**  
> • Yêu cầu chính xác `process.env.E2E_ALLOW_MUTATION === "true"` (từ chối `false`, `0`, `yes`, rỗng).  
> • Mặc định chỉ cho phép chạy trên hostname cục bộ: `localhost`, `127.0.0.1`, `::1`.  
> • Chạy trên remote staging yêu cầu biến riêng `E2E_ALLOW_REMOTE_STAGING=true`.  
> • Tuyệt đối không chạy trên database dùng để thuyết trình hay production.  
> ⚠️ **Lưu ý CI:** Script này là công cụ kiểm thử cục bộ (Local API E2E), **không nằm trong GitHub Actions workflow** của PR.

| Bước | Kiểm thử | Kết quả (khi chạy local) |
|:---:|---|:---:|
| **1** | Xác thực đa vai trò (explicit credentials) | PASS |
| **2** | Catalog services & canonical contract schema | PASS |
| **3** | Tạo lịch hẹn kiểm thử riêng biệt + InConsultation | PASS |
| **4** | Ghi nhận sinh hiệu giả lập, tính BMI 23.2 | PASS |
| **5** | Bác sĩ tạo phiếu CLS + kiểm chứng DTO contract | PASS |
| **6** | Completion Guard #1 (HTTP 422 `PENDING_DIAGNOSTIC_RESULTS`) | PASS |
| **7** | Kỹ thuật viên: tiếp nhận, nhập kết quả giả lập, hoàn tất | PASS |
| **8** | Completion Guard #2 (HTTP 422 `UNREVIEWED_DIAGNOSTIC_RESULTS`) | PASS |
| **9** | Bác sĩ duyệt kết quả + kiểm chứng `reviewedAtUtc` | PASS |
| **10** | Bác sĩ hoàn tất ca khám (có prefix test data) | PASS |
| **11** | Cross-tenant isolation: Bệnh nhân B nhận 404 với phiếu của Bệnh nhân A | PASS |

---

## 4. Tổng Kết Chất Lượng & Bằng Chứng Cục Bộ

- **Backend**: 137/137 integration tests passed, 0 compiler errors/warnings, 0 EF Core sentinel warnings cho `DiagnosticOrder.Status` và `DiagnosticOrderItem.Status`.
- **Migration & Schema**: ModelSnapshot đồng bộ 100% với entity configuration qua migration `20260908072449_RemoveDiagnosticStatusDefaultValues`.
- **Frontend**: 70/70 Vitest tests passed, 0 build errors, 80 Oxlint warnings (< 81 ngưỡng).
- **Tính ổn định (Local Evidence)**: `DiagnosticOrderWorkflowTests` (8 tests) chạy 10 lần liên tiếp thành công 100% (80/80 runs passed).
- **Tương thích ngược**: 10 bác sĩ, chuyên khoa, lịch làm việc seeded không bị thay đổi.
