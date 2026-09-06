# Hướng Dẫn Nghiệp Vụ & Kỹ Thuật: Module Thu Ngân – Hóa Đơn – Thanh Toán – Doanh Thu (ClinicCare AI)

Tài liệu này ghi nhận kiến trúc, cơ sở dữ liệu, phân quyền bảo mật, kiểm thử và hướng dẫn vận hành cho module **Thu ngân – Hóa đơn – Thanh toán – Doanh thu** trong hệ thống **ClinicCare AI**.

---

## 1. Mục Tiêu & Phạm Vi Cố Định

1. **Phạm vi thanh toán**:
   - Phương thức thanh toán trực tiếp tại phòng khám: `Cash` (Tiền mặt tại quầy) hoặc `ManualBankTransfer` (Chuyển khoản trực tiếp tại quầy có mã tham chiếu).
   - Tuyệt đối không tích hợp cổng giả lập (VNPay, MoMo) hoặc tự động báo thành công ảo.
2. **Phân quyền và vai trò (RBAC)**:
   - **Receptionist (Lễ tân/Thu ngân)**: Lập hóa đơn từ ca khám đã hoàn thành hoặc gói khám đã duyệt; thu tiền tại quầy; hủy hóa đơn; tra cứu & in phiếu thu.
   - **Admin (Quản trị viên)**: Xem tổng quan dashboard doanh thu, báo cáo dòng tiền theo ngày, quản lý và điều chỉnh biểu phí khám chuyên khoa.
   - **Patient (Bệnh nhân)**: Tra cứu lịch sử hóa đơn của chính mình, xem chi tiết & in phiếu thu. Không thể tự thu tiền hay sửa hóa đơn.
   - **Doctor & Pharmacist**: Bị cấm hoàn toàn (trả về HTTP 403 Forbidden).
3. **Tính toàn vẹn dữ liệu**:
   - Không hard-delete dữ liệu hóa đơn (`Invoices`) và thanh toán (`Payments`).
   - Mức phí khám chuyên khoa được lưu trong DB và **snapshot** giá trị tại thời điểm lập hóa đơn để lịch sử tài chính không bị biến động khi đổi giá sau này.
   - Kiểm tra chống trùng lặp hóa đơn hoạt động (Unique Filtered Index `AppointmentId` / `HealthPackageRegistrationId` với `Status <> Cancelled`).
   - Đảm bảo tính toán số tiền chuẩn xác từ backend, không phụ thuộc vào frontend tính nhẩm.

---

## 2. Kiến Trúc Cơ Sở Dữ Liệu & Entity Framework Core

### 2.1. Thay đổi thực thể & Quan hệ
- **`Specialty.ConsultationFee`**: Cột `decimal(18,2)` có ràng buộc `CK_Specialties_ConsultationFee_NonNegative` (>= 0). Seeder ban đầu nạp mức giá từ 150.000đ đến 250.000đ có ghi chú `(Dữ liệu demo)`.
- **`Invoices`**:
  - `Id`, `InvoiceCode` (Unique, tiền tố `INV-yyMMdd-XXXXXXX`).
  - `PatientId` (Khóa ngoại sang `Patients`).
  - `SourceType` (1: Appointment, 2: HealthPackageRegistration).
  - `AppointmentId` (Khóa ngoại sang `Appointments`, nullable).
  - `HealthPackageRegistrationId` (Khóa ngoại sang `HealthPackageRegistrations`, nullable).
  - `Status` (1: Unpaid, 2: Paid, 3: Cancelled).
  - `Subtotal`, `TotalAmount` (`decimal(18,2)` >= 0).
  - `RowVersion` (Concurrency token chống xung đột đồng thời).
  - `SingleSource Constraint`: `([AppointmentId] IS NOT NULL AND [HealthPackageRegistrationId] IS NULL) OR ([AppointmentId] IS NULL AND [HealthPackageRegistrationId] IS NOT NULL)`.
- **`InvoiceItems`**:
  - `InvoiceId`, `ItemCode`, `Description`, `Quantity`, `UnitPrice`, `LineTotal`, `ReferenceType`, `ReferenceId`.
- **`Payments`**:
  - `PaymentCode` (Unique, tiền tố `PAY-yyMMdd-XXXXXXX`).
  - `InvoiceId`, `Amount`, `Method` (1: Cash, 2: ManualBankTransfer).
  - `ReferenceCode`, `Note`, `ReceivedByUserId`, `ReceivedAtUtc`, `Status` (1: Succeeded, 2: Voided).

### 2.2. Migration
- File migration: `20260906063514_AddBillingModule.cs`. Đã áp dụng an toàn và cập nhật DB Snapshot.

---

## 3. Danh Mục API Backend

### 3.1. Lễ tân / Thu ngân (`/api/v1/reception/billing`)
- `GET /invoices`: Tra cứu và phân trang danh sách hóa đơn theo từ khóa, trạng thái, nguồn, khoảng ngày.
- `GET /invoices/{id}`: Xem chi tiết hóa đơn, các khoản mục và lịch sử thanh toán.
- `POST /invoices/appointment`: Lập hóa đơn từ ca khám đã hoàn thành (`Completed`).
- `POST /invoices/health-package`: Lập hóa đơn từ đăng ký gói khám đã xác nhận (`Confirmed`).
- `POST /invoices/{id}/pay`: Thu tiền trực tiếp tại quầy (yêu cầu số tiền khớp 100%, ghi audit log và tạo thông báo cho bệnh nhân).
- `PATCH /invoices/{id}/cancel`: Hủy hóa đơn chưa thanh toán kèm lý do bắt buộc.
- `GET /kpi`: Thống kê KPI ngày hôm nay (chưa thu, đã thu, đã hủy, thực thu).

### 3.2. Bệnh nhân (`/api/v1/patient/invoices`)
- `GET /`: Danh sách hóa đơn thuộc quyền sở hữu của bệnh nhân đang đăng nhập.
- `GET /{id}`: Xem chi tiết phiếu thu của mình. Chặn truy cập hóa đơn của bệnh nhân khác (HTTP 404).

### 3.3. Quản trị viên (`/api/v1/admin/billing`)
- `GET /revenue`: Báo cáo tổng thực thu, số giao dịch thành công, phân rã theo từng ngày (`DailyBreakdown`) và trạng thái hóa đơn (`StatusBreakdown`). Chỉ các giao dịch `Succeeded` mới được tính vào doanh thu.
- `GET /specialties`: Lấy danh mục biểu phí khám hiện hành của 11 chuyên khoa.
- `PATCH /specialties/{id}/fee`: Điều chỉnh mức phí khám chuyên khoa.

---

## 4. Giao Diện Người Dùng & Trải Nghiệm (UI/UX)

1. **Trang Lễ tân (`/reception/billing`)**:
   - 4 thẻ KPI đầu trang: *Chờ thanh toán*, *Đã thu hôm nay*, *Đã hủy hôm nay*, *Thực thu hôm nay*.
   - Bộ lọc tìm kiếm nhanh đa tiêu chí (Mã HĐ, Tên/SĐT bệnh nhân, Trạng thái, Nguồn dịch vụ, Khoảng ngày).
   - Modal lập hóa đơn mới: lựa chọn từ lịch khám hoặc gói khám.
   - Modal thu tiền: điền phương thức (`Cash` hoặc `ManualBankTransfer`), kiểm tra số tiền khớp tuyệt đối, nút xác nhận có trạng thái disable chống double click.
   - Modal hủy hóa đơn: cảnh báo rõ ràng, yêu cầu nhập lý do.
2. **Modal Phiếu thu / Hóa đơn & In ấn (`InvoiceReceiptModal`)**:
   - Thiết kế chuẩn mẫu phiếu thu y tế ClinicCare AI có gắn nhãn "(Dữ liệu demo)".
   - Định dạng tiền tệ `Intl.NumberFormat('vi-VN', { currency: 'VND' })`.
   - Khung chữ ký người nộp tiền và người lập phiếu.
   - Hỗ trợ `@media print` sạch sẽ: tự động ẩn giao diện web nền, thanh menu, chỉ in duy nhất mẫu phiếu thu sắc nét.
3. **Trang Bệnh nhân (`/patient/invoices`)**:
   - Hiển thị danh sách hóa đơn cá nhân dạng tab phân loại.
   - Banner thông báo hướng dẫn bệnh nhân thanh toán trực tiếp tại quầy lễ tân.
   - Tuyệt đối không hiển thị nút thu tiền / thanh toán online giả lập.
4. **Trang Quản trị Doanh thu & Biểu phí (`/admin/billing`)**:
   - Tab 1: Dashboard doanh thu trực quan, biểu thống kê dòng tiền theo khoảng thời gian tùy chọn.
   - Tab 2: Quản lý biểu phí chuyên khoa kèm modal điều chỉnh mức phí tức thời.

---

## 5. Kết Quả Kiểm Thử

- **Backend Integration Tests**: **71/71 tests passed** (đã bổ sung 11 integration test chuyên sâu cho Billing và RBAC).
- **Frontend Unit & Component Tests**: **46/46 tests passed** (đã bổ sung 6 component test kiểm tra modal, role guards và form submission).
- **Frontend Production Build**: `npm run build` thành công, sạch lỗi TypeScript.
- **End-to-End Verification (Script trên Live Server)**:
  1. Admin đổi biểu phí chuyên khoa thành công.
  2. Lễ tân lập hóa đơn từ ca khám Completed với mức giá snapshot chính xác.
  3. Lễ tân thu tiền tại quầy bằng tiền mặt, xuất mã giao dịch `PAY-...`.
  4. Bệnh nhân nhận được thông báo in-app `Thanh toán thành công` và xem được hóa đơn.
  5. Dashboard của Admin cập nhật doanh thu và số lượt giao dịch thành công ngay lập tức.
