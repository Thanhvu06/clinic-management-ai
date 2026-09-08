# Tài Liệu Kỹ Thuật: ClinicCare AI Action Assistant

Tài liệu này mô tả kiến trúc, nguyên lý hoạt động, Action Parity Matrix đầy đủ 19 hành động, contract dữ liệu và quy trình tích hợp của **ClinicCare AI Action Assistant** — trợ lý y tế hội thoại định hướng hành động (Action-Oriented AI Assistant) dành riêng cho hệ thống phòng khám ClinicCare.

---

## 1. Mục Tiêu & Kiến Trúc Tổng Quan

ClinicCare AI Action Assistant không chỉ là chatbot sinh văn bản thông thường, mà là một **trợ lý có năng lực hành động (Action-Capable Assistant)** được neo dữ liệu thực tế (Grounded) với cơ sở dữ liệu phòng khám:
1. **Hội thoại tiếng Việt tự nhiên**: Hỗ trợ bệnh nhân chia sẻ triệu chứng, lắng nghe và phân loại mức độ khẩn cấp (`ROUTINE`, `SOON`, `EMERGENCY`).
2. **Neo dữ liệu DB thật (Database Grounding)**: Gợi ý chuyên khoa, bác sĩ, khung giờ khám dựa trên danh mục thực tế đang hoạt động (`IsActive == true`, `AiEnabled == true`), không bịa đặt chuyên khoa hay giá dịch vụ.
3. **Mã chuyên khoa Canonical (`SP01` - `SP11`)**: Đồng bộ danh mục 11 chuyên khoa chuẩn hóa từ `DevelopmentDataSeeder.cs`, từ chối mã có dấu gạch ngang (`SP-01`) và cô lập các chuyên khoa không thuộc danh mục phòng khám.
4. **Thực thi hành động trực tiếp từ chat (Chat-to-Action)**: Cho phép xem chi tiết chuyên khoa, chọn bác sĩ, chọn khung giờ trống, sinh bản nháp đặt lịch (`BookingDraft`) và đặt lịch khám sau khi bệnh nhân xác nhận.
5. **Bảo tồn lý do khám ban đầu (Reason Preservation)**: Triệu chứng ban đầu của người dùng (ví dụ: *"đau ngực khi leo cầu thang"*) được bảo tồn xuyên suốt các lượt hội thoại, không bị ghi đè thành *"Tôi chọn khung giờ này"*.
6. **Quy tắc Chủ nhật nghiêm ngặt**: Khi người dùng yêu cầu ngày Chủ nhật, hệ thống giải thích rõ phòng khám đóng cửa vào Chủ nhật, trả về action `ChangePreferredDate` gợi ý Thứ Hai kế tiếp để người dùng chủ động chọn, tuyệt đối không âm thầm chuyển ngày.
7. **Đặt lịch Idempotent & Chống xung đột (409 Conflict)**: API đặt lịch kiểm tra lịch đã giữ của chính bệnh nhân để trả về thành công an toàn (idempotent), đồng thời phục hồi tự động khi có xung đột slot với bệnh nhân khác.
8. **An toàn y khoa & bảo vệ dữ liệu**: Nhận diện ngữ cảnh phủ định cấp cứu (*"tôi không khó thở"*), chặn PII (CCCD, SĐT, Email), chặn prompt injection, ưu tiên quy tắc cấp cứu (115) trước khi gọi LLM.

```
+-----------------------------------------------------------------------------------+
|                                  BỆNH NHÂN (UI)                                  |
|   - MedicalChatWidget (Floating Widget - Floating Launcher)                       |
|   - PatientAiConsultation (Full Page: /patient/ai-consultation)                  |
|   - Hook dùng chung: useAiBookingFlow (Discriminated Union, 0 `any`)              |
+------------------------------------------+----------------------------------------+
                                           | HTTP POST /api/v1/ai/chat
                                           v
+-----------------------------------------------------------------------------------+
|                         BACKEND: AiSpecialtyController                            |
|   - Rate Limiting: 8 req/min (AiChatPolicy, Testing env: 1000 req/min)             |
|   - Authentication: Role "Patient"                                               |
+------------------------------------------+----------------------------------------+
                                           v
+-----------------------------------------------------------------------------------+
|                         AiSpecialtyService (Grounding Engine)                     |
|                                                                                   |
|   [1] Cấp cứu Rule Filter ---> Trả ngay EMERGENCY + Gọi 115 (Kiểm tra phủ định)   |
|   [2] PII Filter ------------> Chặn SĐT/Email/CCCD, yêu cầu xóa định danh         |
|   [3] Prompt Injection Guard -> Chặn override rules / đóng vai bác sĩ             |
|   [4] ML.NET Classifier Hook -> Phân loại triệu chứng (nếu có model hợp lệ)       |
|   [5] LLM Provider (Gemini) -> Trích xuất ý định, chuyên khoa, bác sĩ, ngày giờ  |
|   [6] DB Grounding Layer ----> Đối chiếu DB: 11 Canonical Specialties, Doctor,    |
|                                Available Slots, Quy tắc Chủ nhật                  |
|   [7] Action Builder --------> Tạo Action DTOs, Booking Draft (Preserve Reason)  |
|   [8] Action Validator ------> Lọc SafeRoutes (/patient/invoices, tel:115, ...)   |
+------------------------------------------+----------------------------------------+
                                           v
+-----------------------------------------------------------------------------------+
|                                 RESPONSE DTO                                      |
|   - Message: Lời phản hồi tiếng Việt tự nhiên, ân cần                            |
|   - Urgency: ROUTINE | SOON | EMERGENCY                                           |
|   - SpecialtySuggestions: Danh sách chuyên khoa thật từ DB (SP01 - SP11)          |
|   - BookingDraft: Bản nháp thông tin khám (Bác sĩ, Ngày, Giờ, Lý do)             |
|   - Actions: Danh sách nút hành động strongly-typed từ Action Registry            |
+-----------------------------------------------------------------------------------+
```

---

## 2. Action Parity Matrix Đầy Đủ 19 Hành Động

Dưới đây là ma trận đối soát tuyệt đối giữa Backend (`AiActionTypes.cs`, `AiDtos.cs`), Frontend Discriminated Union (`types/ai.ts`, `useAiBookingFlow.ts`), và Route đích (`SafeRoutes`):

| # | Action Type | Ý Nghĩa Nghiệp Vụ | Style | Auth | Confirm | Target Route / Behavior | Frontend Handler (`useAiBookingFlow`) |
|---|---|---|---|---|---|---|---|
| 1 | `ViewSpecialty` | Xem chi tiết chuyên khoa & chuyển sang đặt lịch | `secondary` | ❌ | ❌ | `/patient/book?specialtyId={id}` | `navigate('/patient/book?specialtyId=...')` |
| 2 | `ViewDoctors` | Xem danh sách bác sĩ chuyên khoa | `secondary` | ❌ | ❌ | `/doctors?specialtyId={id}` hoặc `/doctors` | `navigate('/doctors...')` |
| 3 | `ViewAvailableSlots` | Tra cứu slot khám còn trống của bác sĩ | `secondary` | ❌ | ❌ | Chat query | `handleSendMessage("Xem lịch trống khả dụng", {...})` |
| 4 | `StartBooking` | Bắt đầu quy trình đặt lịch khám | `primary` | ❌ | ❌ | `/patient/book` | `navigate('/patient/book')` |
| 5 | `SelectDoctor` | Chọn bác sĩ cụ thể, bảo lưu chuyên khoa & lý do | `secondary` | ❌ | ❌ | Chat query | Cập nhật draft, gửi message chọn bác sĩ |
| 6 | `SelectSlot` | Chọn khung giờ khám, bảo lưu bác sĩ & lý do | `primary` | ❌ | ❌ | Chat query | Cập nhật draft, gửi message chọn slot |
| 7 | `ReviewBooking` | Xem lại toàn bộ thông tin bản nháp trước khi chốt | `secondary` | ✅ | ❌ | Chat render card | Hiển thị thẻ tóm tắt xác nhận + nút `ConfirmBooking` |
| 8 | `ConfirmBooking` | Xác nhận đặt lịch chính thức qua API | `primary` | ✅ | ✅ | `POST /api/v1/appointments` | Gọi API với guard chống click đúp, xử lý 409 conflict |
| 9 | `ChangePreferredDate` | Đổi ngày khám mong muốn (VD: sang Thứ Hai) | `secondary` | ❌ | ❌ | Chat query | Cập nhật draft slotDate, gửi message tra cứu ngày mới |
| 10 | `ViewMyAppointments` | Điều hướng xem danh sách lịch hẹn cá nhân | `primary` | ✅ | ❌ | `/patient/appointments` | `navigate('/patient/appointments')` |
| 11 | `OpenAppointmentDetail` | Xem chi tiết một cuộc hẹn cụ thể | `secondary` | ✅ | ❌ | `/patient/appointments` | `navigate('/patient/appointments')` |
| 12 | `RequestReschedule` | Yêu cầu đổi lịch cuộc hẹn đã đặt | `secondary` | ✅ | ❌ | `/patient/appointments` | `navigate('/patient/appointments')` |
| 13 | `RequestCancellation` | Yêu cầu hủy cuộc hẹn đã đặt | `secondary` | ✅ | ❌ | `/patient/appointments` | `navigate('/patient/appointments')` |
| 14 | `ViewDiagnosticResults`| Điều hướng xem kết quả cận lâm sàng | `secondary` | ✅ | ❌ | `/patient/diagnostic-results` | `navigate('/patient/diagnostic-results')` |
| 15 | `ViewPrescriptions` | Điều hướng xem danh sách đơn thuốc | `secondary` | ✅ | ❌ | `/patient/prescriptions` | `navigate('/patient/prescriptions')` |
| 16 | `ViewBills` | Điều hướng xem hóa đơn & viện phí | `secondary` | ✅ | ❌ | `/patient/invoices` *(Canonical Route)* | `navigate('/patient/invoices')` *(Tuyệt đối không `/contact`)* |
| 17 | `ContactReception` | Xem hotline và địa chỉ quầy lễ tân | `secondary` | ❌ | ❌ | In-chat desk info (`1900 1234`) | Render thông tin quầy tiếp đón trực tiếp trong chat |
| 18 | `ManualSpecialtySelection`| Tự chọn chuyên khoa thủ công | `secondary` | ❌ | ❌ | `/patient/book` | `navigate('/patient/book')` |
| 19 | `CallEmergency` | Gọi cấp cứu y tế 115 khẩn cấp | `danger` | ❌ | ❌ | `tel:115` | `window.location.href = 'tel:115'` |

---

## 3. Danh Mục Chuyên Khoa Chuẩn Hóa (Canonical Specialty Catalog)

Hệ thống định nghĩa danh mục 11 chuyên khoa chuẩn tắc tại [`CanonicalSpecialties.cs`](../src/backend/ClinicManagement.Application/AI/Constants/CanonicalSpecialties.cs), hoàn toàn đồng bộ với dữ liệu seed trong `DevelopmentDataSeeder.cs`:

| Mã chuẩn | Tên chuyên khoa | Mô tả lâm sàng |
|---|---|---|
| `SP01` | Nội tổng quát | Khám và điều trị các bệnh lý nội khoa chung |
| `SP02` | Nhi khoa | Khám, chẩn đoán và điều trị bệnh cho trẻ em |
| `SP03` | Sản phụ khoa | Khám thai định kỳ và tư vấn sức khỏe phụ khoa |
| `SP04` | Da liễu | Chuyên trị các vấn đề về da, tóc và móng |
| `SP05` | Tai mũi họng | Khám và điều trị bệnh lý tai mũi họng |
| `SP06` | Tim mạch | Kiểm tra huyết áp, đo điện tâm đồ và bệnh lý tim mạch |
| `SP07` | Cơ xương khớp | Điều trị viêm khớp, thoái hóa khớp và chấn thương |
| `SP08` | Thần kinh | Khám và điều trị các bệnh lý thần kinh và đau đầu |
| `SP09` | Nội tiết | Khám và điều trị bệnh lý tiểu đường, tuyến giáp |
| `SP10` | Nhãn khoa | Khám và điều trị các bệnh lý về mắt và thị lực |
| `SP11` | Tiêu hóa | Khám và điều trị các bệnh lý dạ dày, đại tràng |

*Lưu ý:* Các mã dạng `SP-01` (có dấu gạch ngang) hoặc các chuyên khoa không thuộc danh mục phòng khám (như Nha khoa `CASE-017`, `CASE-018`) đã được cách ly vào `quarantined_records.json` và bị từ chối trong quy trình kiểm định dataset.

---

## 4. Hợp Đồng Dữ Liệu (API Contracts)

### 4.1. Request Contract (`AiChatRequestDto`)
```json
{
  "message": "Tôi muốn đặt lịch khám với BS Nguyễn Minh Khải vào thứ Hai tới",
  "context": [
    { "role": "user", "content": "Tôi hay bị nhói ngực khi leo cầu thang" },
    { "role": "model", "content": "Triệu chứng của bạn có thể liên quan tới Tim Mạch..." }
  ],
  "pendingSpecialtyId": 6,
  "pendingDoctorId": 2,
  "pendingSlotId": 105,
  "pendingSlotDate": "2026-09-14",
  "reason": "Nhói ngực khi leo cầu thang"
}
```

### 4.2. Response Contract (`AiChatResponseDto`)
```json
{
  "message": "Tôi đã tìm thấy lịch khám trống của BS.CKI Nguyễn Minh Khải. Bạn vui lòng kiểm tra và bấm xác nhận:",
  "urgency": "ROUTINE",
  "safetyNotice": "Thông tin chỉ mang tính tham khảo, không thay thế chẩn đoán y khoa.",
  "specialtySuggestions": [
    {
      "specialtyId": 6,
      "specialtyCode": "SP06",
      "specialtyName": "Tim mạch",
      "rank": 1,
      "reason": "Phù hợp để tham khảo dựa trên thông tin triệu chứng bạn cung cấp."
    }
  ],
  "bookingDraft": {
    "specialtyId": 6,
    "specialtyName": "Tim mạch",
    "doctorId": 2,
    "doctorName": "BS.CKI Nguyễn Minh Khải",
    "slotId": 105,
    "slotDate": "2026-09-14",
    "startTime": "08:30",
    "endTime": "09:00",
    "reason": "Nhói ngực khi leo cầu thang",
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
        "specialtyId": 6,
        "specialtyName": "Tim mạch",
        "doctorId": 2,
        "doctorName": "BS.CKI Nguyễn Minh Khải",
        "slotId": 105,
        "slotDate": "2026-09-14",
        "startTime": "08:30",
        "endTime": "09:00",
        "reason": "Nhói ngực khi leo cầu thang"
      }
    }
  ],
  "promptVersion": "v1.0.0",
  "manualSelectionRequired": false
}
```

---

## 5. Quy Trình Đặt Lịch & Xử Lý Ngoại Lệ

1. **Khởi tạo**: Bệnh nhân nhắn triệu chứng ban đầu (VD: *"đau ngực khi gắng sức"*). Trợ lý trích xuất lý do khám này vào bộ nhớ hội thoại.
2. **Định hướng**: AI nhận diện chuyên khoa chuẩn (`SP06` Tim mạch) và truy vấn danh mục DB thật.
3. **Lựa chọn Bác sĩ & Khung giờ**: Bệnh nhân chọn bác sĩ và slot. Trợ lý liên tục truyền `reason` gốc giữa các lượt thoại để đảm bảo không bị mất ngữ cảnh bệnh lý.
4. **Quy tắc Chủ nhật**: Nếu slotDate rơi vào Chủ nhật:
   - Hệ thống phản hồi: *"Phòng khám không mở lịch khám vào Chủ nhật (13/09/2026). Bạn có thể chọn ngày làm việc kế tiếp (Thứ Hai, 14/09/2026)..."*
   - Trả về action `ChangePreferredDate` chứa ngày Thứ Hai để người dùng bấm chọn.
   - Không tự ý chuyển ngày ngầm.
5. **Xác nhận Đặt lịch (Idempotent Booking)**:
   - Khi bấm `ConfirmBooking`, client bật cờ `submittingBooking = true` ngăn chặn click đúp.
   - Backend kiểm tra nếu cùng bệnh nhân gửi lại request đặt cùng slot đã giữ, hệ thống trả về lịch hẹn hiện có mà không báo lỗi.
6. **Xử lý Xung đột Slot (409 Slot Conflict)**:
   - Nếu slot bị người khác đặt trước, API trả về `409 SLOT_ALREADY_BOOKED`.
   - Hook `useAiBookingFlow` giữ nguyên bản nháp (kèm triệu chứng gốc), hiển thị thông báo nhẹ nhàng và tự động gửi request truy vấn các slot trống còn lại của bác sĩ để bệnh nhân chọn lại ngay trong khung chat.
