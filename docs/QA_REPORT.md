# Báo Cáo Kiểm Thử (QA Report) - Phân Hệ ClinicCare AI Action Assistant & Workflow Khép Kín

**Thời điểm thực hiện:** Production Hardening & Parity Audit (PR #1 - `fix/ci-billing-hardening`)  
**Target Base:** `feat/doctor-clinical-workspace` (PR #1 giữ nguyên trạng thái OPEN, không merge)  
**Phạm vi:** Backend (.NET 10), Frontend (React 19 + TypeScript), ML.NET Training Pipeline, Canonical Specialty Mapping (`SP01`-`SP11`), AI Action Assistant 19-Action Registry, Sunday Clinic Rule, Emergency Negation Handling, Booking Idempotency, 409 Conflict Recovery, Longitudinal Vitals & Diagnostic Workflow.

---

## 1. Bảng Tổng Hợp Quality Gates Tự Động Hóa

| Hạng mục | Lệnh thực thi | Kết quả | Chi tiết kiểm chứng |
|---|---|---|---|
| **Backend Build** | `dotnet build -c Release --no-incremental` | **PASS — 0 errors, 0 warnings** | Toàn bộ 6 projects trong solution sạch hoàn toàn cảnh báo |
| **Backend Integration Tests** | `dotnet test -c Release` | **154/154 PASSED** | SQLite In-Memory, ~33s, 154 tests chạy sạch, 0 failed, 0 skipped |
| **Stress Test AI Assistant** | 10 lần chạy liên tiếp `AiActionAssistantTests` | **170/170 PASSED (100%)** | 17 tests × 10 runs = 170 passed, 0 flakes, tính ổn định tuyệt đối |
| **EF Core Model Drift** | `dotnet ef migrations has-pending-model-changes` | **PASS — 0 pending changes** | ModelSnapshot đồng bộ tuyệt đối với DbContext |
| **ML.NET Training Pipeline** | `dotnet run --project ...AI.Training.csproj` | **PASS — Deterministic** | Validate: 18 records (18 approved), 0 duplicate, 0 leakage; Honest metrics: Micro Acc 22.22%, Top-3 Acc 66.67%, ClinicallyValidated: false |
| **Frontend Linter** | `npm run lint` (Oxlint) | **PASS — 0 errors** | 80 warnings cũ của hook, 0 errors mới |
| **Frontend Build** | `npm run build` (tsc -b && Vite) | **PASS — 0 errors** | TypeScript biên dịch nghiêm ngặt, Vite bundle thành công |
| **Frontend Unit/Component Tests** | `npm test -- --run` (Vitest) | **81/81 PASSED — 13 suites** | 81 tests pass sạch sẽ, 0 failed, 0 skipped |
| **Local API E2E Verification** | `scripts/e2e/ai-action-assistant-workflow.mjs` | **PASS — 100% assertions** | Hàng rào `E2E_ALLOW_MUTATION=true`, kiểm tra auth, emergency, PII, injection, Sunday rule, safe routes |

---

## 2. Chi Tiết Test Suite Trọng Tâm

### 2.1. `AiActionAssistantTests` — Đầy đủ 17 [Fact] methods (.NET Integration Tests)
1. **`AiActionTypes_Allowlist_EnforcesKnownActions`**: Kiểm tra 19 action types trong allowlist; từ chối mọi action lạ hoặc có nguy cơ bảo mật.
2. **`AiChatResponseDto_SerializesToCamelCase`**: Kiểm chứng contract JSON trả về serialize chuẩn camelCase (`specialtySuggestions`, `bookingDraft`, `actions`).
3. **`Given_UnauthenticatedUser_When_CallingAiChat_Then_Returns401`**: Yêu cầu xác thực bắt buộc đối với endpoint AI chat.
4. **`Given_DoctorUser_When_CallingAiChat_Then_Returns403Forbidden`**: Phân quyền nghiêm ngặt chỉ cấp phép vai trò `Patient`.
5. **`Given_EmergencyKeyword_When_CallingAiChat_Then_ReturnsEmergencyWithoutCallingProvider`**: Phát hiện Red Flags cấp cứu, trả về `EMERGENCY` + nút gọi `tel:115` ngay lập tức, không gọi LLM bên ngoài.
6. **`Given_PiiInMessage_When_CallingAiChat_Then_PiiIsBlockedAndProviderNotInvoked`**: Chặn dữ liệu định danh (CCCD, SĐT, Email), bảo vệ dữ liệu nhạy cảm của bệnh nhân.
7. **`Given_PromptInjection_When_CallingAiChat_Then_InjectionBlockedAndProviderNotInvoked`**: Vô hiệu hóa prompt injection (bỏ qua quy tắc, đóng vai bác sĩ, xuất system prompt).
8. **`Given_GroundedSpecialtyQuery_When_ProviderReturnsValidSpecialty_Then_ReturnsGroundedDataAndActions`**: Neo dữ liệu chuyên khoa chuẩn tắc (`SP06` Tim mạch) từ DB và sinh actions strongly-typed.
9. **`Given_AlreadyBookedSlot_When_BookingAppointment_Then_Returns409SlotAlreadyBooked`**: Kiểm chứng cơ chế bắt xung đột đồng thời khi 2 bệnh nhân khác nhau cố đặt cùng một slot.
10. **`DatasetValidator_DetectsDuplicates_And_ScenarioLeakage`**: Bắt lỗi trùng lặp text và lỗi rò rỉ kịch bản giữa train/test dưới dạng lỗi nghiêm trọng (`ERROR`).
11. **`DatasetValidator_TracksUnapprovedRecords`**: Cách ly và cảnh báo các bản ghi chưa duyệt (`approved: false`).
12. **`MlNetSpecialtyClassifier_RejectsDemoModel_WhenClinicallyValidatedRequired`**: Từ chối nạp mô hình demo (`clinicallyValidated = false`) khi cờ sản xuất `RequireClinicallyValidated = true`.
13. **`Given_SundayDateRequested_When_CallingAiChat_Then_InformsClosedAndSuggestsMondayAction`**: Khi yêu cầu Chủ nhật, giải thích phòng khám đóng cửa vào Chủ nhật, không tự ý chuyển ngày, trả về action `ChangePreferredDate` cho Thứ Hai để người dùng chủ động chọn.
14. **`Given_SamePatientBooksTwice_When_CallingCreateAppointment_Then_ReturnsIdempotentSuccess`**: Cùng một bệnh nhân bấm xác nhận đặt cùng một slot hai lần được trả về thành công an toàn (idempotent, 200/201) với mã hẹn ban đầu thay vì lỗi 409.
15. **`Given_NegatedEmergencySymptom_When_CallingAiChat_Then_DoesNotTriggerEmergency`**: Kiểm tra ngữ cảnh phủ định (*"tôi không khó thở và không đau ngực"*), không kích hoạt cấp cứu sai, phân loại đúng mức độ `ROUTINE`.
16. **`Given_PhoneNumbers_When_CallingAiChat_Then_BlockedByPiiFilter`**: Kiểm tra Regex SĐT hỗ trợ cả định dạng `0900000003` và `+84900000003`, không chặn nhầm thông tin thường (*"35 tuổi, ho 3 ngày"*).
17. **`AiActionValidator_EnforcesSafeRoutes_And_ViewBillsUsesInvoices`**: Kiểm tra `SafeRoutes` nghiêm ngặt; kiểm chứng action `ViewBills` bắt buộc trỏ tới `/patient/invoices`, từ chối các route không hợp lệ như `/contact`.

---

### 2.2. `aiActionAssistant.test.tsx` — Đầy đủ 11 Component Tests (Vitest)
1. **`does not render launcher if user is not a Patient`**: Ẩn floating launcher đối với tài khoản không phải Patient.
2. **`renders launcher button when user is a Patient`**: Hiển thị launcher tròn nổi bật ở góc màn hình bệnh nhân.
3. **`opens chat window with disclaimer and quick prompts when launcher clicked`**: Mở popup chat kèm lời cảnh báo y khoa và 5 câu hỏi nhanh.
4. **`sends chat request when quick prompt is clicked and renders specialty suggestions`**: Bấm câu hỏi nhanh, gọi API và hiển thị thẻ chuyên khoa gợi ý.
5. **`renders emergency card with 115 call button when urgency is EMERGENCY`**: Hiển thị thẻ cấp cứu đỏ nổi bật kèm liên kết gọi điện `tel:115`.
6. **`renders booking summary card and confirms appointment on user click`**: Hiển thị thẻ tóm tắt đặt lịch và xác nhận đặt lịch khám thành công.
7. **`handles 409 slot conflict during booking confirmation gracefully`**: Bắt lỗi xung đột slot, bảo tồn triệu chứng gốc và hiển thị hướng dẫn chọn lại.
8. **`formats Vietnamese date correctly`**: Kiểm chứng hàm `formatVietnameseDate` định dạng ngày dạng `dd/MM/yyyy` (ví dụ `15/09/2026`).
9. **`navigates to /patient/invoices when ViewBills action is clicked`**: Kiểm chứng nút xem hóa đơn điều hướng chính xác tới route canonical `/patient/invoices`.
10. **`displays reception hotline and desk info without external navigation when ContactReception is clicked`**: Hiển thị thông tin quầy lễ tân ngay trong khung chat, không điều hướng sang route `/contact` không tồn tại.
11. **`renders ReviewBooking summary and allows confirmation from review`**: Hiển thị thẻ xem lại thông tin lịch hẹn và cung cấp nút bấm xác nhận chính thức.

---

### 2.3. Báo Cáo Đo Lường Mô Hình ML.NET (Honest Metrics)
- **Tập dữ liệu huấn luyện**: 18 bản ghi mô phỏng sạch sẽ (`SIMULATED_TEST_DATA`), đã loại bỏ 2 bản ghi Nha khoa không thuộc danh mục phòng khám (`CASE-017`, `CASE-018`).
- **Phân tập**: 9 ca huấn luyện, 9 ca kiểm thử độc lập (chia theo họ kịch bản, fixed seed 42).
- **Kết quả đánh giá trên tập Test**:
  - **Micro Accuracy**: **22.22%**
  - **Macro Accuracy**: **18.75%**
  - **Macro Precision**: **16.67%**
  - **Macro Recall**: **18.75%**
  - **Macro F1**: **17.50%**
  - **Top-3 Accuracy**: **66.67%**
  - **Log Loss**: **2.0171**
  - **Cờ ClinicallyValidated**: **`false`** (Bắt buộc từ chối triển khai lâm sàng theo đúng quy định an toàn y tế).
