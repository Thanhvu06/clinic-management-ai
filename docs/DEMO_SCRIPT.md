# Kịch bản Demo Dự án ClinicCare AI

**Thời lượng dự kiến:** 8-12 phút.  
**Chuẩn bị:** Mở sẵn tab trình duyệt tại `http://localhost:5173`, chạy sẵn backend và frontend ở chế độ Development.

---

## 1. Giới thiệu tổng quan (1 phút)
- **Role đăng nhập:** Khách (Chưa đăng nhập)
- **Trang mở:** `http://localhost:5173/login`
- **Thao tác:** Mở màn hình đăng nhập.
- **Thuyết minh:** *"Chào thầy/cô, em xin demo hệ thống quản lý phòng khám ClinicCare AI. Hệ thống hỗ trợ 4 vai trò độc lập: Bệnh nhân, Lễ tân, Bác sĩ, Quản trị viên, cùng chức năng lõi là AI hỗ trợ tư vấn chuyên khoa an toàn."*

---

## 2. Bệnh nhân đặt lịch & Trải nghiệm AI (2.5 phút)
- **Role đăng nhập:** Bệnh nhân (`patient@cliniccare.local` / `Demo@12345`)
- **Trang mở:** Dashboard `/patient` -> Chọn "Đặt lịch mới" (`/patient/book`)
- **Thao tác:** 
  1. Nhập triệu chứng: *"Tôi bị đau đầu dai dẳng kéo dài 3 ngày nay, kèm theo buồn nôn..."*
  2. Bấm **"Gợi ý chuyên khoa"**.
  3. Bấm chọn chuyên khoa AI gợi ý. Chọn tiếp Bác sĩ, và chọn Giờ khám. Gửi yêu cầu.
- **Kết quả:** Modal báo đặt thành công kèm mã lịch khám (AppointmentCode). Lịch mới nằm ở trạng thái "Chờ xác nhận".
- **Thuyết minh:** *"Ở góc độ người bệnh, khi không biết khám khoa nào, họ chỉ cần miêu tả triệu chứng. AI sẽ phân tích và khoanh vùng 1-3 chuyên khoa chuẩn xác nhất của phòng khám. Dữ liệu này chỉ mang tính định hướng, hệ thống không tự chẩn đoán bệnh."*

---

## 3. Quản lý lịch hẹn bệnh nhân (0.5 phút)
- **Role đăng nhập:** Bệnh nhân
- **Trang mở:** Lịch hẹn của tôi (`/patient/appointments`)
- **Thao tác:** Chỉ vào dòng lịch vừa tạo đang có nhãn "Chờ xác nhận".
- **Thuyết minh:** *"Bệnh nhân có thể quản lý lịch, theo dõi timeline tiến độ xử lý và gửi yêu cầu Đổi/Hủy nếu cần thiết."*

---

## 4. Lễ tân xác nhận lịch (1 phút)
- **Role đăng nhập:** Lễ tân (`reception@cliniccare.local` / `Demo@12345`)
- **Trang mở:** Quản lý lịch hẹn (`/reception/appointments`)
- **Thao tác:** Bấm làm mới, tìm lịch "Chờ xác nhận" vừa tạo. Bấm "Chi tiết" -> Bấm "Xác nhận".
- **Kết quả:** Trạng thái nhảy sang "Đã xác nhận".
- **Thuyết minh:** *"Lễ tân tiếp nhận lịch online, kiểm tra tính hợp lệ và xác nhận. Khi xác nhận, bệnh nhân sẽ thấy trạng thái đổi tương ứng."*

---

## 5. Bác sĩ khám và Đề xuất tái khám (2 phút)
- **Role đăng nhập:** Bác sĩ (`doctor@cliniccare.local` / `Demo@12345`)
- **Trang mở:** Lịch khám của tôi (`/doctor/appointments`)
- **Thao tác:** 
  1. Tìm lịch "Đã xác nhận" của bệnh nhân trên. Bấm "Khám".
  2. Điền tóm tắt kết quả khám và bấm hoàn tất -> Lịch chuyển sang "Đã hoàn thành".
  3. Bấm "Đề xuất tái khám", chọn ngày mai, điền ghi chú "Tái khám kiểm tra".
- **Kết quả:** Form lưu thành công. Lịch sử Timeline hiện đầy đủ.
- **Thuyết minh:** *"Bác sĩ chỉ thấy lịch của mình. Giao diện tập trung vào việc cập nhật kết quả. Bác sĩ cũng có thể chủ động đề xuất tái khám chỉ với vài lượt click thay vì yêu cầu bệnh nhân ra quầy đặt lại từ đầu."*

---

## 6. Bệnh nhân duyệt tái khám (1 phút)
- **Role đăng nhập:** Bệnh nhân (`patient@cliniccare.local` / `Demo@12345`)
- **Trang mở:** Đề xuất tái khám (`/patient/revisit`)
- **Thao tác:** Xem đề xuất của bác sĩ, bấm "Đồng ý", chọn ca khám phù hợp và Submit.
- **Kết quả:** Lịch tái khám Pending được sinh ra, liên kết với ID lịch cũ.
- **Thuyết minh:** *"Bệnh nhân theo dõi lời dặn tái khám, chủ động chọn giờ linh hoạt theo khung rảnh của mình."*

---

## 7. Quản trị viên và Xử lý đơn nghỉ (1.5 phút)
- **Role đăng nhập:** Quản trị viên (`admin@cliniccare.local` / `Demo@12345`)
- **Trang mở:** Danh mục Bác sĩ, Danh mục Nghỉ phép (`/admin/leaves`).
- **Thao tác:** Mở bảng xin nghỉ, cho thấy giao diện duyệt. (Có thể biểu diễn thao tác Tạo lịch làm việc tại `/admin/work-schedules` nếu cần).
- **Thuyết minh:** *"Admin có cái nhìn toàn cảnh để vận hành: cấu hình chuyên khoa, phân bác sĩ, sinh Slot khám tự động theo ca và duyệt các đơn xin nghỉ phép đột xuất của bác sĩ."*

---

## 8. Kết luận (0.5 phút)
- **Thao tác:** Chỉ lại về kiến trúc và UI/UX chung.
- **Thuyết minh:** *"Hệ thống đảm bảo Role-Based Access Control nghiêm ngặt, dữ liệu được bảo vệ. Về AI, chúng em thiết kế Guardrails chặt chẽ: không lộ PII, xử lý gọn gàng fallback khi AI lỗi, luôn đảm bảo nghiệp vụ vận hành an toàn."*

---

## Các tình huống báo lỗi nổi bật (Nếu còn thời gian)
1. **Slot vừa bị người khác chọn (Concurrency):** Bệnh nhân đặt trùng Slot, hệ thống báo `Khung giờ này vừa có người khác đặt. Vui lòng chọn giờ khác.`
2. **Kiểm soát quyền truy cập chéo:** Dùng tài khoản Patient truy cập thẳng URL `/admin` hoặc `/doctor` -> Bị văng ra màn hình "Bạn không có quyền truy cập" (Forbidden).
3. **AI Fallback:** AI chặn không phân tích nội dung rác (Guardrail), hiển thị toast thông báo: *"Tính năng gợi ý tạm thời không khả dụng. Vui lòng tự chọn chuyên khoa bên dưới."* (Vẫn chạy luồng nghiệp vụ thủ công).
4. **Admin duyệt đơn nghỉ gây ảnh hưởng lịch khám:** Khi Admin bấm duyệt yêu cầu nghỉ của bác sĩ vào ngày đang có bệnh nhân đặt lịch, Backend sẽ chủ động ném exception và UI báo *"Bác sĩ đang có lịch hẹn bị ảnh hưởng. Hãy để lễ tân xử lý các lịch này trước khi duyệt nghỉ."*
