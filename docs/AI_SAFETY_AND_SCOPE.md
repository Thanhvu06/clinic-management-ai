# Nguyên Tắc An Toàn & Phạm Vi Hoạt Động Của Trợ Lý AI (AI Safety & Scope)

Văn bản này định nghĩa ranh giới an toàn, phạm vi trách nhiệm và các biện pháp phòng vệ kỹ thuật được tích hợp trong **ClinicCare AI Action Assistant**.

---

## 1. Tôn Chỉ Hoạt Động Cốt Lõi

1. **AI là Trợ Lý Định Hướng, Không Phải Bác Sĩ**:
   - Trợ lý AI tuyệt đối **không thực hiện chẩn đoán y khoa chính thức**, không kết luận bệnh tật và không đưa ra phác đồ điều trị.
   - Mọi thông tin phản hồi đều đi kèm thông điệp miễn trừ trách nhiệm y khoa: *"Thông tin chỉ mang tính tham khảo, không thay thế chẩn đoán của bác sĩ chuyên khoa."*

2. **Không Tự Động Kê Đơn Thuốc**:
   - AI từ chối mọi yêu cầu gợi ý liều lượng thuốc, hướng dẫn đổi thuốc hoặc kê đơn thuốc trực tuyến.
   - Khuyến cáo bệnh nhân tham khảo ý kiến bác sĩ hoặc dược sĩ tại phòng khám.

3. **Tuyệt Đối Không Bịa Đặt Dữ Liệu (No Hallucination)**:
   - Danh sách chuyên khoa chỉ được chọn từ danh mục chuẩn hóa 11 chuyên khoa (`SP01` đến `SP11`) đang kích hoạt trong DB (`IsActive == true`, `AiEnabled == true`).
   - Danh sách bác sĩ, lịch làm việc và khung giờ khám (`AppointmentSlot`) được truy vấn trực tiếp theo thời gian thực.
   - Không bịa đặt giá dịch vụ, số phòng khám hay tạo slot khám ảo.

---

## 2. Các Tầng Bảo Vệ Kỹ Thuật (Multi-layered Technical Guards)

### Tầng 1: Quy Tắc Cấp Cứu Ưu Tiên Tuyệt Đối & Nhận Diện Phủ Định (Emergency Priority & Negation Handling)
- Trước khi thực hiện bất kỳ lệnh gọi nào tới LLM hoặc bộ phân loại, hệ thống quét tin nhắn của người dùng dựa trên bộ từ khóa dấu hiệu nguy hiểm (Red Flags):
  - Đau ngực dữ dội, đau thắt ngực lan tay trái/hàm.
  - Khó thở cấp, suy hô hấp, tím tái, ngừng thở.
  - Đột quỵ, liệt mặt, yếu nửa người, nói đớ/mất ngôn ngữ đột ngột.
  - Bất tỉnh, ngất xỉu, co giật kéo dài.
  - Nôn ra máu, xuất huyết ồ ạt, chấn thương đầu nghiêm trọng.
- **Xử lý Phủ định (Negation Context)**: Hệ thống sử dụng thuật toán kiểm tra ngữ cảnh phủ định phía trước (các từ như *"không"*, *"chưa"*, *"hết"*, *"đỡ"*, *"không bị"*, *"chẳng"*). Nếu người dùng nói *"Tôi hơi mệt nhưng không khó thở và không đau ngực"*, hệ thống không kích hoạt cấp cứu sai mà tiếp tục tư vấn thông thường (`ROUTINE`).
- **Hành động khi phát hiện cấp cứu thật**: Hệ thống lập tức trả về `Urgency = EMERGENCY`, đính kèm nút hành động `CallEmergency` kết nối tới đường dây nóng `tel:115`. Không lưu trữ bản nháp đặt lịch, không điều hướng đặt khám thường. Mô hình LLM bên ngoài hoàn toàn **không được gọi**.

### Tầng 2: Bảo Vệ Dữ Liệu Định Danh Cá Nhân (PII Defense)
- Hệ thống quét nhận diện biểu thức chính quy (Regex) nghiêm ngặt đối với các thông tin nhạy cảm:
  - Căn cước công dân: 9 số hoặc 12 số (`\b\d{9}\b`, `\b\d{12}\b`).
  - Số điện thoại di động Việt Nam: `(?:\+84|0)[35789]\d{8}` (hỗ trợ cả định dạng `0900000003` và `+84900000003`, tránh chặn nhầm tuổi tác hoặc số ngày bị bệnh).
  - Địa chỉ Email cá nhân: `[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}`.
- **Hành động**: Khi phát hiện PII, hệ thống từ chối chuyển tiếp nội dung thô tới bên thứ ba, đồng thời gửi phản hồi yêu cầu bệnh nhân mô tả triệu chứng mà không kèm thông tin định danh nhằm bảo đảm quyền riêng tư y tế.

### Tầng 3: Phòng Chống Prompt Injection & Jailbreak
- Hệ thống nhận diện các mẫu câu cố tình ghi đè hướng dẫn hệ thống: *"bỏ qua quy tắc"*, *"ignore previous"*, *"đóng vai bác sĩ"*, *"hãy chẩn đoán"*, *"kê thuốc"*, *"xuất toàn bộ dữ liệu"*, *"system prompt"*, *"developer mode"*.
- **Hành động**: Trả về phản hồi an toàn từ chối thực hiện, giữ vững vai trò trợ lý điều phối hành chính và định hướng chuyên khoa.

### Tầng 4: Ràng Buộc Hoạt Động Phòng Khám — Ngày Chủ Nhật (Sunday Rule)
- Phòng khám ClinicCare không mở cửa tiếp nhận khám vào ngày Chủ nhật.
- **Quy tắc ứng xử**: Khi bệnh nhân yêu cầu ngày khám rơi vào Chủ nhật (hoặc các cụm từ tương đối như *"Chủ nhật tới"*):
  - Hệ thống **tuyệt đối không âm thầm chuyển ngày sang Thứ Hai** mà không thông báo.
  - Trợ lý giải thích rõ: *"Phòng khám không mở lịch khám vào Chủ nhật ({ngày}). Bạn có thể chọn ngày làm việc kế tiếp (Thứ Hai, {ngày kế}) hoặc một ngày khác nhé."*
  - Cung cấp nút hành động `ChangePreferredDate` chứa ngày Thứ Hai để bệnh nhân chủ động xem xét và bấm chọn.

### Tầng 5: Chống Xung Đột Concurrency & Đặt Trùng Lịch (409 Slot Conflict & Idempotency)
- Khi bệnh nhân bấm nút `ConfirmBooking` trong giao diện trò chuyện, nút bấm kích hoạt cờ `submittingBooking = true` nhằm ngăn chặn click đúp.
- **Xác nhận Idempotent**: Nếu cùng một bệnh nhân bấm xác nhận lại cùng một slot mà họ đã giữ trước đó, hệ thống trả về thông tin lịch hẹn hiện có với mã HTTP 200/201 mà không báo lỗi 409.
- **Xử lý xung đột slot khác người dùng**: Nếu slot khám đã bị bệnh nhân khác đặt trước, hệ thống bắt mã lỗi `409 SLOT_ALREADY_BOOKED`. Hook `useAiBookingFlow` bảo lưu triệu chứng gốc, hiển thị thông báo thân thiện và tự động truy vấn danh sách slot trống còn lại của bác sĩ để bệnh nhân chọn lại ngay trong khung chat.

### Tầng 6: Rate Limiting Chống Tấn Công & Lạm Dụng
- Áp dụng chính sách `AiChatPolicy`: Tối đa 8 yêu cầu/phút trên mỗi tài khoản bệnh nhân (trên môi trường Testing tự động mở rộng lên 1000 để phục vụ test suite tự động).
- Khi vượt ngưỡng, hệ thống trả về HTTP 429 kèm thông báo tiếng Việt lịch sự: *"Bạn đã gửi quá nhiều yêu cầu. Vui lòng thử lại sau 1 phút."*

### Tầng 7: Allowlist Đường Dẫn An Toàn (`SafeRoutes`)
- Mọi action trả về cho người dùng bắt buộc phải trỏ đến các route hợp lệ trong hệ thống (`/patient/invoices`, `/patient/appointments`, `/patient/prescriptions`, `/patient/diagnostic-results`, `/patient/book`, `/doctors`, `/specialties`, `tel:115`).
- Tuyệt đối cấm các route không tồn tại (như `/contact`), các route của phân hệ khác, hoặc các scheme nguy hiểm (`javascript:`, `data:`).

---

## 3. Quản Lý Mô Hình Học Máy Thử Nghiệm (ML Demo Governance)

- Mô hình phân loại triệu chứng huấn luyện cục bộ bằng ML.NET được phân loại rõ ràng là **mô hình thử nghiệm kỹ thuật (Demo/Simulated Model)**, không được tự ý công bố là đạt chuẩn chẩn đoán lâm sàng.
- Tùy chọn cấu hình `RequireClinicallyValidated` trong `appsettings.json` được kích hoạt mặc định (`true`) trên môi trường sản xuất.
- Bất kỳ file mô hình nào có cờ `clinicallyValidated: false` trong metadata sẽ bị từ chối nạp, đảm bảo an toàn tuyệt đối cho người dùng cuối.
- Việc chuyển cờ `clinicallyValidated` sang `true` bắt buộc phải có biên bản thẩm định lâm sàng (`ClinicalApprovalManifest`) do hội đồng chuyên môn y tế phê duyệt kèm số giấy phép hành nghề của bác sĩ chịu trách nhiệm.
