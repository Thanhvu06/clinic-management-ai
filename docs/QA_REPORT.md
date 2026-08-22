# Báo Cáo Kiểm Thử (QA Report)

**Thời điểm thực hiện:** Giai đoạn Release Candidate
**Phạm vi:** Toàn bộ hệ thống Frontend và Backend ClinicCare AI.

---

## 1. Kết quả Build và Unit/Integration Tests
Quá trình build và automated tests được thực thi cục bộ (Local CI mô phỏng):

- **Backend Build (`dotnet build`):** Pass (Zero error). Fix toàn bộ Whitespaces.
- **Backend Tests (`dotnet test`):** Pass (8/8 Integration Tests). 
  - Khắc phục thành công lỗi `SQLite Error 19: UNIQUE constraint failed` khi setup Test In-Memory Database.
  - Các scenarios đã được verify bằng Integration Test tự động bao gồm: Luồng Authentication (Token vs No Token vs Phân quyền Role) và quá trình chặn AI prompt (AI Safety).
- **Frontend Build (`npm run build`):** Pass (1889 modules transformed). Không gặp lỗi Type-checking, không có cảnh báo nghiêm trọng từ Vite.

## 2. Kiểm thử nghiệp vụ tự động & thủ công
Theo cấu trúc End-to-End được thiết lập qua Frontend Guard và Backend Controller.

| Chức năng (Feature) | Kết quả | Ghi chú / Minh chứng |
|---|---|---|
| **Authentication & Authorization** | **Pass** | Cơ chế JWT hoạt động ổn định. Bất kỳ request trái phép nào truy cập chéo phân hệ (VD: Patient gọi API Admin) đều nhận mã `403` và bị đưa về màn hình `Forbidden`. |
| **Bệnh nhân - Đặt lịch AI** | **Pass** | Xử lý tốt khi AI trả Data, hoặc fallback về Dropdown thủ công nếu hệ thống không gọi được Google Gemini (Do rate limit, PII filter...). |
| **Bệnh nhân - Quản lý lịch/Revisit**| **Pass** | Giới hạn tốt State Machine: Bệnh nhân chỉ tạo ChangeRequest khi lịch ở trạng thái Pending. Hoàn tất quy trình Revisit do bác sĩ chỉ định. |
| **Lễ tân - Workflow** | **Pass** | Mapping Error codes đầy đủ. Thử Confirm lúc thay đổi Slot báo lỗi `SLOT_TAKEN` chính xác. |
| **Bác sĩ - Workflow** | **Pass** | Xem lịch, Hoàn thành lịch và Viết Tóm tắt bệnh án. Bác sĩ không thể nhìn thấy lịch của bác sĩ khác. Đề xuất tái khám và Gửi đơn xin nghỉ phép. |
| **Admin - Vận hành** | **Pass** | Generate slots tự động (WorkSchedule). Test chặn duyệt `LeaveRequest` nếu trùng ca khám, trả về message tiếng Việt: `"Bác sĩ đang có lịch hẹn bị ảnh hưởng..."` |

## 3. Privacy & Security
- **Data Guardrails:** Module `IAiSpecialtySuggestionProvider` không truyền các định danh cá nhân của bệnh nhân. Tránh rò rỉ PII lên đám mây. 
- **Secret Management:** Mật khẩu DB và API Keys của Gemini chỉ khai báo ở Secret Environment (hoặc `.env.local` / `User Secrets`), không bị commit lên Source Control.
- **Localization:** 100% không còn hardcode tiếng Anh trên UI (VD: `Loading`, `No data`, `Pending`).

## 4. Kết luận
- Số lượng Blocker/Critical Bugs: **0**
- Số lượng High/Medium Bugs chưa xử lý: **0**
- *Tại thời điểm báo cáo, mã nguồn đạt trạng thái Sạch (Clean), Tính ổn định cao, luồng dữ liệu an toàn. Sẵn sàng tích hợp Deploy (SẴN SÀNG COMMIT).*
