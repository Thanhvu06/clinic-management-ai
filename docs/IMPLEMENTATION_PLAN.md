# Kế hoạch triển khai theo trạng thái hệ thống hiện tại

## Mục đích và mốc đối chiếu

Lượt cập nhật ngày 01/10/2026 đối chiếu code và chạy kiểm thử trên bản vá xuất phát từ HEAD `d1fb5cc03eb57bc59d000f4ca285ddcadba9057d`, nhánh `feat/ai-local-planner`. Các mục Gate giữ thông tin lịch sử, không có live run mới trong lượt này. Đây không phải tuyên bố nghiệm thu production, chất lượng AI hoặc chất lượng y khoa.

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

`VisitCompletionCoordinator.CanCompleteVisitAsync` và `PatientVisitService` đã được đối chiếu: hoàn tất lượt khám cần phiên khám lâm sàng đã hoàn tất, không còn chỉ định đang chờ/thực hiện hay kết quả chưa review, không còn hóa đơn Unpaid, nghĩa vụ phí khám/cận lâm sàng đã thanh toán và số lượng thuốc được thanh toán đủ. Đơn Issued/ReservedForPurchase có item chưa cấp cũng chặn hoàn tất. `PharmacyService.DispensePrescriptionAsync` kiểm tra thanh toán đủ từng item và tồn kho, ghi giao dịch kho trong transaction; trường hợp đã giữ chỗ không trừ kho lần nữa. Không suy diễn thanh toán một phần thành hoàn tất toàn đơn.

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

Số liệu dưới đây lấy từ lệnh thực chạy trên bản vá trong lượt cập nhật này, không sao chép số test lịch sử. `git diff --check` sạch; không tạo migration:

| Phạm vi | Lệnh | Kết quả |
| --- | --- | --- |
| Backend suggestion buttons, Debug | `dotnet test src/backend/ClinicManagement.IntegrationTests/ClinicManagement.IntegrationTests.csproj -c Debug --no-build --no-restore --filter FullyQualifiedName~AiSuggestionButtonTests` | 15 passed, 0 failed, 0 skipped |
| Backend build, Debug | `dotnet build src/backend/ClinicManagement.sln --no-restore -c Debug` | Thành công; lượt cuối 0 lỗi, 11 cảnh báo có sẵn |
| Backend build, Release | `dotnet build src/backend/ClinicManagement.sln --no-restore -c Release` | Thành công; lượt tăng dần cuối 0 lỗi, 0 cảnh báo (lượt trước có 15 cảnh báo) |
| Full backend suite, Release | `dotnet test src/backend/ClinicManagement.sln -c Release --no-build --no-restore --logger "console;verbosity=minimal"` | 882 passed, 0 failed, 0 skipped |
| EF model drift | `dotnet ef migrations has-pending-model-changes --project src/backend/ClinicManagement.Infrastructure --startup-project src/backend/ClinicManagement.Api --configuration Release --no-build` | Không có thay đổi model so với migration cuối |
| Frontend unit/component | `npm.cmd run test` trong `src/frontend` | 235 tests passed trong 27 test files; 0 failed |
| Frontend lint | `npm.cmd run lint` trong `src/frontend` | Thành công, 0 lỗi; đếm 132 dòng cảnh báo trong output |
| Frontend build | `npm.cmd run build` trong `src/frontend` | Thành công; Vite cảnh báo chunk lớn hơn 500 kB |
| Browser E2E đầy đủ | `npm.cmd run e2e` trong `src/frontend` | 35 PASS, 2 NOT_COVERED, 0 FAIL |

Ba scenario mới trong lượt này (`receptionist suggestion chips`, `pharmacist suggestion chips`, `admin suggestion chips`) đều PASS: menu chỉ có nút của vai trò, mỗi nút không cần tài nguyên chạy công cụ đọc thật và trả thẻ dữ liệu, không gọi provider. Các scenario đã có cũng được chạy lại và PASS: `patient suggestion chips` (menu đúng vai trò, nút lịch hẹn trả thẻ dữ liệu), `doctor suggestion chips` (thẻ hàng đợi, nút theo ca chỉ có trên lịch hẹn được phân công), `patient booking wizard` (toàn bộ chuỗi chọn và xác nhận qua nút hiện có, bỏ Chủ nhật, kiểm DOM không lộ ID/token wizard và SQLite có đúng một lịch hẹn mới). Phép kiểm ID phân biệt tọa độ SVG/thuộc tính bố cục như `rows` với định danh nghiệp vụ; không ghi giá trị ID/token hay DOM vào báo cáo.

Browser E2E dùng SQLite tạm và `FakeGeminiHttpHandler`, không gọi Gemini thật. Sáu scenario suggestion/wizard cục bộ chạy với `AiProvider:IsEnabled=false`, không chặn/giả lập response của suggestion hoặc wizard. Lượt này dùng nguyên seed synthetic và canary đã có, không sửa canary. Xác nhận wizard dùng endpoint đặt lịch hiện có; số lịch hẹn được kiểm tra bằng truy vấn chỉ đọc trực tiếp trên SQLite cô lập. Scenario `5 write preview/confirm` dành cho role-action tổng quát và `6 unpublished/published diagnostic result` vẫn chưa có fixture xác nhận/publish an toàn; không tính chúng là PASS.

## Giới hạn chưa kiểm chứng

- Không có Gemini live attempt trong lượt này; contract structured output thay thế với Google chưa được thử.
- Chưa chạy trên database SQL Server thật hoặc dữ liệu bệnh nhân thật.
- Hai browser scenario nêu trên vẫn `NOT_COVERED`.
- Kết quả test kỹ thuật không chứng minh chất lượng trả lời AI, an toàn lâm sàng hoặc chất lượng y khoa.
- Chưa xem giao diện thật bằng mắt; browser acceptance chạy headless.

## Nút gợi ý cục bộ cho sáu vai trò

`AiSuggestionCatalog` do server sở hữu mã và nhãn; client gửi mã lựa chọn. Server tìm mã theo vai trò, ánh xạ sang deterministic planner và công cụ đọc hiện có. Đường resolver/Tool Gateway tiếp tục kiểm quyền, sở hữu và facility scope; suggestion không gọi provider. Menu tối đa sáu nút.

| Vai trò | Mã nút | Nhãn | Nhóm | Công cụ đọc / hành động | Loại tài nguyên |
| --- | --- | --- | --- | --- | --- |
| Patient | `patient.start_booking` | Đặt lịch khám | Đặt lịch | Wizard start, không công cụ | None |
| Doctor | `doctor.my_queue` | Hôm nay tôi khám ai? | Việc hôm nay | `doctor.get_my_queue` | None |
| Doctor | `doctor.patient_summary` | Tóm tắt bệnh nhân đang mở | Theo ca đang mở | `doctor.get_patient_summary` | DoctorCase |
| Doctor | `doctor.diagnostic_orders` | Chỉ định cận lâm sàng của ca này | Theo ca đang mở | `doctor.get_diagnostic_orders` | DoctorCase |
| Doctor | `doctor.prescription_status` | Trạng thái đơn thuốc của ca này | Theo ca đang mở | `doctor.get_prescription_status` | DoctorCase |
| Patient | `patient.my_appointments` | Lịch hẹn của tôi | Dữ liệu của tôi | `patient.get_my_appointments` | None |
| Patient | `patient.my_visits` | Lượt khám của tôi | Dữ liệu của tôi | `patient.get_my_visits` | None |
| Patient | `patient.my_diagnostic_results` | Kết quả cận lâm sàng của tôi | Dữ liệu của tôi | `patient.get_my_diagnostic_results` | None |
| Patient | `patient.my_prescriptions` | Đơn thuốc của tôi | Dữ liệu của tôi | `patient.get_my_prescriptions` | None |
| Patient | `patient.my_bills` | Hóa đơn của tôi | Dữ liệu của tôi | `patient.get_my_bills` | None |
| Receptionist | `receptionist.today_appointments` | Lịch hẹn hôm nay | Việc hôm nay | `reception.get_today_appointments` | None |
| Receptionist | `receptionist.queue` | Hàng đợi tiếp nhận | Việc hôm nay | `reception.get_queue` | None |
| DiagnosticTechnician | `technician.worklist` | Chỉ định cần thực hiện | Việc hôm nay | `technician.get_worklist` | None |
| Pharmacist | `pharmacist.prescription_queue` | Đơn thuốc chờ xử lý | Việc hôm nay | `pharmacist.get_prescription_queue` | None |
| Pharmacist | `pharmacist.inventory` | Tồn kho thuốc | Tổng quan | `pharmacist.get_inventory_status` | None |
| Pharmacist | `pharmacist.prescription_payment` | Thanh toán của đơn đang mở | Theo đơn đang mở | `pharmacist.get_prescription_payment_status` | Prescription |
| Admin | `admin.dashboard_metrics` | Chỉ số hôm nay | Tổng quan | `admin.get_dashboard_metrics` | None |
| Admin | `admin.ai_health` | Hoạt động của trợ lý AI | Tổng quan | `admin.get_ai_health` | None |

Ba nút theo ca của Doctor chỉ hiện khi trang gửi `appointmentId` hoặc `visitId` được resolver xác minh quyền. Context chỉ định/đơn thuốc không tự suy ra appointment/visit cho menu; resource nhớ từ lượt trước cũng không đủ để thực thi suggestion theo ca ở lượt mới. Response bỏ nút có tool vừa chạy khỏi gợi ý tiếp theo. Dược sĩ chỉ thấy/chạy `pharmacist.prescription_payment` khi lượt hiện tại gửi `resourceContext.prescriptionId` và resolver xác minh quyền/cơ sở; bộ nhớ lượt trước và ID giả trong arguments không đủ. Planner không cung cấp ID cho nút này, binding hiện có gắn tham số từ context đã xác minh.

Không tạo nút cho `reception.lookup_appointment` vì cần mã lịch hẹn do người dùng cung cấp, hoặc `clinic.search_knowledge` vì cần câu truy vấn. Admin hiện chỉ có hai công cụ đọc tổng hợp, chưa có công cụ doanh thu/nhập hàng.

Giao diện dùng một dải gợi ý gắn với câu trả lời trợ lý mới nhất: trạng thái rỗng là lưới hai cột có biểu tượng, tiêu đề nhóm chỉ hiện khi có ít nhất hai nhóm; sau trả lời là chip có chiều cao giới hạn/cuộn ngang trên màn hình hẹp. Tin nhắn flex 1, cuộn riêng; dải gợi ý và ô nhập ở phần dưới cố định của panel. Nút câu hỏi cũ chỉ làm dự phòng khi không có gợi ý server; không hiện prompt theo ca khi chưa chọn ca. MedicalChatWidget và PatientAiConsultation cũng dùng một dải và thẻ dữ liệu trả về.

Đã bỏ dải tên công cụ; khay Thao tác có xác nhận (n) gập sẵn bằng details, tự mở khi có preview/phản hồi thao tác. Nhãn thao tác tiếng Việt; prepare/preview/confirm/cancel không đổi. Clarification trùng nội dung chính được gộp theo khoảng trắng/hoa thường; mã lỗi chỉ hiện ở DEV. Trạng thái dùng nhãn tiếng Việt, chi tiết mode nằm trong title. Clarifying/ManualHandoff dùng menu vai trò với hướng dẫn chọn nút. Focus trap có summary và bỏ nút trong khay đang gập.

Wizard dành riêng cho Patient đi qua chuyên khoa → bác sĩ (hoặc bác sĩ bất kỳ) → ngày → giờ → lý do có sẵn/nhập tay → xem lại. Token được Data Protection bảo vệ, ràng buộc user/session/operation, có hạn 15 phút; nút lựa chọn mang token opaque. Ngày lấy từ lịch trống trong 14 ngày tới và bỏ Chủ nhật. Bước xem lại dùng `AiBookingReviewIssuer` hiện có để tạo snapshot và mã xác nhận; nút review rồi `ConfirmBooking` đi qua luồng đặt lịch hiện có, giữ kiểm tra và idempotency.

Trước xác nhận, wizard chỉ có thể ghi `AiSelectionSnapshots`, `AiBookingConfirmations`, `AiSessions`, `AiAuditLogs`; chưa tạo Appointment. Test wizard kiểm fingerprint các bảng trước/sau và kiểm xác nhận lặp không tạo lịch thứ hai. Bản vá L-A trước đó đã thêm giới hạn DTO 2000 ký tự: vượt giới hạn bị `[ApiController]` trả HTTP 400 không phản chiếu lý do. Đầu vào 501–2000 vẫn qua screening cấp cứu/injection/PII trước `INVALID_REASON`. Ba regex PII dùng timeout 100 ms, timeout được coi là PII và đi tới `PII_BLOCKED`, không log input/exception. Không đổi pattern ở đầu vào bình thường.

Test L-A hiện có kiểm Reason 2001 ký tự trả 400 không phản chiếu và không ghi wizard; 2000 ký tự `a` trả `INVALID_REASON`, đầu vào dài chứa dấu hiệu cấp cứu/injection/PII vẫn bị chặn trước lỗi độ dài (ngưỡng thời gian rộng 10 giây). `ContainsPii` với 200.000 ký tự `a` trả `false` trong ngân sách 5 giây; runtime có thể bỏ qua nhanh chuỗi không chứa email, nên test này không coi mọi chuỗi dài đều phải timeout. Nhánh catch timeout fail-closed được giữ trong code; không có bằng chứng test timeout thực tế ổn định trong test hiện có.

Ứng dụng hiện gọi `AddDataProtection()`; chưa cấu hình lưu/chia sẻ key ring chung. Token đang dở có thể mất hiệu lực khi restart làm mất/đổi key ring hoặc khi instance khác không có cùng khóa. Chưa kiểm chứng restart hay nhiều instance trong môi trường triển khai thật; không khẳng định mọi restart đều làm mất token. Harness synthetic dùng khóa ephemeral.

## Hướng phát triển đang được cân nhắc

Mô hình nhỏ hiểu câu gõ tự do không dùng LLM và tập kiểm tra viết tay cho hướng này chưa được triển khai. Đường suggestion và wizard đã có như mô tả trên; kết quả kiểm thử kỹ thuật không phải đánh giá chất lượng AI hay y khoa. Việc cấu hình khóa dùng chung và phạm vi mô hình câu gõ tự do cần chủ đề tài quyết định trước khi triển khai tiếp.
