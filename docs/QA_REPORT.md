# Báo Cáo Kiểm Thử (QA Report)

**Thời điểm thực hiện:** Giai đoạn CI, Billing & Pharmacy Hardening (PR #1 - `fix/ci-billing-hardening`)
**Commit SHA:** HEAD (Cập nhật hoàn thiện Pharmacy Dispensing & Inventory)
**Target Base:** `feat/doctor-clinical-workspace`
**Phạm vi:** Toàn bộ hệ thống Backend (.NET 10), Frontend (React 19 + TypeScript), Billing & Invoicing, Revisit Booking, In-App Notification Center, Doctor Workspace, Database Seeding, và hoàn thiện **Pharmacy Dispensing & Medicine Inventory Workflow**.

---

## 1. Kết quả Build & Automated Tests

Toàn bộ quy trình kiểm thử tự động đã được kiểm chứng độc lập ở cả cấu hình Debug và Release:

- **Backend Build (`dotnet build`):** Pass - 0 Error(s), 0 Warning(s) trên cả `Debug` và `Release`.
- **Backend Tests (`dotnet test`):** **90/90 passed (0 failed, 0 skipped)**
  - `PharmacyDispenseTests`: 11/11 passed
    1. Cấp phát thành công: đơn đổi `Dispensed`, tồn kho trừ chính xác, sinh `MedicineStockTransaction` (Type = Dispense, QuantityChange âm, BalanceAfter cập nhật), lưu `DispensedAt` và `DispensedByUserId`.
    2. Đơn gồm nhiều thuốc: toàn bộ thuốc giảm đúng số lượng.
    3. Đơn gồm nhiều thuốc: nếu 1 thuốc thiếu tồn kho thì rollback toàn bộ giao dịch, không thuốc nào bị trừ tồn kho (HTTP 422 `INSUFFICIENT_MEDICINE_STOCK`).
    4. Thuốc ngừng hoạt động (`IsActive = false`): chặn cấp phát, báo lỗi nghiệp vụ rõ ràng, không trừ kho (HTTP 422 `MEDICINE_INACTIVE`).
    5. Đơn ở trạng thái `Draft` hoặc `Cancelled`: chặn cấp phát (HTTP 422 `PRESCRIPTION_NOT_DISPENSABLE`).
    6. Đơn đã cấp phát trước đó: chặn cấp phát trùng lặp (HTTP 409 `PRESCRIPTION_ALREADY_DISPENSED`).
    7. Concurrency / Race Condition cùng 1 đơn thuốc: 2 request đồng thời -> đúng 1 request thành công (HTTP 200), request còn lại bị chặn Conflict (HTTP 409), tồn kho chỉ trừ 1 lần duy nhất.
    8. Concurrency Stock tranh chấp tồn kho cuối: 2 đơn thuốc cùng tranh chấp lượng thuốc còn lại -> chỉ đơn đầu thành công, đơn sau bị từ chối, tồn kho không bao giờ âm.
    9. RBAC: Pharmacist và Admin cấp phát thành công (200 OK); Doctor, Receptionist, Patient bị từ chối (HTTP 403 Forbidden); Unauthenticated bị chặn (HTTP 401 Unauthorized).
    10. In-App Notification: Bệnh nhân nhận được thông báo thật với route `/patient/prescriptions`, type `Prescription`, entityId trỏ đúng đơn thuốc.
    11. Data Isolation & Privacy: Patient không xem được danh mục tồn kho nội bộ (`GET /api/v1/medicines/active` trả 403); Patient chỉ xem đơn thuốc của chính mình; Doctor chỉ quản lý lịch và đơn thuốc thuộc ca khám của mình.
  - `BillingTests`: 10/10 passed (Validation phí chuyên khoa, kiểm soát range 365 ngày, snapshot giá không đổi khi phí gốc cập nhật, chống double payment, audit log, KPI doanh thu).
  - `RevisitWorkflowTests`: 6/6 passed (Bác sĩ đề xuất tái khám, route `/patient/revisit`, Notification ID thực không hardcode "0", bệnh nhân chỉ đặt được đề xuất của chính mình, phân định trạng thái `HoldingSlotStatuses` vs `ReleasedSlotStatuses`, thông báo lễ tân `/reception/appointments`).
  - `AppointmentConcurrencyTests`: 2/2 passed (Chống race-condition đặt trùng slot đồng thời với Serializable transaction).
  - `DoctorWorkflowTests` & `DoctorIsolationTests`: 15/15 passed (Cách ly hàng đợi và lịch khám giữa các bác sĩ, cập nhật diễn tiến lâm sàng, kết thúc khám).
  - `PatientPrivacyTests`: 11/11 passed (Cách ly hóa đơn bệnh nhân, bảo vệ PII, tra cứu công khai che số điện thoại và tên).
  - Các bộ test Identity, JWT, Schedule & Leave Requests: 35/35 passed.
- **Frontend Linter (`npm run lint`):** Pass - 0 error, 84 minor warnings (cho phép).
- **Frontend Tests (`npm run test`):** **53/53 passed (0 failed across 10 test suites)**
  - `pharmacyPrescriptions.test.tsx`: 4/4 passed (Render danh sách đơn thuốc, xem chi tiết và tồn kho khả dụng, disable nút cấp thuốc khi tồn kho không đủ, hiển thị badge và cảnh báo khi thuốc ngừng hoạt động `IsActive = false`).
  - `revisitBookingHelper.test.ts`: Format ngày giờ tái khám theo tiêu chuẩn ca khám y tế.
  - `billing.test.ts`: Format tiền tệ VND, mapping trạng thái hóa đơn, phân quyền hóa đơn lễ tân và bệnh nhân.
  - `notifications.test.ts` & `notificationBell.test.ts`: Hiển thị thông báo thật, polling/mark-as-read, badge số đếm.
  - `doctorDashboard.test.ts` & `doctorQueue.test.ts`: Bộ lọc hàng đợi, tìm kiếm ca khám, tiếp nhận bệnh nhân.
  - `roleRoutes.test.ts`: Kiểm soát phân quyền route theo vai trò người dùng.
- **Frontend Build (`npm run build`):** Pass - `tsc -b && vite build` tạo bundle thành công, 0 lỗi biên dịch type.

---

## 2. Kiểm thử Thực tế E2E (Live System Verification)

Hệ thống được khởi động live tại Backend (`http://localhost:5258`) và Frontend (`http://localhost:5173`). Kịch bản E2E kiểm tra dữ liệu thật trên DB với 12 bước hoàn chỉnh cho module Pharmacy:

| STT | Bước kiểm tra | Kết quả | Minh chứng thực tế |
|---|---|---|---|
| 1 | **Pharmacist Login** | **Pass** | Đăng nhập tài khoản `pharmacist@cliniccare.local` lấy JWT token thành công. |
| 2 | **Check Pharmacy Dashboard** | **Pass** | Endpoint `GET /api/v1/pharmacy/dashboard` trả về số lượng đơn chờ cấp phát và thuốc sắp hết tồn kho. |
| 3 | **Query Prescriptions Queue** | **Pass** | Endpoint `GET /api/v1/pharmacy/prescriptions?status=Issued` trả về danh sách đơn thuốc đang chờ phát. |
| 4 | **View Prescription Detail & Stock** | **Pass** | Xem chi tiết đơn thuốc #1: 3 mục thuốc (Paracetamol 500mg, Amoxicillin 500mg, Vitamin C 500mg) kèm tồn kho thật trong DB. |
| 5 | **Dispense Prescription Atomic** | **Pass** | Cấp phát đơn #1 thành công -> `POST /api/v1/pharmacy/prescriptions/1/dispense` trả HTTP 200, ghi nhận `dispensedAt`. |
| 6 | **Verify Stock Reduction** | **Pass** | Tồn kho Paracetamol trước cấp: 500, sau cấp: 490 (trừ chính xác 10 viên, trạng thái đổi `Dispensed`). |
| 7 | **Prevent Duplicate Dispense** | **Pass** | Thử cấp phát lại đơn #1 lần 2 -> Chặn ngay lập tức với HTTP 409 Conflict (`PRESCRIPTION_ALREADY_DISPENSED`). |
| 8 | **Verify Stock Audit Transaction** | **Pass** | Bảng `MedicineStockTransactions` ghi nhận giao dịch: Type `Dispense`, `QuantityChange = -10`, `BalanceAfter = 490`, `ActorName = Dược sĩ Lâm Sàng`, liên kết `PrescriptionId = 1`. |
| 9 | **RBAC Enforce on Dispense** | **Pass** | Gọi cấp phát đơn thuốc từ Doctor, Receptionist, Patient -> Đều bị chặn chính xác với HTTP 403 Forbidden. |
| 10| **Internal Inventory Privacy** | **Pass** | Bệnh nhân gọi `GET /api/v1/medicines/active` và `GET /api/v1/pharmacy/prescriptions` -> Bị chặn với HTTP 403 Forbidden. |
| 11| **Patient In-App Notification & Rx View** | **Pass** | Bệnh nhân nhận thông báo: Title `Đơn thuốc đã được phát`, Route `/patient/prescriptions`. Xem được đơn thuốc tại trang bệnh nhân. |
| 12| **Frontend Web Server Health** | **Pass** | Web Frontend phản hồi HTTP 200 tại `http://localhost:5173`. |

---

## 3. Data Safety, Privacy & Security Audit

1. **RBAC & Data Isolation:**
   - Dược sĩ và Quản trị viên: Có quyền truy cập kho thuốc, xem danh sách đơn thuốc và thực hiện cấp phát.
   - Bác sĩ chỉ truy cập được hàng đợi, bệnh án và lịch làm việc của chính mình.
   - Bệnh nhân chỉ xem được hóa đơn, thông báo và đơn thuốc thuộc sở hữu của mình; không thể truy cập danh mục thuốc nội bộ hay hàng đợi nhà thuốc.
   - Lễ tân không có quyền cấu hình mức phí chuyên khoa và không được phép cấp phát thuốc.
2. **Transaction Isolation & Concurrency Safety:**
   - Cấp phát thuốc thực hiện trong transaction với mức cô lập `Serializable`.
   - Tranh chấp đơn thuốc hoặc tồn kho đồng thời được xử lý an toàn: trả HTTP 409 `DISPENSE_CONFLICT`, không gây dirty write hay số lượng tồn kho âm.
3. **Public Lookup Safety:**
   - Yêu cầu chuỗi tìm kiếm tối thiểu 4 ký tự.
   - Che mặt định danh cá nhân (PII): họ tên bệnh nhân chỉ hiển thị dạng ký tự đầu và dấu sao, số điện thoại được che giữa.
4. **Slot Policy State Machine:**
   - Trạng thái giữ slot (`HoldingSlotStatuses`): `Pending`, `Confirmed`, `PendingReschedule`, `PendingCancellation`, `CheckedIn`, `InConsultation`.
   - Trạng thái giải phóng slot (`ReleasedSlotStatuses`): `Cancelled`, `Completed`, `NoShow`.
5. **Data Integrity & Consistency:**
   - Giá trên hóa đơn lấy snapshot tại thời điểm lập hóa đơn, không bị hồi tố khi Admin thay đổi giá chuyên khoa.
   - Giới hạn tra cứu báo cáo doanh thu tối đa 365 ngày, xử lý ngày giờ theo múi giờ chuẩn Việt Nam (UTC+7).

---

## 4. Hạn Chế Còn Lại & Khuyến Nghị Vận Hành

- **Không tích hợp cổng thanh toán trực tuyến:** Phase này chỉ hỗ trợ thanh toán tại quầy (`Cash` và `ManualBankTransfer`) theo đúng phạm vi nghiệp vụ.
- **Tách bạch thanh toán và cấp phát:** Hóa đơn lâm sàng tách biệt với đơn thuốc, dược sĩ chỉ cấp phát đơn khi đơn ở trạng thái `Issued`.
- **Chủ nhật không có lịch khám:** Hệ thống mặc định lịch làm việc từ Thứ Hai đến Thứ Bảy, cần lưu ý khi chọn ngày khám trên giao diện.

---

## 5. Kết Luận

- Trạng thái mã nguồn: **Sạch, chuẩn hóa, không có thay đổi rác hay secret bị lộ**.
- CI local: **100% Pass** (Backend 90/90, Frontend 53/53, Lint 0 error, Build 0 error).
- E2E: **12/12 bước kiểm thử Pharmacy Dispensing & Medicine Inventory hoạt động ổn định trên môi trường thực tế**.
- Nhánh `fix/ci-billing-hardening` đã hoàn thiện toàn diện và sẵn sàng merge vào `feat/doctor-clinical-workspace`.
