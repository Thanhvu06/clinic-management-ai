# Báo Cáo Kiểm Thử (QA Report) - Phân Hệ Lâm Sàng & Cận Lâm Sàng

**Thời điểm thực hiện:** Final Patch (PR #1 - `fix/ci-billing-hardening`)  
**Target Base:** `feat/doctor-clinical-workspace`  
**Phạm vi:** Backend (.NET 10), Frontend (React 19 + TypeScript), EF Core Migrations, Longitudinal Anthropometrics & Vitals, Diagnostic Catalog & Orders, Technician Workflow, Doctor Review, Patient Diagnostic Results, Consultation Completion Guards.

---

## 1. Kết Quả Kiểm Thử Tự Động Hóa Trong CI (GitHub Actions)

Các kiểm thử dưới đây chạy hoàn toàn tự động trong CI, không phụ thuộc service ngoài:

| Hạng mục | Công cụ | Kết quả | Chi tiết |
|---|---|---|---|
| **Backend Build** | `dotnet build -c Release --no-incremental` | **PASS — 0 errors, 0 warnings** | Sạch cảnh báo trên tất cả 6 projects |
| **Backend Integration Tests** | `dotnet test -c Release` | **149/149 PASSED** | SQLite In-Memory, ~29s, 0 EF Core sentinel warnings |
| **EF Core Model Drift** | `dotnet ef migrations has-pending-model-changes` | **PASS — 0 pending changes** | Migration `20260908072449_RemoveDiagnosticStatusDefaultValues` đồng bộ ModelSnapshot |
| **ML.NET Pipeline** | `ClinicManagement.AI.Training` | **PASS — 100% metrics generated** | Dataset validation sạch, metadata SHA-256 xác định |
| **Frontend Build** | `npm run build` (tsc + Vite) | **PASS — 0 errors** | Bundle dist thành công, typing nghiêm ngặt |
| **Frontend Linter** | `npm run lint` (Oxlint) | **PASS — 82 warnings, 0 errors** | Không phát sinh error, cảnh báo hook cũ được duy trì |
| **Frontend Unit/Component Tests** | `npm test -- --run` (Vitest) | **77/77 PASSED — 13 suites** | 0 failed, 0 skipped |

> **Lưu ý cảnh báo:** Các con số trên phản ánh riêng từng hạng mục:  
> • "0 warnings" ở Backend Build = compiler warnings (.NET/Roslyn).  
> • "82 warnings" ở Frontend Linter = Oxlint lint warnings (không phải compiler errors).  
> • EF Core runtime warnings về `DiagnosticOrder.Status` và `DiagnosticOrderItem.Status` đã được loại bỏ triệt để bằng migration `RemoveDiagnosticStatusDefaultValues`.

---

## 2. Chi Tiết Test Suites Trọng Tâm

### `DiagnosticOrderWorkflowTests` — Đúng 8 [Fact] methods

1. **`Given_DoctorAndPatient_When_FullDiagnosticOrderWorkflowExecuted_Then_TransitionsCorrectly_And_BlocksConsultationUntilReviewed`**  
   Bác sĩ chỉ định → KTV tiếp nhận (`InProgress`) → KTV nhập kết quả từng dịch vụ → KTV hoàn tất (`Completed`) → Bác sĩ đối chiếu và xác nhận → Hoàn tất ca khám → Bệnh nhân tra cứu kết quả.
2. **`Given_PendingOrder_When_DoctorCancels_Then_StatusBecomesCancelled`**  
   Bác sĩ hủy chỉ định ở trạng thái `Ordered`; ca khám được phép hoàn tất bình thường sau khi hủy.
3. **`Given_DiagnosticOrder_When_SerializedToJson_Then_ContractMatchesFrontendExpectations`**  
   JSON payload trả về chứa chính xác `specialtyName`, `category`, `preparationInstructions`; không chứa `orderingDoctorSpecialty` hoặc `serviceCategory`.
4. **`Given_CrossTenantUsers_When_AccessingDiagnosticOrder_Then_IsolationIsEnforcedWith404OrExclusion`**  
   Bác sĩ B không thể xem/hủy/duyệt chỉ định của Bác sĩ A (HTTP 404). Bệnh nhân B không thể xem chỉ định của Bệnh nhân A.
5. **`Given_RoleSecurity_When_UnauthorizedRolesAccessEndpoints_Then_ReturnsForbiddenOrUnauthorized`**  
   Client chưa xác thực → 401; Bệnh nhân/Lễ tân/Dược sĩ truy cập API Kỹ thuật viên → 403; Kỹ thuật viên tạo chỉ định bác sĩ → 403.
6. **`Given_InvalidService_When_CreatingOrder_Then_ValidationRejects_Before_Transaction`**  
   Kiểm chứng rằng validation layer từ chối request trước khi bất kỳ transaction nào được mở; không để lại bản ghi rác.
7. **`Given_TransactionRollback_When_AuditLogSaveFailsAfterOrderInserted_Then_NoDiagnosticDataPersisted`**  
   Atomicity test chuẩn xác: Sử dụng `SaveFailureInterceptor` (EF Core `SaveChangesInterceptor`) inject lỗi có kiểm soát tại lần `SaveChangesAsync` thứ hai (sau khi `DiagnosticOrder` và `DiagnosticOrderItem` đã flush vào transaction nhưng trước khi audit log + notification commit). Test assert interceptor đã kích hoạt (`WasTriggered == true`, `SaveCallCount == 2`) và so sánh đối chiếu toàn bộ số lượng bản ghi DB sau rollback bằng đúng baseline trước request (không dùng assertion lỏng lẻo).
8. **`Given_NonOrderCodeDbUpdateException_When_CreatingOrder_Then_RethrownAndNotMappedToCollision`**  
   Kiểm chứng rằng `DbUpdateException` không liên quan đến unique constraint `OrderCode` (ví dụ lỗi trên bảng khác) KHÔNG bị nuốt hay chuyển thành HTTP 409 `ORDER_CODE_COLLISION`. Request trả về HTTP 500 `INTERNAL_SERVER_ERROR`, transaction rollback sạch sẽ về đúng baseline ban đầu.

### `AiActionAssistantTests` — 12 [Fact] methods
1. **`AiActionTypes_Allowlist_EnforcesKnownActions`**: Kiểm chứng 19 action types được cấp phép nghiêm ngặt; từ chối mọi action lạ hoặc độc hại.
2. **`AiChatResponseDto_SerializesToCamelCase`**: Serialization chuẩn camelCase cho toàn bộ hợp đồng API trả về client.
3. **`Given_UnauthenticatedUser_When_CallingAiChat_Then_Returns401`**: Endpoint yêu cầu xác thực người dùng.
4. **`Given_DoctorUser_When_CallingAiChat_Then_Returns403Forbidden`**: Phân quyền chỉ dành riêng cho vai trò Bệnh nhân (`Patient`).
5. **`Given_EmergencyKeyword_When_CallingAiChat_Then_ReturnsEmergencyWithoutCallingProvider`**: Nhận diện triệu chứng nguy hiểm, trả về `EMERGENCY` + nút gọi 115 ngay lập tức; không gọi LLM/provider bên ngoài.
6. **`Given_PiiInMessage_When_CallingAiChat_Then_PiiIsBlockedAndProviderNotInvoked`**: Phát hiện CCCD, SĐT, Email và từ chối gửi dữ liệu cá nhân ra ngoài; nhắc nhở bệnh nhân bảo mật thông tin.
7. **`Given_PromptInjection_When_CallingAiChat_Then_InjectionBlockedAndProviderNotInvoked`**: Chặn các nỗ lực vượt quyền, yêu cầu system prompt hoặc đổi vai trò bác sĩ.
8. **`Given_GroundedSpecialtyQuery_When_ProviderReturnsValidSpecialty_Then_ReturnsGroundedDataAndActions`**: Neo dữ liệu DB với chuyên khoa thật và sinh các action strongly-typed.
9. **`Given_AlreadyBookedSlot_When_BookingAppointment_Then_Returns409SlotAlreadyBooked`**: Kiểm chứng cơ chế khóa đồng thời ngăn chặn đặt trùng khung giờ (409 Conflict).
10. **`DatasetValidator_DetectsDuplicates_And_ScenarioLeakage`**: Phát hiện câu trùng lặp và ngăn rò rỉ họ kịch bản giữa tập train và test.
11. **`DatasetValidator_TracksUnapprovedRecords`**: Lọc bản ghi chưa duyệt, chỉ cho phép dữ liệu đã được phê duyệt tham gia huấn luyện.
12. **`MlNetSpecialtyClassifier_RejectsDemoModel_WhenClinicallyValidatedRequired`**: Chặn nạp mô hình demo (`clinicallyValidated: false`) khi cờ sản xuất `RequireClinicallyValidated = true`.

### `PatientVitalHistoryTests`
- Kiểm chứng đối chiếu nhân trắc dọc (`AnthropometricComparisonDto`).
- Kiểm chứng tính ΔWeight và ΔBMI giữa 2 lần khám liên tiếp.

### `aiActionAssistant.test.tsx` — 7 tests (Vitest)
1. **`does not render launcher if user is not a Patient`**: Ẩn widget đối với bác sĩ/khách.
2. **`renders launcher button when user is a Patient`**: Nút launcher hiển thị nổi trên màn hình bệnh nhân.
3. **`opens chat window with disclaimer and quick prompts when launcher clicked`**: Mở hộp thoại kèm cảnh báo y khoa và 5 câu hỏi nhanh.
4. **`sends chat request when quick prompt is clicked and renders specialty suggestions`**: Bấm câu hỏi nhanh, hiển thị thẻ chuyên khoa và nút xem chi tiết.
5. **`renders emergency card with 115 call button when urgency is EMERGENCY`**: Hiển thị thẻ cảnh báo đỏ và link gọi 115.
6. **`renders booking summary card and confirms appointment on user click`**: Hiển thị thẻ tóm tắt đặt lịch và xác nhận đặt lịch khám thành công qua API.
7. **`handles 409 slot conflict during booking confirmation gracefully`**: Bắt lỗi 409 khi slot bị trùng và tự động tra cứu lại các slot trống khác.

### `bmiCalculation.test.ts` — 6 tests (Vitest)
- BMI formula: 68.5 kg / (1.72 m)² = 23.2 → nhãn *"Bình thường"* (< 25.0).
- Ngưỡng phân loại: Thiếu cân (< 18.5), Bình thường (< 25.0), Thừa cân / Tiền béo phì (< 30.0), Béo phì (≥ 30.0).

### `diagnosticWorkflow.test.tsx` — 5 tests (Vitest)
- `TechnicianDashboard`: Hàng đợi chỉ định và thẻ KPI (Ordered, InProgress, Completed).
- `DiagnosticOrderPrint`: Phiếu in chuẩn, hiển thị `preparationInstructions`, fallback giới tính "Chưa cập nhật", fallback chuyên khoa "---" hoặc "Chưa cập nhật" (không dùng dữ liệu suy đoán).
- `PatientDiagnosticResults`: Accordion kết quả, nhãn "Đã có kết luận bác sĩ", khoảng tham chiếu.
- API error state handling.

---

## 3. Kiểm Thử Thực Tế Trên Môi Trường Local (Local API Verification)

Các script kiểm thử trong thư mục `scripts/e2e/` giao tiếp HTTP API với backend chạy local:
- [`../scripts/e2e/diagnostic-workflow.mjs`](../scripts/e2e/diagnostic-workflow.mjs): Kiểm thử toàn trình chỉ định và kết quả cận lâm sàng.
- [`../scripts/e2e/ai-action-assistant-workflow.mjs`](../scripts/e2e/ai-action-assistant-workflow.mjs): Kiểm thử toàn trình trợ lý AI, an toàn PII, prompt injection, cấp cứu 115, và luồng đặt lịch qua chat.

> ⚠️ **Hàng rào an toàn nghiêm ngặt:**  
> • Yêu cầu chính xác `process.env.E2E_ALLOW_MUTATION === "true"` (từ chối `false`, `0`, `yes`, rỗng).  
> • Mặc định chỉ cho phép chạy trên hostname cục bộ: `localhost`, `127.0.0.1`, `::1`.  
> • Chạy trên remote staging yêu cầu biến riêng `E2E_ALLOW_REMOTE_STAGING=true`.  
> • Tuyệt đối không chạy trên database dùng để thuyết trình hay production.  

---

## 4. Tổng Kết Chất Lượng & Bằng Chứng Cục Bộ

- **Backend**: 149/149 integration tests passed, 0 compiler errors/warnings, 0 EF Core sentinel warnings.
- **ML.NET Pipeline**: Bộ công cụ `ClinicManagement.AI.Training` kiểm định dataset sạch, tính toán metrics F1/Accuracy xác định, đóng gói metadata SHA-256 hoàn chỉnh.
- **Frontend**: 77/77 Vitest tests passed trên 13 test suites, 0 build errors.
- **An Toàn Dữ Liệu**: 10 bác sĩ, chuyên khoa, lịch làm việc và slot seeded được bảo toàn nguyên vẹn 100%.

