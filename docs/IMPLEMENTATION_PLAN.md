# Kế hoạch triển khai theo trạng thái hệ thống hiện tại

## Mục đích và mốc đối chiếu

Tài liệu này mô tả trạng thái đã xác minh từ code, tài liệu Gate và các lệnh kiểm thử chạy tại HEAD `8925a471cb970d4a63537f27a49dba185a481d5b`. Đây không phải tuyên bố rằng mọi luồng production, chất lượng AI hoặc chất lượng y khoa đã được nghiệm thu.

## Kiến trúc hiện tại

- Backend ASP.NET Core nhắm `net10.0`, tổ chức theo các lớp API, Application, Domain và Infrastructure.
- Frontend dùng React và Vite.
- Backend ứng dụng cấu hình SQL Server. Các integration test và browser acceptance trong lượt này dùng SQLite tạm, không dùng database thật.
- Hệ thống có sáu actor nghiệp vụ: Bệnh nhân, Lễ tân, Bác sĩ, Kỹ thuật viên, Dược sĩ và Admin.

## Luồng khám và vận hành phòng khám

### PatientVisit và hàng đợi

`PatientVisit` là ngữ cảnh trung tâm của một lượt khám, liên kết bệnh nhân, lịch hẹn, cơ sở, khoa/phòng, bác sĩ và các dữ liệu khám liên quan. Dịch vụ lượt khám cấp số thứ tự và sắp xếp hàng đợi theo trạng thái, mức ưu tiên và thời điểm đến.

### Cận lâm sàng

Luồng cận lâm sàng dùng `DiagnosticOrder` và các item của chỉ định, gắn với lượt khám và phạm vi cơ sở/khoa. Trạng thái kết quả và điều kiện review được kiểm tra trước khi lượt khám có thể hoàn tất.

### Thanh toán và cấp thuốc theo item

Điều kiện hoàn tất lượt khám được tổng hợp từ tư vấn, cận lâm sàng, hóa đơn và đơn thuốc. Khả năng cấp thuốc được tính theo từng item và số lượng đã thanh toán; thao tác cấp thuốc kiểm tra thanh toán, tồn kho và cập nhật từng item trong transaction. Vì vậy trạng thái thanh toán hoặc cấp phát một phần không được coi như toàn bộ đơn đã hoàn tất.

## Copilot theo vai trò

### Ranh giới đọc và ghi

- Role planner chỉ được chọn công cụ đọc trong Tool Gateway do server sở hữu và kiểm tra allowlist, actor, scope và capability.
- Gemini không phải nguồn thẩm quyền cho role, facility, resource scope hoặc quyền xác nhận.
- Thao tác ghi đi theo luồng riêng `prepare -> preview -> confirm`; không được thực thi trực tiếp từ quyết định của provider.

### Bảo vệ thao tác chờ xác nhận

Pending action lưu actor, role, session/conversation, snapshot tài nguyên và version, tham số chuẩn hóa, thời hạn, trạng thái lease, audit metadata và dấu vết idempotency/confirmation. Endpoint confirm kiểm tra lại ngữ cảnh phía server; audit và idempotency được duy trì ở lớp backend.

### Degraded mode

Khi Gemini không khả dụng hoặc output không dùng được, hệ thống chuyển sang đường deterministic/manual tùy ngữ cảnh. Tool đọc chỉ được dùng khi server có thể xác định an toàn; nếu không đủ căn cứ, hệ thống yêu cầu làm rõ hoặc hướng dẫn thao tác thủ công thay vì bịa dữ liệu. Cơ chế degraded không được coi là live-provider PASS.

## Trạng thái các Gate AI

| Gate | Trạng thái đã xác minh | Ý nghĩa hiện tại |
| --- | --- | --- |
| Gate B | `NOT_PROMOTED` | Tài liệu quyết định promotion không đạt đủ điều kiện. ML.NET tiếp tục ở `Shadow`/`Off`, không làm nguồn quyết định production. |
| Gate C | Degraded mode đã có trong thiết kế và code | Lỗi provider được cô lập khỏi quyền, scope và xác nhận; hệ thống có đường deterministic/manual. Trạng thái này không chứng minh provider live hoạt động. |
| Gate D | Chưa đạt tiêu chí live Gemini ban đầu | Các live run lịch sử sau bản vá contract vẫn `FAIL`; chi tiết nằm trong `docs/ai/GATE_D_ACCEPTANCE_EVIDENCE.md`. Việc định nghĩa lại tiêu chí đang chờ quyết định của chủ đề tài. |

## Kết quả kiểm thử chạy trong lượt cập nhật này

Tất cả số liệu dưới đây được lấy từ lệnh chạy tại HEAD nêu trên, không sao chép số test lịch sử:

| Phạm vi | Lệnh | Kết quả |
| --- | --- | --- |
| Backend integration, Release | `dotnet test src/backend/ClinicManagement.IntegrationTests/ClinicManagement.IntegrationTests.csproj -c Release --no-build --no-restore --logger "console;verbosity=minimal"` | 819 passed, 0 failed, 0 skipped |
| Frontend unit/component | `npm.cmd run test` trong `src/frontend` | 210 tests passed trong 25 test files; 0 failed |
| Browser E2E | `npm.cmd run e2e` trong `src/frontend` | 29 PASS, 2 NOT_COVERED, 0 FAIL |

Browser E2E dùng SQLite tạm và `FakeGeminiHttpHandler`, nên không gọi Gemini thật. Hai scenario chưa được phủ là `5 write preview/confirm` và `6 unpublished/published diagnostic result`; chúng không được tính là PASS.

## Giới hạn chưa kiểm chứng

- Không có Gemini live attempt trong lượt này; contract structured output thay thế với Google chưa được thử.
- Chưa chạy trên database SQL Server thật hoặc dữ liệu bệnh nhân thật.
- Hai browser scenario nêu trên vẫn `NOT_COVERED`.
- Kết quả test kỹ thuật không chứng minh chất lượng trả lời AI, an toàn lâm sàng hoặc chất lượng y khoa.
- Trạng thái CI của commit tài liệu chỉ được xác nhận sau khi commit được push và các check hoàn tất.

## Hướng phát triển đang được cân nhắc

Chủ đề tài đang cân nhắc một trợ lý không dùng nhà cung cấp LLM ngoài, gồm nút gợi ý theo vai trò, mô hình nhỏ cho câu gõ tự do và tập kiểm tra viết tay. Hướng này **chưa được triển khai**; tài liệu này không coi đó là kiến trúc hiện tại hay cam kết phạm vi mới.
