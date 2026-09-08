# Báo Cáo Kiểm Thử (QA Report) - Phân Hệ Lâm Sàng & Cận Lâm Sàng

**Thời điểm thực hiện:** Final Correction Audit Round 2 (PR #1 - `fix/ci-billing-hardening`)  
**Target Base:** `feat/doctor-clinical-workspace`  
**Phạm vi:** Backend (.NET 10), Frontend (React 19 + TypeScript), Longitudinal Anthropometrics & Vitals, Diagnostic Catalog & Orders, Technician Workflow, Doctor Review, Patient Diagnostic Results, Consultation Completion Guards.

---

## 1. Kết Quả Kiểm Thử Tự Động Hóa Trong CI (GitHub Actions)

Các kiểm thử dưới đây chạy hoàn toàn tự động trong CI, không phụ thuộc service ngoài:

| Hạng mục | Công cụ | Kết quả | Chi tiết |
|---|---|---|---|
| **Backend Build** | `dotnet build -c Release --no-incremental` | **PASS — 0 errors, 0 warnings** | Sạch cảnh báo trên tất cả 5 projects |
| **Backend Integration Tests** | `dotnet test -c Release` | **136/136 PASSED** | SQLite In-Memory, ~29s, 0 EF Core sentinel warnings |
| **Frontend Build** | `npm run build` (tsc + Vite) | **PASS — 0 errors** | Bundle dist thành công, typing nghiêm ngặt |
| **Frontend Linter** | `npm run lint` (Oxlint) | **PASS — 80 warnings, 0 errors** | Dưới ngưỡng 81 warnings |
| **Frontend Unit/Component Tests** | `npm test -- --run` (Vitest) | **70/70 PASSED — 12 suites** | 0 failed, 0 skipped |

> **Lưu ý compiler warnings:** Các con số trên phản ánh riêng từng hạng mục:  
> • "0 warnings" ở Backend Build = compiler warnings (.NET/Roslyn).  
> • "80 warnings" ở Frontend Linter = Oxlint lint warnings (không phải compiler errors).  
> • EF Core runtime warnings về `DiagnosticOrder.Status` và `DiagnosticOrderItem.Status` đã được loại bỏ bằng cách bỏ `HasDefaultValue()` khỏi EF Core configuration.

---

## 2. Chi Tiết Test Suites Trọng Tâm

### `DiagnosticOrderWorkflowTests` — 8 [Fact] methods

1. **Full Workflow End-to-End** — Bác sĩ chỉ định → KTV tiếp nhận (`InProgress`) → KTV nhập kết quả từng dịch vụ → KTV hoàn tất (`Completed`) → Bác sĩ đối chiếu và xác nhận → Hoàn tất ca khám → Bệnh nhân tra cứu kết quả.
2. **Order Cancellation Guard** — Bác sĩ hủy chỉ định ở trạng thái `Ordered`; ca khám được phép hoàn tất bình thường sau khi hủy.
3. **Canonical Contract Verification** — JSON payload trả về chứa chính xác `specialtyName`, `category`, `preparationInstructions`; không chứa `orderingDoctorSpecialty` hoặc `serviceCategory`.
4. **Cross-Tenant Isolation** — Bác sĩ B không thể xem/hủy/duyệt chỉ định của Bác sĩ A (HTTP 404). Bệnh nhân B không thể xem chỉ định của Bệnh nhân A.
5. **Role Security** — Client chưa xác thực → 401; Bệnh nhân/Lễ tân/Dược sĩ truy cập API Kỹ thuật viên → 403; Kỹ thuật viên tạo chỉ định bác sĩ → 403.
6. **Input Validation (Invalid Service)** — Kiểm chứng rằng validation layer từ chối request trước khi bất kỳ transaction nào được mở; không để lại bản ghi rác.
7. **Non-OrderCode DbUpdateException Rethrown** — Kiểm chứng rằng `DbUpdateException` không liên quan đến `OrderCode` unique constraint KHÔNG bị chuyển thành `ORDER_CODE_COLLISION`.
8. **Transaction Atomicity (True Rollback)** — Sử dụng `SaveFailureInterceptor` inject lỗi có kiểm soát SAU `SaveChangesAsync` đầu tiên (DiagnosticOrder + DiagnosticOrderItem đã ghi) nhưng TRƯỚC commit audit-log + notification. Sau thất bại, kiểm chứng bằng query trực tiếp DB rằng không có `DiagnosticOrder`, `DiagnosticOrderItem`, `SystemAuditLog`, hoặc `Notification` nào liên quan đến request đó tồn tại.

### `PatientVitalHistoryTests`
- Kiểm chứng đối chiếu nhân trắc dọc (`AnthropometricComparisonDto`).
- Kiểm chứng tính ΔWeight và ΔBMI giữa 2 lần khám liên tiếp.

### `bmiCalculation.test.ts` — 6 tests (Vitest)
- BMI formula: 68.5 kg / (1.72 m)² = 23.2 → nhãn *"Bình thường"* (< 25.0).
- Ngưỡng phân loại: Thiếu cân (< 18.5), Bình thường (< 25.0), Thừa cân / Tiền béo phì (< 30.0), Béo phì (≥ 30.0).

### `diagnosticWorkflow.test.tsx` — 5 tests (Vitest)
- `TechnicianDashboard`: Hàng đợi chỉ định và thẻ KPI (Ordered, InProgress, Completed).
- `DiagnosticOrderPrint`: Phiếu in chuẩn, hiển thị `preparationInstructions`, fallback giới tính "Chưa cập nhật", fallback chuyên khoa "---" hoặc "Chưa cập nhật" (không dùng "Chuyên khoa Lâm sàng" — đây là dữ liệu suy đoán).
- `PatientDiagnosticResults`: Accordion kết quả, nhãn "Đã có kết luận bác sĩ", khoảng tham chiếu.
- API error state handling.

---

## 3. Kiểm Thử Thực Tế Trên Môi Trường Local (Local API Verification)

Script [`../scripts/e2e/diagnostic-workflow.mjs`](../scripts/e2e/diagnostic-workflow.mjs) giao tiếp HTTP API với backend chạy local (SQL Server / Dev DB).

> ⚠️ **Phạm vi:** Script chỉ kiểm thử **HTTP API endpoints**. Nó KHÔNG kiểm thử giao diện React, render trang in, hay bất kỳ thành phần frontend nào.  
> ⚠️ **Yêu cầu:** Phải set `E2E_ALLOW_MUTATION=true` và cung cấp đầy đủ credentials qua env vars trước khi chạy.  
> ⚠️ **Môi trường:** Chỉ chạy trên Dev/Staging DB. Script từ chối chạy nếu `API_BASE_URL` chứa "prod".  
> ⚠️ **Script này KHÔNG nằm trong GitHub Actions workflow** (chưa tích hợp vào CI YAML).

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

## 4. Tổng Kết Chất Lượng

- **Backend**: 136/136 integration tests passed, 0 compiler errors/warnings, 0 EF Core sentinel warnings cho `DiagnosticOrder.Status` và `DiagnosticOrderItem.Status`.
- **Frontend**: 70/70 Vitest tests passed, 0 build errors, 80 Oxlint warnings (< 81 ngưỡng).
- **Tính ổn định**: Tests chạy trên SQLite In-Memory isolated DB — không có shared state giữa test suites. Chưa có đủ dữ liệu để tuyên bố "Zero Flakiness" dài hạn.
- **Tương thích ngược**: 10 bác sĩ, chuyên khoa, lịch làm việc seeded không bị thay đổi.
