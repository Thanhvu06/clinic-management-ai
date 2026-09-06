# Báo Cáo Kiểm Thử (QA Report)

**Thời điểm thực hiện:** Giai đoạn CI & Billing Hardening (PR #1 - `fix/ci-billing-hardening`)
**Commit SHA:** `5ee54e2` (HEAD)
**Target Base:** `feat/doctor-clinical-workspace`
**Phạm vi:** Toàn bộ hệ thống Backend (.NET 10), Frontend (React 19 + TypeScript), Billing & Invoicing, Revisit Booking, In-App Notification Center, Doctor Workspace và Database Seeding.

---

## 1. Kết quả Build & Automated Tests

Toàn bộ quy trình kiểm thử tự động đã được kiểm chứng độc lập ở cả cấu hình Debug và Release:

- **Backend Build (`dotnet build`):** Pass - 0 Error(s), 0 Warning(s) trên cả `Debug` và `Release`.
- **Backend Tests (`dotnet test`):** **81/81 passed (0 failed, 0 skipped)**
  - `BillingTests`: 10/10 passed (Validation phí chuyên khoa, kiểm soát range 365 ngày, snapshot giá không đổi khi phí gốc cập nhật, chống double payment, audit log, KPI doanh thu).
  - `RevisitWorkflowTests`: 6/6 passed (Bác sĩ đề xuất tái khám, route `/patient/revisit`, Notification ID thực không hardcode "0", bệnh nhân chỉ đặt được đề xuất của chính mình, phân định trạng thái `HoldingSlotStatuses` vs `ReleasedSlotStatuses`, thông báo lễ tân `/reception/appointments`).
  - `AppointmentConcurrencyTests`: 2/2 passed (Chống race-condition đặt trùng slot đồng thời với Serializable transaction).
  - `DoctorWorkflowTests` & `DoctorIsolationTests`: 15/15 passed (Cách ly hàng đợi và lịch khám giữa các bác sĩ, cập nhật diễn tiến lâm sàng, kết thúc khám).
  - `PatientPrivacyTests`: 11/11 passed (Cách ly hóa đơn bệnh nhân, bảo vệ PII, tra cứu công khai che số điện thoại và tên).
  - Các bộ test Identity, JWT, Pharmacy Dispense, Schedule & Leave Requests: 37/37 passed.
- **Frontend Linter (`npm run lint`):** Pass - 0 error, 84 minor warnings (cho phép).
- **Frontend Tests (`npm run test`):** **49/49 passed (0 failed across 9 test suites)**
  - `revisitBookingHelper.test.ts`: Format ngày giờ tái khám theo tiêu chuẩn ca khám y tế.
  - `billing.test.ts`: Format tiền tệ VND, mapping trạng thái hóa đơn, phân quyền hóa đơn lễ tân và bệnh nhân.
  - `notifications.test.ts` & `notificationBell.test.ts`: Hiển thị thông báo thật, polling/mark-as-read, badge số đếm.
  - `doctorDashboard.test.ts` & `doctorQueue.test.ts`: Bộ lọc hàng đợi, tìm kiếm ca khám, tiếp nhận bệnh nhân.
  - `roleRoutes.test.ts`: Kiểm soát phân quyền route theo vai trò người dùng.
- **Frontend Build (`npm run build`):** Pass - `tsc -b && vite build` tạo bundle thành công, 0 lỗi biên dịch type.

---

## 2. Kiểm thử Thực tế E2E (Live System Verification)

Hệ thống được khởi động live tại Backend (`http://localhost:5258`, OpenAPI tại `/openapi/v1.json`) và Frontend (`http://localhost:5173`). Kịch bản E2E kiểm tra dữ liệu thật trên DB với 11 bước hoàn chỉnh:

| STT | Bước kiểm tra | Kết quả | Minh chứng thực tế |
|---|---|---|---|
| 1 | **Patient Browse Doctors** | **Pass** | Lấy đủ 10 bác sĩ đang hoạt động từ DB với chuyên khoa, học vị chuẩn hóa. |
| 2 | **Query Available Slots** | **Pass** | Endpoint `GET /api/v1/doctors/1/available-slots?fromDate=2026-09-07&toDate=2026-09-07` trả về 10 slot còn trống trong ngày 07/09/2026. |
| 3 | **Book Appointment** | **Pass** | Bệnh nhân đặt slot 1705 thành công -> Lịch hẹn ID 79, mã `APT-260906-BEC3052`, trạng thái `Pending`. |
| 4 | **Patient In-App Notification** | **Pass** | Thông báo ID 11 tự động tạo với tiêu đề "Đặt lịch khám thành công" gửi đúng tài khoản bệnh nhân. |
| 5 | **Receptionist Confirm & Check-in** | **Pass** | Lễ tân xác nhận lịch hẹn và check-in tiếp nhận bệnh nhân vào phòng khám. |
| 6 | **Doctor Consultation & Revisit Proposal**| **Pass** | Bác sĩ tiếp nhận, bắt đầu khám, hoàn tất bệnh án và đề xuất tái khám ngày 14/09/2026 (Revisit Request ID 2). |
| 7 | **Patient Accept Revisit Booking** | **Pass** | Bệnh nhân xem đề xuất, chọn slot 1785 ngày 14/09/2026 và đồng ý -> Tự động sinh lịch hẹn mới ID 80, mã `APT-260906-1A187ED`. |
| 8 | **Receptionist Invoice & Cash Payment** | **Pass** | Lập hóa đơn ID 2 từ ca khám hoàn tất (220,000 VND), thanh toán tiền mặt thành công (Payment ID 2, trạng thái `Thành công`). |
| 9 | **Atomic Double Payment Prevention** | **Pass** | Thử thanh toán lại lần 2 trên cùng hóa đơn -> Hệ thống chặn và trả lỗi HTTP 422 (`UnprocessableEntityException: Hóa đơn đã được thanh toán`). |
| 10| **Patient Isolated Invoices** | **Pass** | Bệnh nhân tra cứu hóa đơn của chính mình thấy hóa đơn ID 2 với trạng thái `Đã thanh toán`. |
| 11| **Admin Revenue Reporting** | **Pass** | Admin xem báo cáo doanh thu chu kỳ từ 01/09 đến 30/09, tổng doanh thu ghi nhận chính xác 440,000 VND. |

---

## 3. Data Safety, Privacy & Security Audit

1. **RBAC & Data Isolation:**
   - Bác sĩ chỉ truy cập được hàng đợi, bệnh án và lịch làm việc của chính mình.
   - Bệnh nhân chỉ xem được hóa đơn, thông báo và đề xuất tái khám thuộc sở hữu của mình.
   - Lễ tân không có quyền cấu hình mức phí chuyên khoa (chỉ dành cho Admin).
2. **Public Lookup Safety:**
   - Yêu cầu chuỗi tìm kiếm tối thiểu 4 ký tự.
   - Che mặt định danh cá nhân (PII): họ tên bệnh nhân chỉ hiển thị dạng ký tự đầu và dấu sao, số điện thoại được che giữa.
3. **Slot Policy State Machine:**
   - Trạng thái giữ slot (`HoldingSlotStatuses`): `Pending`, `Confirmed`, `PendingReschedule`, `PendingCancellation`, `CheckedIn`, `InConsultation`.
   - Trạng thái giải phóng slot (`ReleasedSlotStatuses`): `Cancelled`, `Completed`, `NoShow`.
4. **Data Integrity & Consistency:**
   - Giá trên hóa đơn lấy snapshot tại thời điểm lập hóa đơn, không bị hồi tố khi Admin thay đổi giá chuyên khoa.
   - Giới hạn tra cứu báo cáo doanh thu tối đa 365 ngày, xử lý ngày giờ theo múi giờ chuẩn Việt Nam (UTC+7).

---

## 4. Hạn Chế Còn Lại & Khuyến Nghị Vận Hành

- **Không tích hợp cổng thanh toán trực tuyến:** Phase này chỉ hỗ trợ thanh toán tại quầy (`Cash` và `ManualBankTransfer`) theo đúng phạm vi nghiệp vụ.
- **Tiền thuốc chưa tính vào hóa đơn khám:** Đúng thiết kế để không phá vỡ quy trình cấp phát thuốc tại kho Dược.
- **Chủ nhật không có lịch khám:** Hệ thống mặc định lịch làm việc từ Thứ Hai đến Thứ Bảy, cần lưu ý khi chọn ngày khám trên giao diện.

---

## 5. Kết Luận

- Trạng thái mã nguồn: **Sạch, chuẩn hóa, không có thay đổi rác hay secret bị lộ**.
- CI local: **100% Pass** (Backend 81/81, Frontend 49/49, Lint 0 error, Build 0 error).
- E2E: **11/11 luồng nghiệp vụ hoạt động ổn định trên môi trường thực tế**.
- Nhánh `fix/ci-billing-hardening` tại commit `5ee54e2` đã sẵn sàng để review và merge vào `feat/doctor-clinical-workspace`.
