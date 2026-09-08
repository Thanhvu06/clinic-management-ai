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
   - Danh sách chuyên khoa chỉ được chọn từ danh sách đang kích hoạt trong DB (`IsActive == true`, `AiEnabled == true`).
   - Danh sách bác sĩ, lịch làm việc và khung giờ khám (`AppointmentSlot`) được truy vấn trực tiếp theo thời gian thực.
   - Không bịa đặt giá dịch vụ, số phòng khám hay tạo slot khám ảo.

---

## 2. Các Tầng Bảo Vệ Kỹ Thuật (Multi-layered Technical Guards)

### Tầng 1: Quy Tắc Cấp Cứu Ưu Tiên Tuyệt Đối (Emergency Priority Rule)
- Trước khi thực hiện bất kỳ lệnh gọi nào tới mô hình ngôn ngữ lớn (LLM) hoặc bộ phân loại, hệ thống quét tin nhắn của người dùng dựa trên bộ từ khóa dấu hiệu nguy hiểm (Red Flags):
  - Đau ngực dữ dội, đau thắt ngực lan tay trái/hàm.
  - Khó thở cấp, suy hô hấp, tím tái, ngừng thở.
  - Đột quỵ, liệt mặt, yếu nửa người, nói đớ/mất ngôn ngữ đột ngột.
  - Bất tỉnh, ngất xỉu, co giật kéo dài.
  - Nôn ra máu, xuất huyết ồ ạt, chấn thương đầu nghiêm trọng.
- **Hành động**: Hệ thống lập tức trả về `Urgency = EMERGENCY`, đính kèm nút hành động `CallEmergency` kết nối tới đường dây nóng `tel:115`. Không lưu trữ bản nháp đặt lịch, không điều hướng đặt khám thường. Mô hình LLM bên ngoài hoàn toàn **không được gọi**.

### Tầng 2: Bảo Vệ Dữ Liệu Định Danh Cá Nhân (PII Defense)
- Hệ thống quét nhận diện biểu thức chính quy (Regex) đối với các thông tin nhạy cảm:
  - Căn cước công dân (9 số hoặc 12 số).
  - Số điện thoại di động Việt Nam (`03x`, `05x`, `07x`, `08x`, `09x`, `+84x`).
  - Địa chỉ Email cá nhân.
- **Hành động**: Khi phát hiện PII, hệ thống từ chối chuyển tiếp nội dung thô tới bên thứ ba, đồng thời gửi phản hồi yêu cầu bệnh nhân mô tả triệu chứng mà không kèm thông tin định danh nhằm bảo đảm quyền riêng tư y tế.

### Tầng 3: Phòng Chống Prompt Injection & Jailbreak
- Hệ thống nhận diện các mẫu câu cố tình ghi đè hướng dẫn hệ thống, yêu cầu "đóng vai bác sĩ", "bỏ qua quy tắc", "hiển thị system prompt", "developer mode".
- **Hành động**: Trả về phản hồi an toàn từ chối thực hiện, giữ vững vai trò trợ lý điều phối hành chính và định hướng chuyên khoa.

### Tầng 4: Ràng Buộc Hoạt Động Phòng Khám — Ngày Chủ Nhật (Sunday Clinic Closure)
- Phòng khám không tiếp nhận đặt lịch vào Chủ nhật.
- Khi bệnh nhân yêu cầu lịch khám vào ngày Chủ nhật (hoặc các yêu cầu tương đối như "ngày mai", "cuối tuần" rơi vào Chủ nhật), thuật toán giải quyết ngày sẽ tự động chuyển tiếp sang ngày thứ Hai làm việc gần nhất.
- Ngăn chặn triệt để lỗi ngoại lệ `DOCTOR_NOT_AVAILABLE` và bảo vệ trải nghiệm của bệnh nhân.

### Tầng 5: Chống Xung Đột Concurrency & Đặt Trùng Lịch (409 Slot Conflict)
- Khi bệnh nhân bấm nút `ConfirmBooking` trong giao diện trò chuyện, nút bấm kích hoạt cờ `submittingBooking = true` nhằm ngăn chặn click đúp.
- Quá trình đặt lịch được bảo vệ bằng transaction cô lập ở tầng cơ sở dữ liệu.
- Nếu slot khám đã được giữ trước bởi bệnh nhân khác, hệ thống xử lý mã lỗi `409 SLOT_ALREADY_BOOKED` và tự động tìm kiếm các khung giờ khác của cùng bác sĩ, giúp bệnh nhân chọn lại nhanh chóng mà không bị kẹt phiên làm việc.

### Tầng 6: Rate Limiting Chống Tấn Công Dồn Dập
- Áp dụng chính sách `AiChatPolicy`: Tối đa 8 yêu cầu/phút trên mỗi tài khoản bệnh nhân.
- Khi vượt ngưỡng, hệ thống trả về mã lỗi `TOO_MANY_REQUESTS` kèm thông báo tiếng Việt lịch sự, ngăn chặn spam và lạm dụng tài nguyên tính toán.

---

## 3. Quản Lý Mô Hình Học Máy Thử Nghiệm (ML Demo Governance)

- Mô hình phân loại triệu chứng huấn luyện cục bộ bằng ML.NET được phân loại rõ ràng là **mô hình thử nghiệm kỹ thuật (Demo/Simulated Model)**, không được tự ý công bố là đạt chuẩn chẩn đoán lâm sàng.
- Tùy chọn cấu hình `RequireClinicallyValidated` trong `appsettings.json` được kích hoạt mặc định (`true`) trên môi trường sản xuất.
- Bất kỳ file mô hình nào có cờ `clinicallyValidated: false` trong metadata sẽ bị từ chối nạp, đảm bảo an toàn tuyệt đối cho người dùng cuối.
