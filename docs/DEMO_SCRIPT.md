# Kịch bản Demo Dự án ClinicCare AI

**Thời lượng dự kiến:** 10-15 phút.  
**Chuẩn bị:** Mở sẵn trình duyệt tại `http://localhost:5173`, chạy backend tại `http://localhost:5258` và frontend ở chế độ Development.

---

## 1. Giới thiệu tổng quan (1 phút)
- **Role:** Khách (Chưa đăng nhập)
- **Trang:** `http://localhost:5173/login`
- **Thuyết minh:** *"Chào thầy/cô, hệ thống quản lý phòng khám ClinicCare AI kết nối toàn diện 5 vai trò nghiệp vụ: Bệnh nhân, Lễ tân, Bác sĩ, Kỹ thuật viên Cận lâm sàng, và Quản trị viên. Điểm nhấn là quy trình lâm sàng khép kín từ chỉ định xét nghiệm, thực hiện CLS, bác sĩ xem, đối chiếu kết quả và hoàn thiện chẩn đoán đến theo dõi sinh hiệu nhân trắc học dọc."*

---

## 2. Bệnh nhân đặt lịch & AI Triage (2 phút)
- **Role:** Bệnh nhân (`patient@cliniccare.local` / `Demo@12345`)
- **Trang:** `/patient/book`
- **Thao tác:** 
  1. Nhập mô tả triệu chứng: *"Tôi cảm thấy đau thắt ngực khi gắng sức, hồi hộp và mệt mỏi..."*
  2. Bấm **"Gợi ý chuyên khoa"** -> AI tư vấn chuyên khoa **Tim mạch / Nội tổng quát**.
  3. Chọn Bác sĩ (BS.CKI Nguyễn Minh Khải), chọn ngày và slot khám. Gửi yêu cầu đặt lịch.
- **Kết quả:** Lịch hẹn sinh ra ở trạng thái `Chờ xác nhận` (`Pending`).

---

## 3. Lễ tân tiếp đón & Check-in (1 phút)
- **Role:** Lễ tân (`reception@cliniccare.local` / `Demo@12345`)
- **Trang:** `/reception/appointments`
- **Thao tác:** 
  1. Xác nhận lịch khám mới (`Pending` → `Confirmed`).
  2. Bệnh nhân đến phòng khám: Bấm **"Tiếp nhận" (Check-in)** (`Confirmed` → `CheckedIn`).
- **Thuyết minh:** *"Bệnh nhân được đưa vào hàng đợi khám thực tế của bác sĩ chuyên khoa."*

---

## 4. Bác sĩ khám lâm sàng & Ghi nhận Sinh hiệu Nhân trắc dọc (2.5 phút)
- **Role:** Bác sĩ (`doctor@cliniccare.local` / `Demo@12345` – BS.CKI Nguyễn Minh Khải)
- **Trang:** `/doctor/appointments` hoặc `/doctor/queue`
- **Thao tác:**
  1. Bấm **"Bắt đầu khám"** ca hẹn để vào **Không gian khám lâm sàng** (`/doctor/appointments/:id/examination`).
  2. **Tab Dấu hiệu sinh tồn:**
     - Bấm nút **"Dùng chiều cao lần trước"** (tự động điền 172cm từ lịch sử đo).
     - Nhập cân nặng 68.5kg -> Hệ thống tự động tính **BMI = 23.2** kèm nhãn xanh *"Bình thường"* (theo ngưỡng hệ thống: <18.5 Thiếu cân, <25.0 Bình thường, <30.0 Thừa cân / Tiền béo phì, >=30.0 Béo phì).
     - Xem **Vùng 2 & Vùng 3 (Nhân trắc học dọc)**: Đối chiếu với lần đo trước, hiển thị biến thiên cân nặng $\Delta \text{Weight}$ và $\Delta \text{BMI}$ với badge màu trực quan.
     - Xem **Vùng 4 (Bảng lịch sử sinh hiệu)**: Liệt kê các mốc đo sinh hiệu trong quá khứ của bệnh nhân.
     - Bấm **"Lưu sinh hiệu"**.
  3. **Tab Diễn tiến khám:** Ghi nhận triệu chứng lâm sàng và chẩn đoán sơ bộ.

---

## 5. Bác sĩ Chỉ định Cận lâm sàng & Kiểm chứng Guard chặn hoàn tất (2 phút)
- **Role:** Bác sĩ
- **Trang:** `/doctor/appointments/:id/examination` - Tab **"Chỉ định Cận lâm sàng"**
- **Thao tác:**
  1. Chọn dịch vụ từ Catalog: **Tổng phân tích tế bào máu (LAB-CBC)** và **Siêu âm ổ bụng tổng quát (US-ABD)**.
  2. Nhập chỉ định lâm sàng: *"Đau thắt ngực, tầm soát bệnh lý tim mạch và ổ bụng"*.
  3. Bấm **"Tạo phiếu chỉ định"**. Phiếu CLS sinh ra mã dạng `DX-2026xxxx-xxxx`.
  4. Bấm **"In phiếu chỉ định"** -> Mở trang in phiếu y tế chuyên nghiệp (`/doctor/diagnostic-orders/:id/print`) với mã phiếu `DX`, hướng dẫn chuẩn bị nhịn ăn, danh mục dịch vụ chỉ định và chữ ký xác nhận của kỹ thuật viên và bác sĩ.
  5. **Thử nghiệm Completion Guard #1:** Chuyển sang Tab Kê đơn hoặc Diễn tiến, thử bấm **"Hoàn tất ca khám"**.
  - **Kết quả:** Hệ thống hiển thị cảnh báo đỏ và chặn không cho hoàn tất: *"Không thể hoàn tất phiên khám khi còn chỉ định cận lâm sàng đang chờ kết quả."*
  - **Thuyết minh:** *"Đây là chốt chặn an toàn y khoa tối quan trọng, hỗ trợ kiểm soát chất lượng khám và ngăn kết thúc ca khám khi xét nghiệm chưa có kết quả."*

---

## 6. Kỹ thuật viên Tiếp nhận, Thực hiện & Nhập kết quả CLS (2.5 phút)
- **Role:** Kỹ thuật viên CLS (`technician@cliniccare.local` / `Demo@12345`)
- **Trang:** `/diagnostics` (Bàn làm việc Kỹ thuật viên Cận lâm sàng)
- **Thao tác:**
  1. Xem thống kê KPI (Chờ thực hiện, Đang thực hiện, Hoàn tất).
  2. Tìm phiếu chỉ định trong danh sách "Chờ thực hiện" -> Bấm **"Tiếp nhận"** (`Ordered` → `InProgress`).
  3. Bấm **"Nhập kết quả"** (`/diagnostics/orders/:id`):
     - Dịch vụ CBC: Nhập kết quả chi tiết (*WBC: 7.2 G/L, RBC: 4.6 T/L, HGB: 142 g/L*), kết luận (*Chỉ số máu trong giới hạn bình thường*), khoảng tham chiếu (*WBC: 4.0-10.0*), đơn vị (*G/L*).
     - Dịch vụ Siêu âm ổ bụng: Nhập kết quả (*Gan, mật, tụy, lách, thận kích thước bình thường*), kết luận (*Chưa phát hiện bất thường*).
  4. Bấm **"Hoàn tất phiếu chỉ định"** (`InProgress` → `Completed`).

---

## 7. Bác sĩ Duyệt Kết quả Cận lâm sàng & Hoàn tất Ca khám (2 phút)
- **Role:** Bác sĩ (`doctor@cliniccare.local`)
- **Trang:** Quay lại `/doctor/appointments/:id/examination` - Tab **"Chỉ định Cận lâm sàng"**
- **Thao tác:**
  1. Danh sách phiếu cập nhật trạng thái **"Đã có kết quả"** với chi tiết các trị số do KTV nhập.
  2. **Thử nghiệm Completion Guard #2:** Thử bấm **"Hoàn tất ca khám"** ngay khi chưa duyệt.
     - **Kết quả:** Hệ thống tiếp tục chặn: *"Không thể hoàn tất phiên khám khi có kết quả cận lâm sàng chưa được bác sĩ xem và xác nhận."*
  3. Bác sĩ bấm nút **"Xác nhận đã xem kết quả"** -> Phiếu hiển thị dấu tích xanh và ghi nhận thời gian duyệt của bác sĩ.
  4. Bác sĩ xem, đối chiếu kết quả và hoàn thiện chẩn đoán, kê đơn thuốc và bấm **"Hoàn tất ca khám"** -> Ca khám chuyển sang `Completed` thành công.

---

## 8. Bệnh nhân Tra cứu Kết quả CLS Cá nhân (1 phút)
- **Role:** Bệnh nhân (`patient@cliniccare.local` / `Demo@12345` hoặc SĐT của bệnh nhân trong ca hẹn)
- **Trang:** Cổng thông tin kết quả (`/patient/diagnostic-results`)
- **Thao tác:**
  1. Xem danh sách phiếu CLS cá nhân: Thấy huy hiệu xanh **"Đã có kết luận bác sĩ"**.
  2. Nhấp mở Accordion: Đọc chi tiết từng kết quả xét nghiệm, kết luận dễ hiểu, kèm ghi nhận bác sĩ đã xem lúc mấy giờ.
  3. Chuyển sang tab **"Chỉ số sinh hiệu"**: Xem lịch sử các lần đo sinh hiệu tại phòng khám.
  - **Thuyết minh:** *"Bệnh nhân chủ động nắm bắt hồ sơ sức khỏe của mình, đồng thời hệ thống bảo đảm cách ly dữ liệu tuyệt đối giữa các bệnh nhân."*

---

## 9. Tổng kết & Điểm nổi bật Y tế
1. **Clinical State Machine & Completion Guards chặt chẽ:** Giảm thiểu tối đa sai sót quy trình do kết thúc khám trước khi có kết quả xét nghiệm hoặc quên xác nhận kết quả.
2. **Longitudinal Anthropometrics:** Bác sĩ nắm bắt nhanh chóng biến thiên thể trọng và BMI qua các lần khám theo ngưỡng phân loại tiêu chuẩn.
3. **Role-Based Workflows chuyên biệt:** Bác sĩ chỉ định -> KTV thực hiện -> Bác sĩ đối chiếu và xác nhận -> Bệnh nhân tra cứu.
4. **Optimistic Concurrency & Data Isolation:** Bảo vệ an toàn dữ liệu và quyền riêng tư theo tiêu chuẩn y tế.

