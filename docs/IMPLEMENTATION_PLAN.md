# Kế hoạch triển khai theo trạng thái hệ thống hiện tại

## Mục đích và mốc đối chiếu

Lượt cập nhật bố cục ngày 01/10/2026 đối chiếu code và chạy kiểm thử trên bản vá xuất phát từ HEAD `0942604f546d84186f6c3b755957f3fc0aa8072b`, nhánh `feat/ai-local-planner`. Các mục Gate giữ thông tin lịch sử, không có live run mới trong lượt này. Đây không phải tuyên bố nghiệm thu production, chất lượng AI hoặc chất lượng y khoa.

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

## Kết quả kiểm thử trước lượt thiết kế lại bố cục

Số liệu dưới đây là bằng chứng của lượt suggestion trước đó, xuất phát từ `d1fb5cc03eb57bc59d000f4ca285ddcadba9057d`. Kết quả của lượt bố cục hiện tại được ghi riêng bên dưới; không tạo migration:

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
- Giao diện hiện tại đã được xem qua 30 ảnh Chromium headless ở hai viewport; chưa kiểm tra trình duyệt khác hoặc bàn phím ảo trên thiết bị thật.

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

Giao diện dùng một dải gợi ý trong vùng soạn tin: trạng thái rỗng là lưới hai cột có biểu tượng và nhóm; sau trả lời là tối đa sáu chip, xuống dòng trong 96 px, chuyển sang một hàng cuộn ngang có mép mờ nếu vượt chiều cao. Không đặt dải trong bong bóng. Tin nhắn flex 1 là vùng cuộn dọc duy nhất; tiêu đề, dải gợi ý và ô nhập cố định. MedicalChatWidget, PatientAiConsultation và UnifiedCopilotPanel dùng chung token teal, khoảng cách 4/8/12/16/24 px, bo góc 12 px (chip 999 px), chữ nội dung 14 px, mục tiêu nút 40 px/44 px trên màn hình hẹp. Desktop dùng giới hạn khoảng 640 px/80dvh, mobile widget là sheet 100dvh. Bong bóng tối đa 85%; thẻ dữ liệu không thêm lớp viền/đổ bóng và bỏ mô tả trùng văn bản chính. Nút disabled giữ chữ đậm, opacity 1, con trỏ not-allowed.

Đã bỏ dải tên công cụ; khay Thao tác có xác nhận (n) gập sẵn bằng details trong vùng tin nhắn ngay trước vùng soạn tin, tự mở khi có pendingAction/preview/phản hồi. Mở khay không làm vùng soạn tin ép nhỏ phần tin nhắn. Nhãn thao tác tiếng Việt; prepare/preview/confirm/cancel không đổi. Clarification trùng nội dung chính được gộp theo khoảng trắng/hoa thường; mã lỗi chỉ hiện ở DEV. Chi tiết mode, nguồn và trạng thái wire có trong title; giao diện thường dùng nhãn tiếng Việt. Clarifying/ManualHandoff dùng menu vai trò với hướng dẫn chọn nút. Focus trap có summary và bỏ nút trong khay đang gập.

Wizard dành riêng cho Patient đi qua chuyên khoa → bác sĩ (hoặc bác sĩ bất kỳ) → ngày → giờ → lý do có sẵn/nhập tay → xem lại. Token được Data Protection bảo vệ, ràng buộc user/session/operation, có hạn 15 phút; nút lựa chọn mang token opaque. Ngày lấy từ lịch trống trong 14 ngày tới và bỏ Chủ nhật. Bước xem lại dùng `AiBookingReviewIssuer` hiện có để tạo snapshot và mã xác nhận; nút review rồi `ConfirmBooking` đi qua luồng đặt lịch hiện có, giữ kiểm tra và idempotency.

Wizard chỉ có một thẻ bước: Bước n/6, chuỗi nhãn trên desktop/thanh tiến trình trên mobile, mỗi bước đã qua một dòng tóm tắt có dấu ✓. Chuyên khoa/bác sĩ hai cột; ngày chip; giờ ba cột; Quay lại ở đầu, Bắt đầu lại là liên kết phụ cuối thẻ. Bước mới cuộn đầu thẻ vào vùng nhìn và focus tiêu đề. Thông điệp/thẻ tóm tắt hiện có được gộp vào bước xem lại, giữ nguyên callback và xác nhận. Lý do vẫn 10–500 ký tự, textarea tự tăng chiều cao, không có cuộn dọc bên trong. Nút chọn bác sĩ/giờ của chat cũ dùng cùng kiểu, giờ ba cột và sáu lựa chọn đầu cùng Xem thêm/Thu gọn; lựa chọn ở tin cũ gập thành một dòng. Phản hồi cũ thiếu bác sĩ/giờ/lý do ưu tiên nút Đặt lịch khám hiện có ở đầu dải. Không đổi payload đặt lịch hay cách ký token.

## Trạng thái giao diện và tương phản đã đo

| Giá trị/điều kiện | Nhãn | Màu |
| --- | --- | --- |
| NotCalled, Disabled, NotConfigured | Chế độ nội bộ | Xám xanh |
| Online | Trực tuyến | Xanh |
| Degraded, Unavailable, Timeout, RateLimited, 429 | Đang dùng chế độ nội bộ | Hổ phách |
| SafetyBlocked | Đã chặn vì an toàn | Hổ phách |
| Lỗi kết nối/máy chủ không phục hồi của yêu cầu hiện tại | Không thể kết nối. Vui lòng thử lại. | Đỏ |

Không dùng trạng thái sức khỏe cũ để tô đỏ một lượt xử lý nội bộ mới. Tương thích providerStatus cũ chỉ ánh xạ cách hiển thị; wire hiện có giữ nguyên. Chi tiết assistantMode/plannerMode/mã lỗi nằm trong tooltip, chỉ hiển thị trực tiếp ở DEV.

Tính theo độ sáng tương đối sRGB: chữ #134e4a/nền chip #f0fdfa = 9.0851:1; chữ #134e4a/nền disabled #ccfbf1 = 8.4087:1; trắng/#0f766e = 5.4733:1; trắng/#134e4a = 9.4751:1; #0f766e/trắng = 5.4733:1; trắng/#b91c1c = 6.4700:1. E2E lấy màu computed của từng nút đang hiện, tổng hợp nền alpha của ancestor và kiểm >=4.5:1; giá trị nhỏ nhất thực đo trên cả hai viewport là 5.4733:1.

## Trợ giúp cục bộ và câu đồng nghĩa

Planner dùng intent `AiChatIntentTypes.Help` đã có, sub-intent RoleHelp, Deterministic, RequiresProvider=false và 0 công cụ. Tám cụm: bạn làm được gì; giúp gì; hướng dẫn; menu; trợ giúp; tôi có quyền hạn gì; tôi làm được gì; cách dùng. Chuẩn hóa bằng AiTextNormalizer, không phân biệt dấu/hoa thường. Thông điệp lấy đúng nhãn AiSuggestionCatalog.ForRole của vai trò; các mục theo ca/đơn luôn nhắc điều kiện mở tài nguyên đúng quyền. Không thêm intent, nút hoặc công cụ.

35 câu đồng nghĩa cho 17 nút đọc, tối đa ba câu/nút; so khớp chính xác sau chuẩn hóa và dùng PlanSuggestion cùng resolver/quyền hiện có:

| Vai trò | Mã nút | Câu đồng nghĩa |
| --- | --- | --- |
| Patient | patient.my_appointments | lịch hẹn của mình; lịch của tôi |
| Patient | patient.my_visits | các lượt khám của tôi; lịch sử khám của mình |
| Patient | patient.my_diagnostic_results | kết quả xét nghiệm của mình; xem kết quả của tôi |
| Patient | patient.my_prescriptions | toa thuốc của mình; xem đơn thuốc của tôi |
| Patient | patient.my_bills | biên lai của tôi; hóa đơn của mình |
| Doctor | doctor.my_queue | hôm nay khám ai; hàng đợi; danh sách chờ khám |
| Doctor | doctor.patient_summary | tóm lược ca đang mở; tóm tắt bệnh nhân hiện tại |
| Doctor | doctor.diagnostic_orders | xem chỉ định của ca đang mở; cận lâm sàng của ca hiện tại |
| Doctor | doctor.prescription_status | đơn thuốc của ca đang mở; xem trạng thái toa thuốc của ca |
| Receptionist | receptionist.today_appointments | các lịch hẹn trong ngày; lịch tiếp nhận hôm nay |
| Receptionist | receptionist.queue | danh sách chờ tiếp nhận; xem hàng đợi lễ tân |
| DiagnosticTechnician | technician.worklist | các chỉ định cần làm; danh sách phiếu đang chờ |
| Pharmacist | pharmacist.prescription_queue | các đơn chờ cấp thuốc; danh sách toa chờ xử lý |
| Pharmacist | pharmacist.inventory | kiểm tra kho thuốc; thuốc còn trong kho |
| Pharmacist | pharmacist.prescription_payment | đơn đang mở đã thanh toán chưa; đối chiếu thanh toán toa hiện tại |
| Admin | admin.dashboard_metrics | số liệu hôm nay; báo cáo tổng hợp trong ngày |
| Admin | admin.ai_health | kiểm tra hoạt động trợ lý; trạng thái trợ lý nội bộ |

Frontend bệnh nhân chỉ chuyển các cụm trợ giúp/đồng nghĩa đọc chính xác sang endpoint role-copilot sẵn có, không gửi suggestionCode cho câu gõ. Các câu đặt lịch/lâm sàng cũ giữ đường gửi và callback. Các nút Doctor theo ca và Pharmacist theo đơn vẫn cần resourceContext được xác minh; câu ngoài vai trò không kích hoạt công cụ của vai trò khác.

## Ảnh kiểm tra bố cục

Ảnh nằm trong `TestResults/ui-screenshots/`, đã kiểm git check-ignore trước capture và không stage. Chromium desktop 1280×800/mobile 390×844, mỗi viewport 15 ảnh: patient-empty, patient-appointments, patient-wizard-specialty, patient-wizard-doctor, patient-wizard-day, patient-wizard-slot, patient-wizard-reason, patient-wizard-reason-input, patient-wizard-review, patient-wizard-review-summary, patient-help, doctor-empty, doctor-queue, doctor-actions-open, receptionist-empty; tên file có tiền tố desktop-/mobile- và đuôi .png.

Đã mở và xem cả 30 ảnh bằng công cụ đọc ảnh, sửa textarea cuộn lồng, cuộn mượt gây ảnh chụp chuyển tiếp/cắt vùng nhập, thẻ review lặp và nhiều lớp viền, clear chat còn wizard, trạng thái dữ liệu tiếng Anh, góc chụp khay mở/đáy thẻ dài. E2E kiểm chỉ vùng tin nhắn cuộn dọc, không scroller trong bong bóng/thẻ, đúng một dải ở composer, mọi nút >=40/44 px, không tràn ngang, một bước wizard cùng n−1 dòng tóm tắt, ba cột giờ, trạng thái tắt trung tính và không lộ mã lỗi/tên tool thô. Các ảnh đầu thẻ và cuối thẻ dài là hai vị trí cuộn của cùng vùng tin nhắn.

Test frontend mới kiểm màu/nhãn status, help và alias chỉ gửi endpoint đọc, sáu lựa chọn giờ cùng Xem thêm và callback cũ, gập lựa chọn lịch sử, ưu tiên wizard, progress/tóm tắt/focus, dịch trạng thái wire với raw value trong title. Test cũ cập nhật vị trí composer thay cho nút trong bong bóng, thông điệp trạng thái của lượt hiện tại thay cho cờ sức khỏe cũ, nguồn/trạng thái/expired tiếng Việt. Test hủy vẫn kiểm không ghi/không gọi request mới, wizard vẫn kiểm token opaque và endpoint confirm hiện có; không bỏ tiêu chí quyền, scope, hủy hoặc xác nhận.

## Kết quả chạy của lượt bố cục hiện tại

| Phạm vi | Lệnh | Kết quả thực chạy |
| --- | --- | --- |
| Debug build | `dotnet build src/backend/ClinicManagement.sln --no-restore -c Debug -p:OutputPath=bin/CodexDebug/` | 0 lỗi, 15 cảnh báo |
| Release build | `dotnet build src/backend/ClinicManagement.sln --no-restore -c Release` | 0 lỗi, 11 cảnh báo ở lượt ghi log |
| Full backend Release | `dotnet test src/backend/ClinicManagement.sln -c Release --no-build --no-restore --logger "trx;LogFileName=full-backend-layout.trx" --results-directory TestResults` | 911 passed, 0 failed, 0 skipped |
| EF model | `dotnet ef migrations has-pending-model-changes --project src/backend/ClinicManagement.Infrastructure --startup-project src/backend/ClinicManagement.Api --configuration Release --no-build` | No changes have been made to the model since the last migration |
| Frontend lint | `npm.cmd run lint` | Thành công, 0 lỗi; 135 dòng warning |
| Frontend tests | `npm.cmd run test` (JSON reporter để đếm) | 253 passed, 0 failed; 28 file |
| Frontend build | `npm.cmd run build` | Thành công; cảnh báo chunk >500 kB |
| Full browser E2E | `npm.cmd run e2e` | 37 PASS, 2 NOT_COVERED, 0 FAIL |
| Whitespace | `git diff --check` | Sạch |

Debug đầu tiên ở thư mục chuẩn gặp DLL bị API đang chạy khóa; đã giữ API và dùng OutputPath riêng, không thay code để né build. Backend thêm 29 theory cases (12 trợ giúp, 17 nhóm alias), bên trong kiểm từng cụm có dấu/không dấu, provider Times.Never, 0 tool cho Help, scope theo vai trò/tài nguyên và fingerprint nghiệp vụ không đổi. Toàn bộ test cũ vẫn chạy trong suite 911. Hai scenario hình ảnh mới kiểm desktop/mobile; các scenario cũ cũng chạy đầy đủ. Hai NOT_COVERED giữ lý do fixture nêu trên, không tính là PASS.

## Bảo toàn cơ chế đặt lịch hiện có

Trước xác nhận, wizard chỉ có thể ghi `AiSelectionSnapshots`, `AiBookingConfirmations`, `AiSessions`, `AiAuditLogs`; chưa tạo Appointment. Test wizard kiểm fingerprint các bảng trước/sau và kiểm xác nhận lặp không tạo lịch thứ hai. Bản vá L-A trước đó đã thêm giới hạn DTO 2000 ký tự: vượt giới hạn bị `[ApiController]` trả HTTP 400 không phản chiếu lý do. Đầu vào 501–2000 vẫn qua screening cấp cứu/injection/PII trước `INVALID_REASON`. Ba regex PII dùng timeout 100 ms, timeout được coi là PII và đi tới `PII_BLOCKED`, không log input/exception. Không đổi pattern ở đầu vào bình thường.

Test L-A hiện có kiểm Reason 2001 ký tự trả 400 không phản chiếu và không ghi wizard; 2000 ký tự `a` trả `INVALID_REASON`, đầu vào dài chứa dấu hiệu cấp cứu/injection/PII vẫn bị chặn trước lỗi độ dài (ngưỡng thời gian rộng 10 giây). `ContainsPii` với 200.000 ký tự `a` trả `false` trong ngân sách 5 giây; runtime có thể bỏ qua nhanh chuỗi không chứa email, nên test này không coi mọi chuỗi dài đều phải timeout. Nhánh catch timeout fail-closed được giữ trong code; không có bằng chứng test timeout thực tế ổn định trong test hiện có.

Ứng dụng hiện gọi `AddDataProtection()`; chưa cấu hình lưu/chia sẻ key ring chung. Token đang dở có thể mất hiệu lực khi restart làm mất/đổi key ring hoặc khi instance khác không có cùng khóa. Chưa kiểm chứng restart hay nhiều instance trong môi trường triển khai thật; không khẳng định mọi restart đều làm mất token. Harness synthetic dùng khóa ephemeral.

## Hướng phát triển đang được cân nhắc

Mô hình nhỏ hiểu câu gõ tự do không dùng LLM và tập kiểm tra viết tay cho hướng này chưa được triển khai. Đường suggestion và wizard đã có như mô tả trên; kết quả kiểm thử kỹ thuật không phải đánh giá chất lượng AI hay y khoa. Việc cấu hình khóa dùng chung và phạm vi mô hình câu gõ tự do cần chủ đề tài quyết định trước khi triển khai tiếp.
