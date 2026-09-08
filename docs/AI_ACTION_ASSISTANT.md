# Tài Liệu Kỹ Thuật: ClinicCare AI Action Assistant

Tài liệu này mô tả kiến trúc, nguyên lý hoạt động, contract dữ liệu và quy trình tích hợp của **ClinicCare AI Action Assistant** — trợ lý y tế hội thoại định hướng hành động (Action-Oriented AI Assistant) dành riêng cho hệ thống phòng khám ClinicCare.

---

## 1. Mục Tiêu & Kiến Trúc Tổng Quan

ClinicCare AI Action Assistant không chỉ là chatbot sinh văn bản thông thường, mà là một **trợ lý có năng lực hành động (Action-Capable Assistant)** được neo dữ liệu thực tế (Grounded) với cơ sở dữ liệu phòng khám:
1. **Hội thoại tiếng Việt tự nhiên**: Hỗ trợ bệnh nhân chia sẻ triệu chứng, lắng nghe và phân loại mức độ khẩn cấp.
2. **Neo dữ liệu DB thật (Database Grounding)**: Gợi ý chuyên khoa, bác sĩ, khung giờ khám dựa trên danh mục thực tế đang hoạt động (`IsActive == true`, `AiEnabled == true`), không bịa đặt chuyên khoa hay giá dịch vụ.
3. **Thực thi hành động trực tiếp từ chat (Chat-to-Action)**: Cho phép xem chi tiết chuyên khoa, chọn bác sĩ, chọn khung giờ trống, sinh bản nháp đặt lịch (`BookingDraft`) và đặt lịch khám sau khi bệnh nhân xác nhận.
4. **Liên kết nghiệp vụ hệ thống**: Hỗ trợ các nút hành động điều hướng nhanh tới Lịch hẹn cá nhân, Kết quả cận lâm sàng, Đơn thuốc, Hóa đơn và Liên hệ lễ tân.
5. **An toàn y khoa & bảo vệ dữ liệu**: Chặn PII (CCCD, SĐT, Email), chặn prompt injection, ưu tiên quy tắc cấp cứu (115) trước khi gọi AI, và tự động tránh đặt lịch vào Chủ nhật.

```
+-----------------------------------------------------------------------------------+
|                                  BỆNH NHÂN (UI)                                  |
|   - MedicalChatWidget (Floating Widget)                                          |
|   - PatientAiConsultation (Full Page: /patient/ai-consultation)                  |
+------------------------------------------+----------------------------------------+
                                           | HTTP POST /api/v1/ai/chat
                                           v
+-----------------------------------------------------------------------------------+
|                         BACKEND: AiSpecialtyController                            |
|   - Rate Limiting: 8 req/min (AiChatPolicy)                                       |
|   - Authentication: Role "Patient"                                               |
+------------------------------------------+----------------------------------------+
                                           v
+-----------------------------------------------------------------------------------+
|                         AiSpecialtyService (Grounding Engine)                     |
|                                                                                   |
|   [1] Cấp cứu Rule Filter ---> Trả ngay EMERGENCY + Gọi 115 (Không gọi AI)        |
|   [2] PII Filter ------------> Chặn SĐT/Email/CCCD, yêu cầu xóa định danh         |
|   [3] Prompt Injection Guard -> Chặn override rules / đóng vai bác sĩ             |
|   [4] ML.NET Classifier Hook -> Phân loại triệu chứng (nếu có model hợp lệ)       |
|   [5] LLM Provider (Gemini) -> Trích xuất ý định, chuyên khoa, bác sĩ, ngày giờ  |
|   [6] DB Grounding Layer ----> Đối chiếu DB: Specialty, Doctor, Available Slots   |
|   [7] Action Builder --------> Tạo Action DTOs, Booking Draft                     |
+------------------------------------------+----------------------------------------+
                                           v
+-----------------------------------------------------------------------------------+
|                                 RESPONSE DTO                                      |
|   - Message: Lời phản hồi tiếng Việt tự nhiên, ân cần                            |
|   - Urgency: ROUTINE | SOON | EMERGENCY                                           |
|   - SpecialtySuggestions: Danh sách chuyên khoa thật từ DB                        |
|   - BookingDraft: Bản nháp thông tin khám (Bác sĩ, Ngày, Giờ, Lý do)             |
|   - Actions: Danh sách nút hành động strongly-typed (ConfirmBooking, v.v.)        |
+-----------------------------------------------------------------------------------+
```

---

## 2. Danh Mục Hành Động (Allowlisted Action Types)

Hệ thống quản lý danh sách hành động chuẩn hoá nghiêm ngặt (`AiActionTypes`). Mọi hành động trả về client bắt buộc phải thuộc danh mục cho phép:

| Action Type | Ý Nghĩa | Style | Xác Thực | Xác Nhận |
|---|---|---|---|---|
| `ViewSpecialty` | Chuyển tới trang đặt lịch lọc theo chuyên khoa | `secondary` | Không | Không |
| `ViewDoctors` | Xem danh sách bác sĩ chuyên khoa | `secondary` | Không | Không |
| `ViewAvailableSlots` | Xem các slot khám trống trong ngày | `secondary` | Không | Không |
| `SelectDoctor` | Chọn một bác sĩ cụ thể để chuẩn bị đặt lịch | `secondary` | Không | Không |
| `SelectSlot` | Chọn một khung giờ cụ thể | `primary` | Không | Không |
| `ReviewBooking` | Xem lại toàn bộ thông tin bản nháp đặt lịch | `primary` | Có | Không |
| `ConfirmBooking` | Xác nhận đặt lịch khám chính thức qua API | `primary` | Có | Có |
| `ChangePreferredDate` | Đổi ngày khám mong muốn | `secondary` | Không | Không |
| `ViewMyAppointments` | Điều hướng tới `/patient/appointments` | `primary` | Có | Không |
| `ViewDiagnosticResults` | Điều hướng tới `/patient/diagnostic-results` | `secondary` | Có | Không |
| `ViewPrescriptions` | Điều hướng tới danh sách đơn thuốc | `secondary` | Có | Không |
| `ViewBills` | Điều hướng tới hóa đơn bệnh nhân | `secondary` | Có | Không |
| `ContactReception` | Gọi hotline lễ tân (`tel:1900xxxx`) | `secondary` | Không | Không |
| `CallEmergency` | Gọi cấp cứu y tế 115 (`tel:115`) | `danger` | Không | Không |
| `ManualSpecialtySelection` | Mở trang tự chọn chuyên khoa thủ công | `secondary` | Không | Không |

---

## 3. Data Transfer Objects (Contracts)

### Request Contract (`AiChatRequestDto`)
```json
{
  "message": "Tôi muốn đặt lịch khám với BS Nguyễn Minh Khải vào thứ Hai tới",
  "context": [
    { "role": "user", "content": "Tôi hay bị nhói ngực khi leo cầu thang" },
    { "role": "model", "content": "Triệu chứng của bạn có thể liên quan tới Tim Mạch..." }
  ],
  "pendingSpecialtyId": 1,
  "pendingDoctorId": 2,
  "pendingSlotId": 105,
  "pendingSlotDate": "2026-09-14",
  "reason": "Khám tim mạch"
}
```

### Response Contract (`AiChatResponseDto`)
```json
{
  "message": "Tôi đã tìm thấy lịch khám trống của BS.CKI Nguyễn Minh Khải. Bạn vui lòng kiểm tra và bấm xác nhận:",
  "urgency": "ROUTINE",
  "safetyNotice": "Thông tin chỉ mang tính tham khảo, không thay thế chẩn đoán y khoa.",
  "specialtySuggestions": [
    {
      "specialtyId": 1,
      "specialtyCode": "SP-01",
      "specialtyName": "Tim Mạch",
      "reason": "Phù hợp với triệu chứng nhói ngực khi gắng sức."
    }
  ],
  "bookingDraft": {
    "specialtyId": 1,
    "specialtyName": "Tim Mạch",
    "doctorId": 2,
    "doctorName": "BS.CKI Nguyễn Minh Khải",
    "slotId": 105,
    "slotDate": "2026-09-14",
    "startTime": "08:30",
    "endTime": "09:00",
    "reason": "Khám tim mạch",
    "isComplete": true
  },
  "actions": [
    {
      "id": "act-confirm-105",
      "type": "ConfirmBooking",
      "label": "Xác nhận đặt lịch ngay",
      "style": "primary",
      "requiresAuthentication": true,
      "requiresConfirmation": true,
      "payload": {
        "specialtyId": 1,
        "specialtyName": "Tim Mạch",
        "doctorId": 2,
        "doctorName": "BS.CKI Nguyễn Minh Khải",
        "slotId": 105,
        "slotDate": "2026-09-14",
        "startTime": "08:30",
        "endTime": "09:00",
        "reason": "Khám tim mạch"
      }
    }
  ]
}
```

---

## 4. Quy Trình Đặt Lịch Qua Chat (Direct Booking Workflow)

1. **Khởi tạo**: Bệnh nhân nhắn triệu chứng hoặc tên bác sĩ.
2. **Định hướng**: AI nhận diện chuyên khoa và kiểm tra cơ sở dữ liệu thật.
3. **Lựa chọn**: Bệnh nhân chọn bác sĩ và khung giờ bằng các nút hành động hoặc lời thoại trực tiếp.
4. **Tóm tắt (Draft)**: AI hiển thị thẻ tóm tắt đặt lịch (`BookingSummaryCard`) màu Navy/Teal với thông tin chuyên khoa, bác sĩ, ngày, giờ, lý do.
5. **Xác nhận**: Bệnh nhân bấm nút **"Xác nhận đặt lịch ngay"**.
   - Client gửi `POST /api/v1/appointments` với `appointmentSlotId`, `doctorId`, `specialtyId`.
   - Nút xác nhận có guard chống click đúp (`submittingBooking`).
6. **Xử lý xung đột (409 Conflict Handling)**:
   - Nếu khung giờ vừa bị bệnh nhân khác đặt trước, API trả về `409 SLOT_ALREADY_BOOKED`.
   - Trợ lý AI tự động thông báo thân thiện và tự động truy vấn lại các slot khám còn trống khác của bác sĩ để bệnh nhân chọn lại ngay trong luồng chat.
