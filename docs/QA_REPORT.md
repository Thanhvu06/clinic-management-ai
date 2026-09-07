# Báo Cáo Kiểm Thử (QA Report) - Toàn Diện Phân Hệ Lâm Sàng & Cận Lâm Sàng

**Thời điểm thực hiện:** Vòng Correction Audit Cuối (PR #1 - `fix/ci-billing-hardening`)  
**Commit SHA:** HEAD  
**Target Base:** `feat/doctor-clinical-workspace`  
**Phạm vi:** Toàn bộ hệ thống Backend (.NET 10), Frontend (React 19 + TypeScript), Longitudinal Anthropometrics & Vitals, Diagnostic Catalog & Orders, Technician Workflow, Doctor Review, Patient Diagnostic Results, và Consultation Completion Guards.

---

## 1. Kết Quả Kiểm Thử Tự Động Hóa Trong Repo & CI (Automated Verification in Repository/CI)

Tất cả các kiểm thử dưới đây chạy độc lập, tự động hóa hoàn toàn và sẵn sàng chạy trên GitHub Actions CI mà không phụ thuộc vào bất kỳ service bên ngoài nào:

| Hạng mục kiểm thử | Công cụ thực hiện | Kết quả đạt được | Chi tiết kỹ thuật |
|---|---|---|---|
| **Backend Build** | `dotnet build ClinicManagement.sln -c Release --no-incremental` | **PASS (0 errors, 0 warnings)** | Sạch hoàn toàn cảnh báo trên cả 5 projects |
| **Backend Integration Tests** | `dotnet test -c Release` | **135/135 PASSED (0 failed, 0 skipped)** | 100% deterministic (SQLite In-Memory), thời gian chạy ~28s |
| **Frontend Build** | `npm.cmd run build` (tsc + vite) | **PASS (0 errors)** | Bundle dist thành công, typing nghiêm ngặt |
| **Frontend Linter** | `npm.cmd run lint` (Oxlint) | **PASS (80 warnings, 0 errors)** | Giảm từ baseline 81 xuống 80 cảnh báo (đạt chỉ tiêu $\le 81$) |
| **Frontend Unit/Component Tests** | `npm.cmd test -- --run` (Vitest) | **70/70 PASSED (12/12 suites)** | 100% passed across all component & logic suites |

### Chi tiết các Test Suites Trọng Tâm:
1. **`DiagnosticOrderWorkflowTests` (Backend - Integration Tests - 5 tests):**
   - **Full Workflow:** Bác sĩ chỉ định $\rightarrow$ KTV tiếp nhận (`InProgress`) $\rightarrow$ KTV nhập kết quả từng dịch vụ $\rightarrow$ KTV hoàn tất (`Completed`) $\rightarrow$ Bác sĩ đối chiếu và xác nhận (`Reviewed`) $\rightarrow$ Hoàn tất ca khám.
   - **Guard Chặn #1 (`PENDING_DIAGNOSTIC_RESULTS` - HTTP 422):** Ngăn bác sĩ hoàn tất khám khi chỉ định đang ở trạng thái `Ordered` hoặc `InProgress`.
   - **Guard Chặn #2 (`UNREVIEWED_DIAGNOSTIC_RESULTS` - HTTP 422):** Ngăn bác sĩ hoàn tất khám khi KTV đã hoàn thành nhưng bác sĩ chưa bấm xác nhận đã xem.
   - **Order Cancellation:** Bác sĩ hủy chỉ định khi còn ở trạng thái `Ordered`; ca khám được phép hoàn tất bình thường sau khi hủy.
   - **Canonical Contract Verification:** Kiểm chứng JSON payload trả về chứa chính xác thuộc tính `specialtyName`, `category`, `preparationInstructions`; tuyệt đối không chứa `orderingDoctorSpecialty` hoặc `serviceCategory`.
   - **Cross-Tenant Isolation:** Bác sĩ B không thể xem, hủy, hay duyệt chỉ định của Bác sĩ A (HTTP 404). Bệnh nhân B không thể xem chỉ định của Bệnh nhân A (HTTP 404 và loại khỏi danh sách).
   - **Role Boundaries:** Client chưa đăng nhập bị chặn HTTP 401; Bệnh nhân/Lễ tân/Dược sĩ truy cập API Kỹ thuật viên bị chặn HTTP 403; Kỹ thuật viên tạo chỉ định bác sĩ bị chặn HTTP 403.
   - **Transaction Atomicity:** Khi tạo chỉ định thất bại (dịch vụ không hợp lệ), toàn bộ transaction bị rollback và không để lại bản ghi rác.
2. **`PatientVitalHistoryTests` (Backend - Integration Tests):**
   - Kiểm chứng đối chiếu nhân trắc học dọc (`AnthropometricComparisonDto`).
   - Kiểm chứng tính $\Delta \text{Weight}$ và $\Delta \text{BMI}$ giữa 2 lần khám liên tiếp.
3. **`bmiCalculation.test.ts` (Frontend - Vitest - 6 tests):**
   - Kiểm chứng công thức tính BMI và phân loại theo cấu hình hệ thống: 68.5kg / 172cm $\rightarrow$ BMI 23.2 nhãn *"Bình thường"* (<25.0).
   - Kiểm chứng các ngưỡng: Thiếu cân (<18.5), Bình thường (<25.0), Thừa cân / Tiền béo phì (<30.0), Béo phì ($\ge 30.0$).
4. **`diagnosticWorkflow.test.tsx` (Frontend - Vitest - 5 tests):**
   - `TechnicianDashboard`: Hàng đợi chỉ định và thẻ KPI (Ordered, InProgress, Completed).
   - `DiagnosticOrderPrint`: Phiếu in chỉ định chuẩn y tế, thông tin phòng khám demo, mã phiếu DX, hiển thị `preparationInstructions` cho từng dịch vụ, xử lý an toàn giới tính không xác định ("Chưa cập nhật") và fallback chuyên khoa ("Chuyên khoa Lâm sàng" / "---").
   - `PatientDiagnosticResults`: Accordion kết quả xét nghiệm, nhãn "Đã có kết luận bác sĩ", khoảng tham chiếu và kết luận chuyên môn.
   - Bắt lỗi giao tiếp API và hiển thị thông báo lỗi phù hợp.

---

## 2. Kết Quả Kiểm Thử Thực Tế Trên Môi Trường Local (Live Runtime Verification)

Được thực hiện độc lập qua script tự động hóa [`scripts/e2e/diagnostic-workflow.mjs`](file:///c:/Users/LEGION/OneDrive/Máy%20tính/clinic-management-ai/scripts/e2e/diagnostic-workflow.mjs) giao tiếp trực tiếp qua HTTP RESTful API với Backend chạy trên môi trường thực tế (SQL Server / Localhost) và Frontend:

| Bước | Diễn giải kịch bản kiểm thử E2E | Kết quả | Ghi chú kỹ thuật |
|:---:|---|:---:|---|
| **1** | Xác thực đa vai trò (Patient, Receptionist, Doctor, Technician) | **PASS** | Đăng nhập lấy JWT Bearer token hợp lệ cho cả 4 actors |
| **2** | Tải danh mục dịch vụ cận lâm sàng (`GET /api/v1/diagnostic-services`) | **PASS** | Lấy đủ 10 dịch vụ chuẩn, xác nhận có `preparationInstructions` |
| **3** | Lễ tân check-in ca hẹn & Bác sĩ bắt đầu khám (`InConsultation`) | **PASS** | Chuyển trạng thái: Confirmed $\rightarrow$ CheckedIn $\rightarrow$ InConsultation |
| **4** | Ghi nhận sinh hiệu, tính BMI và so sánh nhân trắc dọc | **PASS** | 68.5kg / 172cm $\rightarrow$ BMI 23.2 nhãn "Bình thường" (<25.0) |
| **5** | Bác sĩ tạo phiếu chỉ định CLS gồm 2 dịch vụ (CBC + Siêu âm bụng) | **PASS** | Phiếu tạo thành công với mã `DX-2026xxxx-xxxx`, trạng thái `Ordered` |
| **6** | **Kiểm thử Completion Guard #1 (Phiếu đang chờ kết quả)** | **PASS** | **Chặn thành công:** HTTP 422 `PENDING_DIAGNOSTIC_RESULTS` |
| **7** | Bác sĩ truy xuất dữ liệu in phiếu chỉ định CLS | **PASS** | Route `/doctor/appointments/:id/examination`, in phiếu khớp hoàn toàn |
| **8** | Kỹ thuật viên kiểm tra hàng đợi và KPI thống kê (`/diagnostics`) | **PASS** | Phiếu hiển thị trong danh sách `Ordered`, thống kê tăng chính xác |
| **9** | Kỹ thuật viên tiếp nhận thực hiện phiếu chỉ định | **PASS** | Trạng thái chuyển sang `InProgress` |
| **10** | Kỹ thuật viên nhập kết quả, kết luận, khoảng tham chiếu từng mục | **PASS** | Ghi nhận thành công kết quả cho CBC và Siêu âm bụng tổng quát |
| **11** | Kỹ thuật viên hoàn tất phiếu chỉ định (`/diagnostics/orders/:id`) | **PASS** | Trạng thái phiếu chuyển sang `Completed` |
| **12** | **Kiểm thử Completion Guard #2 (Kết quả chưa được bác sĩ duyệt)** | **PASS** | **Chặn thành công:** HTTP 422 `UNREVIEWED_DIAGNOSTIC_RESULTS` |
| **13** | Bác sĩ xem, đối chiếu và xác nhận kết quả CLS | **PASS** | Cập nhật `ReviewedAtUtc` và `ReviewedByDoctorName` |
| **14** | Bác sĩ hoàn tất ca khám lâm sàng | **PASS** | Ca khám chuyển sang `Completed` thành công |
| **15.1**| **Bảo mật cách ly dữ liệu: Bệnh nhân khác kiểm tra phiếu** | **PASS** | Bệnh nhân khác KHÔNG THỂ nhìn thấy phiếu CLS này (404/không có trong list) |
| **15.2**| Bệnh nhân tra cứu kết quả cá nhân (`/patient/diagnostic-results`) | **PASS** | Thấy đầy đủ kết quả, kết luận và xác nhận duyệt của bác sĩ |

---

## 3. Tổng kết Chất lượng & Độ tin cậy (Conclusion)
- **Tương thích ngược & Tính toàn vẹn:** 100% dữ liệu 10 bác sĩ, chuyên khoa, lịch làm việc không bị thay đổi.
- **Tính ổn định (Zero Flakiness):** Toàn bộ 135 test backend và 70 test frontend chạy hoàn toàn độc lập, linter 80 cảnh báo (dưới ngưỡng 81).
- **Tiêu chuẩn an toàn y tế:** Các quy tắc chốt chặn hoàn tất khám bảo vệ tối đa tính chính xác của quyết định lâm sàng và dữ liệu người bệnh.

