# Báo Cáo Kiểm Thử (QA Report)

**Thời điểm thực hiện:** Giai đoạn CI, Billing, Pharmacy & Appointment Change Requests Hardening (PR #1 - `fix/ci-billing-hardening`)
**Commit SHA:** HEAD (Hoàn thiện toàn diện quy trình Đổi lịch & Hủy lịch khám - Appointment Change Request Workflow)
**Target Base:** `feat/doctor-clinical-workspace`
**Phạm vi:** Toàn bộ hệ thống Backend (.NET 10), Frontend (React 19 + TypeScript), Billing & Invoicing, Revisit Booking, In-App Notification Center, Doctor Workspace, Database Seeding, Pharmacy Dispensing & Medicine Inventory, và **Atomic Appointment Change Request Workflow (Đổi lịch / Hủy lịch khám)**.

---

## 1. Kết quả Build & Automated Tests

Toàn bộ quy trình kiểm thử tự động đã được kiểm chứng độc lập ở cả cấu hình Debug và Release:

- **Backend Build (`dotnet build`):** Pass - **0 Error(s), 0 Warning(s)** trên toàn bộ solution.
- **Backend Tests (`dotnet test`):** **123/123 passed (0 failed, 0 skipped)** - **Kiểm chứng 3 lần liên tiếp đạt 123/123 passed** (100% deterministic, loại bỏ triệt để flakiness).
  - `AppointmentChangeRequestTests`: **33/33 passed**
    1. Yêu cầu hủy lịch hẹn `Confirmed` -> trạng thái đổi `PendingCancellation`, slot khám hiện tại vẫn được giữ tạm thời.
    2. Chống tạo yêu cầu thay đổi trùng lặp khi đã có yêu cầu `Pending` (HTTP 409 `ACTIVE_CHANGE_REQUEST_EXISTS`).
    3. Lễ tân duyệt yêu cầu hủy lịch -> lịch hẹn chuyển `Cancelled`, slot khám được giải phóng (`IsBooked = false`).
    4. Lễ tân từ chối yêu cầu hủy lịch -> lịch hẹn khôi phục trạng thái `Confirmed`, slot khám tiếp tục được giữ (`IsBooked = true`), ghi nhận lý do từ chối vào `AppointmentHistory`.
    5. Yêu cầu đổi sang slot hợp lệ cùng bác sĩ -> trạng thái đổi `PendingReschedule`, slot cũ vẫn được giữ tạm thời.
    6. Lễ tân duyệt đổi lịch -> slot cũ được giải phóng, slot mới được chuyển thành booked (`IsBooked = true`), lịch hẹn trỏ sang slot mới và chuyển về `Confirmed`.
    7. Lễ tân từ chối yêu cầu đổi lịch -> lịch hẹn khôi phục trạng thái cũ (`Confirmed`), slot cũ vẫn giữ nguyên, ghi chép audit log đầy đủ.
    8. Concurrency Collision khi đổi lịch: Slot đích bị đặt đồng thời bởi lịch hẹn khác -> duyệt đổi lịch bắn HTTP 409 `TARGET_SLOT_ALREADY_BOOKED`, transaction rollback toàn bộ, không có dữ liệu chắp vá (slot cũ giữ nguyên, lịch hẹn giữ nguyên).
    9. Bệnh nhân rút yêu cầu đổi lịch (`Withdrawn`) -> lịch hẹn khôi phục trạng thái cũ, slot được bảo toàn.
    10. Bệnh nhân rút yêu cầu hủy lịch (`Withdrawn`) -> lịch hẹn khôi phục trạng thái cũ, slot được bảo toàn.
    11. Chặn đổi sang slot ở quá khứ (HTTP 400 `INVALID_TARGET`).
    12. Chặn đổi sang slot trùng với slot hiện tại (HTTP 400 `INVALID_TARGET`).
    13. Chặn đổi sang slot của bác sĩ khác (HTTP 400 `DOCTOR_MISMATCH`).
    14. Chặn đổi sang slot vào ngày Chủ nhật (HTTP 400 `INVALID_SCHEDULE`).
    15. Chặn đổi sang slot của bác sĩ không hoạt động (HTTP 400 `DOCTOR_NOT_AVAILABLE`).
    16. Chặn đổi sang slot khi bác sĩ có lịch nghỉ đã duyệt (HTTP 400 `DOCTOR_NOT_AVAILABLE`).
    17. Chặn đổi sang slot khi bệnh nhân có lịch hẹn khác trùng giờ (HTTP 400 `PATIENT_TIME_CONFLICT`).
    18. Notification: Lễ tân nhận thông báo thật với route `/reception/change-requests` khi bệnh nhân gửi yêu cầu (ID thực không hardcode "0").
    19. Notification: Bệnh nhân nhận thông báo thật với route `/patient/appointments` khi yêu cầu được duyệt hoặc từ chối.
    20. Data Isolation & Privacy: Bệnh nhân chỉ xem và thao tác trên yêu cầu của chính mình; không thể rút yêu cầu của người khác.
    21. Validation: Lý do đổi lịch < 5 ký tự bị chặn HTTP 400.
    22. Validation: Lý do hủy lịch < 5 ký tự bị chặn HTTP 400.
    23. Validation: Lý do từ chối của lễ tân < 5 ký tự bị chặn HTTP 400.
    24. Trạng thái không hợp lệ: Không thể hủy lịch hẹn đã hoàn thành (`Completed`) hoặc đã hủy (`Cancelled`) (HTTP 422 `INVALID_STATE`).
    25. Trạng thái không hợp lệ: Không thể đổi lịch khi lịch hẹn không ở trạng thái hợp lệ (HTTP 422 `INVALID_STATE`).
    26. Rút yêu cầu: Không thể rút yêu cầu đã được duyệt hoặc từ chối (HTTP 409 `CHANGE_REQUEST_ALREADY_PROCESSED`).
    27. Rút yêu cầu: Khôi phục chính xác trạng thái `OriginalAppointmentStatus` (Pending -> Pending, Confirmed -> Confirmed).
    28. Từ chối yêu cầu: Khôi phục chính xác trạng thái `OriginalAppointmentStatus` (Pending -> Pending, Confirmed -> Confirmed).
    29. Concurrency: Gửi 2 yêu cầu thay đổi đồng thời trên cùng 1 lịch hẹn -> đúng 1 yêu cầu thành công, yêu cầu còn lại nhận HTTP 409 `ACTIVE_CHANGE_REQUEST_EXISTS`.
    30. Concurrency: 2 lễ tân duyệt hủy đồng thời trên cùng 1 yêu cầu -> đúng 1 yêu cầu thành công, yêu cầu còn lại nhận HTTP 409 `CHANGE_REQUEST_ALREADY_PROCESSED`.
    31. Concurrency: 1 lễ tân duyệt và 1 lễ tân từ chối đồng thời -> đúng 1 thao tác thành công, thao tác còn lại nhận HTTP 409 `CHANGE_REQUEST_ALREADY_PROCESSED`.
    32. Concurrency: Lễ tân từ chối và bệnh nhân rút yêu cầu đồng thời -> đúng 1 thao tác thành công, thao tác còn lại nhận HTTP 409 `CHANGE_REQUEST_ALREADY_PROCESSED`.
    33. Phân trang và validation tham số query: Tự động clamp `page >= 1`, `pageSize <= 100`, reject invalid enum string với HTTP 400.
  - `PharmacyDispenseTests`: 11/11 passed (Cấp phát nguyên tử, trừ tồn kho chính xác, chống duplicate dispense 409, concurrency race condition, RBAC dược sĩ, thông báo `/patient/prescriptions`).
  - `BillingTests`: 10/10 passed (Validation phí chuyên khoa, kiểm soát range 365 ngày, snapshot giá không đổi khi phí gốc cập nhật, chống double payment, audit log, KPI doanh thu).
  - `RevisitWorkflowTests`: 6/6 passed (Bác sĩ đề xuất tái khám, route `/patient/revisit`, Notification ID thực không hardcode "0", bệnh nhân chỉ đặt được đề xuất của chính mình, phân định trạng thái `HoldingSlotStatuses` vs `ReleasedSlotStatuses`, thông báo lễ tân `/reception/appointments`).
  - `AppointmentConcurrencyTests`: 2/2 passed (Chống race-condition đặt trùng slot đồng thời với Serializable transaction).
  - `DoctorWorkflowTests` & `DoctorIsolationTests`: 15/15 passed (Cách ly hàng đợi và lịch khám giữa các bác sĩ, cập nhật diễn tiến lâm sàng, kết thúc khám).
  - `PatientPrivacyTests`: 11/11 passed (Cách ly hóa đơn bệnh nhân, bảo vệ PII, tra cứu công khai che số điện thoại và tên).
  - Các bộ test Identity, JWT, Schedule & Leave Requests: 35/35 passed.
- **Frontend Linter (`npm run lint`):** Pass - **0 error, 81 minor warnings** (nằm trong ngưỡng cho phép <= 84).
- **Frontend Tests (`npm run test`):** **62/62 passed (0 failed across 11 test suites)**
  - `appointmentChangeRequests.test.tsx`: **9/9 passed** (Bệnh nhân xem danh sách lịch khám và badge trạng thái, mở modal chọn slot khả dụng & gửi yêu cầu đổi lịch, chặn chọn Chủ nhật kèm thông báo, retry khi lỗi kết nối, lễ tân lọc danh sách yêu cầu theo loại Reschedule/Cancellation & trạng thái, xem chi tiết và duyệt yêu cầu, validate độ dài lý do từ chối >= 5 ký tự, phân trang điều hướng).
  - `pharmacyPrescriptions.test.tsx`: 4/4 passed (Render danh sách đơn thuốc, xem chi tiết và tồn kho khả dụng, disable nút cấp thuốc khi tồn kho không đủ, hiển thị badge và cảnh báo khi thuốc ngừng hoạt động).
  - `revisitBookingHelper.test.ts`: Format ngày giờ tái khám theo tiêu chuẩn ca khám y tế.
  - `billing.test.ts`: Format tiền tệ VND, mapping trạng thái hóa đơn, phân quyền hóa đơn lễ tân và bệnh nhân.
  - `notifications.test.ts` & `notificationBell.test.ts`: Hiển thị thông báo thật, polling/mark-as-read, badge số đếm.
  - `doctorDashboard.test.ts` & `doctorQueue.test.ts`: Bộ lọc hàng đợi, tìm kiếm ca khám, tiếp nhận bệnh nhân.
  - `roleRoutes.test.ts`: Kiểm soát phân quyền route theo vai trò người dùng.
- **Frontend Build (`npm run build`):** Pass - `tsc -b && vite build` tạo bundle thành công, **0 lỗi biên dịch type**.

---

## 2. Kiểm thử Thực tế E2E (Live System Verification)

Hệ thống được khởi động live tại Backend (`http://localhost:5258`) và Frontend (`http://localhost:5173`). Kịch bản E2E kiểm tra dữ liệu thật trên DB:

### A. Quy trình Đổi lịch & Hủy lịch khám (Appointment Change Requests)

| STT | Kịch bản kiểm tra | Kết quả | Chi tiết xác thực nghiệp vụ |
|---|---|---|---|
| 1 | **Scenario A: Yêu cầu hủy lịch & Lễ tân duyệt** | **Pass** | Bệnh nhân gửi yêu cầu hủy lịch #APT -> Trạng thái đổi `PendingCancellation`. Chặn yêu cầu trùng lặp (HTTP 409). Lễ tân duyệt hủy -> Lịch đổi `Cancelled`, Slot khám ban đầu được giải phóng (`IsBooked = false`). Lịch sử ghi nhận `Cancelled` với lý do duyệt. |
| 2 | **Scenario B: Yêu cầu hủy & Lễ tân từ chối** | **Pass** | Lễ tân từ chối yêu cầu với lý do -> Lịch hẹn khôi phục chính xác trạng thái ban đầu (`Confirmed`), Slot khám tiếp tục được giữ nguyên (`IsBooked = true`). |
| 3 | **Scenario C: Yêu cầu đổi lịch & Lễ tân duyệt** | **Pass** | Bệnh nhân gửi yêu cầu đổi sang Slot B -> Trạng thái đổi `PendingReschedule`. Lễ tân duyệt đổi lịch -> Slot A được giải phóng, Slot B chuyển thành `IsBooked = true`, lịch hẹn trỏ sang Slot B và cập nhật trạng thái `Confirmed`. |
| 4 | **Scenario D: Xung đột Slot đích (Concurrency Collision)** | **Pass** | Bệnh nhân 1 xin đổi sang Slot C. Trong lúc chờ, Bệnh nhân 2 đặt thành công Slot C. Lễ tân duyệt yêu cầu của Bệnh nhân 1 -> Bị chặn với HTTP 409 `TARGET_SLOT_ALREADY_BOOKED`. Transaction rollback an toàn: Bệnh nhân 1 vẫn giữ Slot B ban đầu, không xảy ra ghi đè dữ liệu. |
| 5 | **Scenario E: Bệnh nhân rút yêu cầu** | **Pass** | Bệnh nhân chủ động rút yêu cầu (`withdraw`) -> Lịch hẹn khôi phục trạng thái `Confirmed`, Slot B vẫn được giữ nguyên. |
| 6 | **Scenario F: In-App Notifications** | **Pass** | Bệnh nhân nhận thông báo với route `/patient/appointments`. Lễ tân nhận thông báo với route `/reception/change-requests`. ID liên kết thực tế, không hardcode. |
| 7 | **Scenario G: Web Server Health** | **Pass** | Frontend phản hồi HTTP 200 tại `http://localhost:5173`. |

### B. Quy trình Cấp phát thuốc & Kho dược (Pharmacy Dispensing & Inventory)

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

---

## 3. Data Safety, Privacy & Security Audit

1. **RBAC & Data Isolation:**
   - Dược sĩ và Quản trị viên: Có quyền truy cập kho thuốc, xem danh sách đơn thuốc và thực hiện cấp phát.
   - Bác sĩ chỉ truy cập được hàng đợi, bệnh án và lịch làm việc của chính mình.
   - Bệnh nhân chỉ xem và thao tác trên lịch hẹn, yêu cầu đổi/hủy lịch, hóa đơn và thông báo của chính mình; không xem kho thuốc nội bộ.
   - Lễ tân có quyền duyệt/từ chối yêu cầu đổi và hủy lịch hẹn, quản lý hàng đợi lễ tân; không thể cấp phát thuốc hoặc thay đổi phí cấu hình.
2. **Transaction Isolation & Concurrency Safety:**
   - Cấp phát thuốc và duyệt đổi lịch thực hiện với transaction Serializable kết hợp conditional update (`ExecuteUpdateAsync`).
   - Xung đột slot đích khi duyệt dời lịch trả HTTP 409 `TARGET_SLOT_ALREADY_BOOKED` kèm rollback an toàn.
   - Tranh chấp đơn thuốc hoặc tồn kho đồng thời được xử lý an toàn: trả HTTP 409 `DISPENSE_CONFLICT`, không gây dirty write hay âm tồn kho.
3. **Public Lookup Safety:**
   - Yêu cầu chuỗi tìm kiếm tối thiểu 4 ký tự.
   - Che mặt định danh cá nhân (PII): họ tên bệnh nhân chỉ hiển thị dạng ký tự đầu và dấu sao, số điện thoại được che giữa.
4. **Slot Policy State Machine:**
   - Trạng thái giữ slot (`HoldingSlotStatuses`): `Pending`, `Confirmed`, `PendingReschedule`, `PendingCancellation`, `CheckedIn`, `InConsultation`.
   - Trạng thái giải phóng slot (`ReleasedSlotStatuses`): `Cancelled`, `Completed`, `NoShow`.
   - Khôi phục trạng thái chuẩn xác khi từ chối hoặc rút yêu cầu đổi/hủy lịch dựa trên lịch sử `AppointmentHistories`.
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
- CI local: **100% Pass** (Backend 123/123 - 3 lần kiểm thử liên tiếp pass 100%, Frontend 62/62, Lint 0 error / 81 warnings, Build 0 error).
- E2E: **Toàn bộ kịch bản nghiệp vụ Đổi lịch/Hủy lịch khám và Pharmacy Dispensing hoạt động ổn định và nhất quán trên môi trường thực tế**.
- Nhánh `fix/ci-billing-hardening` đã hoàn thiện toàn diện và sẵn sàng merge vào `feat/doctor-clinical-workspace`.

