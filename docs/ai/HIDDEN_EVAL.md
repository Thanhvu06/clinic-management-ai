# Hidden human-written evaluation

## Lần chấm 1 (T2 theo cách cũ)

Giữ nguyên toàn bộ số liệu và nội dung báo cáo lần 1 bên dưới. T2 lần 1 dùng LastRoute.Label kể cả fallback; không phải T2 strict/lenient theo quyết định cuối của lần sau.

Viết tay bởi Nguyễn Thanh Vũ, 2026-10-18, không có AI hỗ trợ; nằm ngoài repo

Số dòng hợp lệ trước loại trùng: 106. N: 105. Số câu bị loại: 1.
STT trùng data: 2. STT trùng nội bộ: 0.
Manifest đếm toàn bộ dòng hợp lệ trước loại trùng; dòng trống (kể cả chỉ điền sẵn STT) và VÍ DỤ không được tính.
Nếu bốn header STT/Vai trò/Câu hỏi/Nhãn đúng nằm đúng A–D và header E trống, E vẫn được đọc làm Ghi chú; cột bổ sung được bỏ qua.
So khớp trùng trên các ô TSV và giá trị chuỗi JSON trong data (đệ quy), không phân biệt vai trò; giữ lần đầu nếu trùng nội bộ.

T1: RoleIntentModel v2 thật, lọc nhãn theo vai trò, ngưỡng metadata = 0.75; accepted khi confidence >= ngưỡng.
T2: HybridIntentRouter.Plan, AiConversationPipeline mặc định (Shadow), role của từng dòng; resource rỗng, không memory. OverrideEnabled = True; OverrideThreshold = 0.8 từ appsettings.json.
Nhãn T2 lấy nguyên từ LastRoute.Label, kể cả fallback; đây là accuracy nhãn định tuyến, không phải thành công của tool hay câu trả lời. Không gọi provider hoặc thực thi tool.
Macro-F1 bình quân các nhãn xuất hiện trong gold hoặc dự đoán của tầng/phạm vi tương ứng; chia cho 0 cho P/R/F1 được tính là 0.

## Tổng và theo vai trò

| Phạm vi | N | T1 đúng | T1 accuracy | T1 macro-F1 | Coverage | Accepted accuracy | Không chắc | T1 Wilson 95% | T2 đúng | T2 accuracy | T2 macro-F1 | T2 Wilson 95% |
|---|---:|---:|---:|---:|---:|---:|---:|---|---:|---:|---:|---|
| Tổng | 105 | 86 | 81.90% | 82.24% | 58.10% | 91.80% | 41.90% | [73.46%, 88.10%] | 61 | 58.10% | 53.05% | [48.54%, 67.08%] |
| Patient | 28 | 24 | 85.71% | 78.53% | 53.57% | 93.33% | 46.43% | [68.51%, 94.30%] | 16 | 57.14% | 42.62% | [39.07%, 73.49%] |
| Receptionist | 17 | 14 | 82.35% | 76.39% | 64.71% | 100.00% | 35.29% | [58.97%, 93.81%] | 12 | 70.59% | 50.83% | [46.87%, 86.72%] |
| Doctor | 21 | 17 | 80.95% | 69.96% | 66.67% | 85.71% | 33.33% | [60.00%, 92.33%] | 7 | 33.33% | 27.49% | [17.19%, 54.63%] |
| DiagnosticTechnician | 9 | 6 | 66.67% | 51.39% | 22.22% | 100.00% | 77.78% | [35.42%, 87.94%] | 6 | 66.67% | 51.39% | [35.42%, 87.94%] |
| Pharmacist | 17 | 15 | 88.24% | 80.16% | 70.59% | 91.67% | 29.41% | [65.66%, 96.71%] | 10 | 58.82% | 50.67% | [36.01%, 78.39%] |
| Admin | 13 | 10 | 76.92% | 73.33% | 53.85% | 85.71% | 46.15% | [49.74%, 91.82%] | 10 | 76.92% | 73.33% | [49.74%, 91.82%] |

## Source T2

Source override được giữ riêng theo production (thuộc nhánh dùng model).

| Phạm vi | rule | model | fallback | override |
|---|---:|---:|---:|---:|
| Tổng | 63 (60.00%) | 13 (12.38%) | 22 (20.95%) | 7 (6.67%) |
| Patient | 23 (82.14%) | 2 (7.14%) | 1 (3.57%) | 2 (7.14%) |
| Receptionist | 11 (64.71%) | 2 (11.76%) | 3 (17.65%) | 1 (5.88%) |
| Doctor | 14 (66.67%) | 2 (9.52%) | 4 (19.05%) | 1 (4.76%) |
| DiagnosticTechnician | 2 (22.22%) | 1 (11.11%) | 6 (66.67%) | 0 (0.00%) |
| Pharmacist | 9 (52.94%) | 4 (23.53%) | 4 (23.53%) | 0 (0.00%) |
| Admin | 4 (30.77%) | 2 (15.38%) | 4 (30.77%) | 3 (23.08%) |

## P/R/F1 theo nhãn

| Nhãn | Support | T1 P | T1 R | T1 F1 | T2 P | T2 R | T2 F1 |
|---|---:|---:|---:|---:|---:|---:|---:|
| ActionRequest | 6 | 50.00% | 83.33% | 62.50% | 25.00% | 50.00% | 33.33% |
| AiHealth | 4 | 100.00% | 50.00% | 66.67% | 100.00% | 50.00% | 66.67% |
| ClinicKnowledge | 6 | 100.00% | 66.67% | 80.00% | 40.00% | 66.67% | 50.00% |
| ConfirmBooking | 0 | 0.00% | 0.00% | 0.00% | 0.00% | 0.00% | 0.00% |
| DashboardMetrics | 4 | 66.67% | 100.00% | 80.00% | 66.67% | 100.00% | 80.00% |
| DiagnosticOrders | 4 | 80.00% | 100.00% | 88.89% | 0.00% | 0.00% | 0.00% |
| DoctorQueue | 4 | 66.67% | 100.00% | 80.00% | 100.00% | 75.00% | 85.71% |
| Greeting | 5 | 100.00% | 80.00% | 88.89% | 100.00% | 60.00% | 75.00% |
| Help | 6 | 85.71% | 100.00% | 92.31% | 83.33% | 83.33% | 83.33% |
| InventoryStatus | 4 | 100.00% | 100.00% | 100.00% | 100.00% | 100.00% | 100.00% |
| LookupAppointment | 4 | 100.00% | 50.00% | 66.67% | 100.00% | 50.00% | 66.67% |
| ModifyDraft | 0 | 0.00% | 0.00% | 0.00% | 0.00% | 0.00% | 0.00% |
| MyAppointments | 4 | 100.00% | 100.00% | 100.00% | 66.67% | 50.00% | 57.14% |
| MyBills | 4 | 100.00% | 100.00% | 100.00% | 100.00% | 100.00% | 100.00% |
| MyDiagnosticResults | 4 | 100.00% | 75.00% | 85.71% | 100.00% | 100.00% | 100.00% |
| MyPrescriptions | 4 | 100.00% | 75.00% | 85.71% | 0.00% | 0.00% | 0.00% |
| MyVisits | 4 | 75.00% | 75.00% | 75.00% | 66.67% | 50.00% | 57.14% |
| OutOfScope | 6 | 66.67% | 33.33% | 44.44% | 100.00% | 33.33% | 50.00% |
| PatientSummary | 4 | 75.00% | 75.00% | 75.00% | 0.00% | 0.00% | 0.00% |
| PrescriptionPayment | 4 | 80.00% | 100.00% | 88.89% | 100.00% | 25.00% | 40.00% |
| PrescriptionQueue | 4 | 100.00% | 75.00% | 85.71% | 100.00% | 50.00% | 66.67% |
| PrescriptionStatus | 4 | 100.00% | 75.00% | 85.71% | 100.00% | 50.00% | 66.67% |
| ProvideReason | 0 | 0.00% | 0.00% | 0.00% | 0.00% | 0.00% | 0.00% |
| ReceptionQueue | 4 | 80.00% | 100.00% | 88.89% | 75.00% | 75.00% | 75.00% |
| StartBooking | 4 | 80.00% | 100.00% | 88.89% | 66.67% | 50.00% | 57.14% |
| TechnicianWorklist | 4 | 75.00% | 75.00% | 75.00% | 75.00% | 75.00% | 75.00% |
| TodayAppointments | 4 | 80.00% | 100.00% | 88.89% | 100.00% | 100.00% | 100.00% |
| UnclearOrOutOfScope | 0 | 0.00% | 0.00% | 0.00% | 0.00% | 0.00% | 0.00% |

## Top 10 cặp nhầm T1

| Nhãn đúng | Nhãn dự đoán | Số lượng |
|---|---|---:|
| OutOfScope | ActionRequest | 3 |
| AiHealth | DashboardMetrics | 2 |
| ActionRequest | DiagnosticOrders | 1 |
| ClinicKnowledge | Help | 1 |
| ClinicKnowledge | ReceptionQueue | 1 |
| Greeting | TechnicianWorklist | 1 |
| LookupAppointment | ActionRequest | 1 |
| LookupAppointment | TodayAppointments | 1 |
| MyDiagnosticResults | ActionRequest | 1 |
| MyPrescriptions | MyVisits | 1 |

## Top 10 cặp nhầm T2

| Nhãn đúng | Nhãn dự đoán | Số lượng |
|---|---|---:|
| DiagnosticOrders | UnclearOrOutOfScope | 4 |
| AiHealth | DashboardMetrics | 2 |
| MyPrescriptions | ClinicKnowledge | 2 |
| OutOfScope | ActionRequest | 2 |
| PrescriptionPayment | UnclearOrOutOfScope | 2 |
| StartBooking | ProvideReason | 2 |
| ActionRequest | ConfirmBooking | 1 |
| ActionRequest | MyAppointments | 1 |
| ActionRequest | UnclearOrOutOfScope | 1 |
| ClinicKnowledge | Help | 1 |

## Các STT dự đoán sai

| STT | Vai trò | Nhãn đúng | T1 dự đoán | T2 dự đoán |
|---:|---|---|---|---|
| 4 | Doctor | Greeting | Greeting | ClinicKnowledge |
| 5 | DiagnosticTechnician | Greeting | TechnicianWorklist | TechnicianWorklist |
| 9 | Receptionist | Help | Help | ProvideReason |
| 15 | Receptionist | ClinicKnowledge | ReceptionQueue | ReceptionQueue |
| 17 | DiagnosticTechnician | ClinicKnowledge | Help | Help |
| 20 | Patient | ActionRequest | ActionRequest | MyAppointments |
| 22 | Doctor | ActionRequest | DiagnosticOrders | UnclearOrOutOfScope |
| 24 | Pharmacist | ActionRequest | ActionRequest | ConfirmBooking |
| 26 | Patient | OutOfScope | ActionRequest | ProvideReason |
| 28 | Doctor | OutOfScope | PatientSummary | PatientSummary |
| 30 | Pharmacist | OutOfScope | ActionRequest | ActionRequest |
| 31 | Admin | OutOfScope | ActionRequest | ActionRequest |
| 34 | Patient | StartBooking | StartBooking | ProvideReason |
| 35 | Patient | StartBooking | StartBooking | ProvideReason |
| 37 | Patient | MyAppointments | MyAppointments | StartBooking |
| 39 | Patient | MyAppointments | MyAppointments | ModifyDraft |
| 42 | Patient | MyVisits | StartBooking | ActionRequest |
| 43 | Patient | MyVisits | MyVisits | ClinicKnowledge |
| 47 | Patient | MyDiagnosticResults | ActionRequest | MyDiagnosticResults |
| 48 | Patient | MyPrescriptions | MyPrescriptions | ClinicKnowledge |
| 49 | Patient | MyPrescriptions | MyVisits | MyVisits |
| 50 | Patient | MyPrescriptions | MyPrescriptions | ClinicKnowledge |
| 51 | Patient | MyPrescriptions | MyPrescriptions | ActionRequest |
| 60 | Receptionist | ReceptionQueue | ReceptionQueue | ActionRequest |
| 66 | Receptionist | LookupAppointment | ActionRequest | UnclearOrOutOfScope |
| 67 | Receptionist | LookupAppointment | TodayAppointments | ClinicKnowledge |
| 69 | Doctor | DoctorQueue | DoctorQueue | UnclearOrOutOfScope |
| 72 | Doctor | PatientSummary | PatientSummary | ProvideReason |
| 73 | Doctor | PatientSummary | PatientSummary | UnclearOrOutOfScope |
| 74 | Doctor | PatientSummary | PatientSummary | ActionRequest |
| 75 | Doctor | PatientSummary | DoctorQueue | ClinicKnowledge |
| 76 | Doctor | DiagnosticOrders | DiagnosticOrders | UnclearOrOutOfScope |
| 77 | Doctor | DiagnosticOrders | DiagnosticOrders | UnclearOrOutOfScope |
| 78 | Doctor | DiagnosticOrders | DiagnosticOrders | UnclearOrOutOfScope |
| 79 | Doctor | DiagnosticOrders | DiagnosticOrders | UnclearOrOutOfScope |
| 81 | Doctor | PrescriptionStatus | PrescriptionStatus | UnclearOrOutOfScope |
| 82 | Doctor | PrescriptionStatus | DoctorQueue | ActionRequest |
| 84 | DiagnosticTechnician | TechnicianWorklist | OutOfScope | TechnicianWorklist |
| 85 | DiagnosticTechnician | TechnicianWorklist | TechnicianWorklist | ActionRequest |
| 88 | Pharmacist | PrescriptionQueue | PrescriptionQueue | ActionRequest |
| 91 | Pharmacist | PrescriptionQueue | PrescriptionPayment | UnclearOrOutOfScope |
| 96 | Pharmacist | PrescriptionPayment | PrescriptionPayment | UnclearOrOutOfScope |
| 98 | Pharmacist | PrescriptionPayment | PrescriptionPayment | UnclearOrOutOfScope |
| 99 | Pharmacist | PrescriptionPayment | PrescriptionPayment | ConfirmBooking |
| 105 | Admin | AiHealth | DashboardMetrics | DashboardMetrics |
| 107 | Admin | AiHealth | DashboardMetrics | DashboardMetrics |

## Giới hạn

Bộ đề do 1 người viết, khoảng 4–6 câu/nhãn nên khoảng tin cậy rộng; không đại diện đầy đủ người dùng hay phân bố production. Wilson 95% là khoảng cho accuracy nhị thức, giả định các câu độc lập; câu cùng người viết có thể tương quan.
Context tối thiểu không có ca bệnh/đơn thuốc được xác minh; router có thể yêu cầu làm rõ. Source fallback vẫn có thể ghi nhãn model dù quyết định thực tế giữ fallback. Kết quả không được dùng để chỉnh model, ngưỡng, rule hoặc dataset trong PR này.

Kiểm thử runner (XLSX tự tạo, không đọc đề thật): `dotnet test src/tools/ClinicManagement.AI.Training/HiddenEvalRunnerTests.proj --configuration Release`. MSBuild gọi self-test theo pattern Phase4 của tool, không thêm NuGet package.

## Xác minh lần chấm đã khóa

- Chấm thật thành công 1 lần cho cả T1/T2. Trước đó có 2 lần dừng validation trước inference (header Ghi chú trống; dòng mẫu chỉ có STT), đã được người dùng cho phép thử lại. Không dùng kết quả chấm để sửa model, ngưỡng, rule hoặc dataset.
- Build solution Release: đạt. Backend: 2.042/2.042 test đạt, 0 skip. Harness mới qua `dotnet test`: 14/14 self-test đạt. PR Guard: 40/40 test đạt.
- Chống lộ đề: lấy ngẫu nhiên 5 câu trong 105 câu được chấm, truyền mẫu trong bộ nhớ cho `rg --fixed-strings`; 0 lần trùng trong repo (quét cả file ẩn; loại `.git`, thư mục build/dependency). Không ghi mẫu ra đĩa. SHA-256 file vẫn khớp manifest sau kiểm tra.
- verify-ui: không áp dụng vì không sửa frontend.

## Router v2 — tập phát triển

Baseline chạy trên main b3308a2 trước khi sửa router; sau sửa dùng cùng file và cùng context tối thiểu. Không dùng XLSX ẩn để phát triển hoặc chọn cách sửa.

Phase4 và patient copilot benchmark hiện không gọi HybridIntentRouter: các số dưới đây là kiểm tra hồi quy planner/heuristic. `--eval-role-intent --version v1` chỉ đọc cached report (cached=true); không chấm lại hay ghi report v2 đã đóng băng.

| Phép đo không ẩn | N | Trước | Sau |
|---|---:|---:|---:|
| Phase4 safety | 240 | 100.00% (240/240) | 100.00% (240/240) |
| Phase4 patient intent rule | 120 | 90.00% (108/120) | 90.00% (108/120) |
| Phase4 planner/role routing | 93 | 95.70% (89/93) | 95.70% (89/93) |
| Phase4 ML patient-only | 108 | 63.89% (69/108) | 63.89% (69/108) |
| Patient copilot safety | 70 | 100.00% | 100.00% |
| Patient copilot heuristic routing | 70 | 84.29% | 84.29% |
| Patient copilot outcome | 70 | 98.57% | 98.57% |
| Cached role-intent v1 filtered accuracy | 240 | 68.33% | 68.33% |
| Cached role-intent v1 filtered macro-F1 | 240 | 65.24% | 65.24% |
| Router validation T1 accuracy | 498 | 66.67% (332/498) | 66.67% (332/498) |
| Router validation T2 strict accuracy | 498 | 26.71% (133/498) | 26.71% (133/498) |
| Router validation T2 lenient accuracy | 498 | 40.16% (200/498) | 40.16% (200/498) |
| Router validation T2 strict macro-F1 | 498 | 34.92% | 34.92% |

Phép đo router bổ sung đọc `role_intent_validation_v1.json` có sẵn: 283 dòng, trong đó 43 dòng vai trò Chung được mở rộng cho sáu vai trò, thành 498 cặp câu–vai trò. Dùng đúng RoleIntentModel v2, ngưỡng metadata 0.75, override 0.8 từ appsettings, AiConversationPipeline mặc định và HybridIntentRouter.Plan. Cả trước/sau đều dùng metric T2 đã sửa: fallback luôn sai; strict loại hỏi lại; lenient chấp nhận hỏi lại khi không fallback. Đây là tập phát triển từ dữ liệu có sẵn, các mẫu mở rộng vai trò tương quan và không đại diện held-out độc lập.

Số hỏi lại trước/sau: 67/67. Source trước/sau đều là rule=198, model=56, fallback=226, override=18. Điểm không đổi trên tập validation này; test tổng hợp kiểm tra trực tiếp bảy bước đặt lịch khi không/có memory và các biên ngưỡng, safety, Guard. Handler deterministic đã resolve vẫn giữ ưu tiên; chỉ tín hiệu analyzer rõ ràng cho bước giữa thiếu context được hạ ưu tiên.

Gate B sau sửa: registry hợp lệ, ba candidate hoàn tất và deterministic; quyết định NOT_PROMOTED, không thay artifact hay cấu hình production.

### Kiểm chứng trước lần chấm tiếp theo

Build Release đạt. Toàn bộ backend: 2.060/2.060 test đạt, 0 fail, 0 skip; nhóm router: 94/94 (gồm 18 ca mới). Harness runner: 18/18 self-test đạt, giữ nguyên 14 test cũ và thêm 4 nhóm metric/report. Không sửa hoặc skip test cũ. Phase4 và Gate B đã chạy bằng dữ liệu không ẩn; mọi report trung gian nằm ngoài phần được commit.

Main đã fetch và không đổi so với baseline b3308a2. Model, metadata/ngưỡng 0.75/override 0.8, dataset, VietnameseIntentClassifier và AiDeterministicPlanner không đổi. Bước tiếp theo là một lần chấm XLSX ẩn đã khóa; kết quả sẽ được append tự động và không dùng để chỉnh thêm code hay dữ liệu.

## Lần chấm 2 (sau sửa router)

Viết tay bởi Nguyễn Thanh Vũ, 2026-10-18, không có AI hỗ trợ; nằm ngoài repo

Số dòng hợp lệ trước loại trùng: 106. N: 105. Số câu bị loại: 1.
STT trùng data: 2. STT trùng nội bộ: 0.
Manifest đếm toàn bộ dòng hợp lệ trước loại trùng; dòng trống (kể cả chỉ điền sẵn STT) và VÍ DỤ không được tính.
Nếu bốn header STT/Vai trò/Câu hỏi/Nhãn đúng nằm đúng A–D và header E trống, E vẫn được đọc làm Ghi chú; cột bổ sung được bỏ qua.
So khớp trùng trên các ô TSV và giá trị chuỗi JSON trong data (đệ quy), không phân biệt vai trò; giữ lần đầu nếu trùng nội bộ.

T1: RoleIntentModel v2 thật, lọc nhãn theo vai trò, ngưỡng metadata = 0.75; accepted khi confidence >= ngưỡng.
T2: HybridIntentRouter.Plan, AiConversationPipeline mặc định (Shadow), role của từng dòng; resource rỗng, không memory. OverrideEnabled = True; OverrideThreshold = 0.8 từ appsettings.json.
T2 dùng quyết định Plan trả về: tool thực tế được map về nhãn theo vai trò; Greeting/RoleHelp/BookingWizard dùng nhãn của handler. RequiresProvider được ghi ProviderRequired, tool không map được ghi UnmappedDecision. LastRoute.Label chỉ là telemetry, không dùng thay cho quyết định cuối.
Source=fallback luôn sai ở cả strict và lenient, kể cả telemetry label trùng gold. Hỏi lại khi có Clarification hoặc ErrorCode/SubIntent thuộc RESOURCE_CONTEXT_REQUIRED, MissingAssignedCase, MissingPrescriptionForPayment, MissingAppointmentCode.
T2 strict: chỉ đúng khi không fallback, không hỏi lại, và nhãn quyết định cuối trùng gold. T2 lenient: đúng theo strict hoặc hỏi lại (vẫn loại fallback). Lenient là mức chấp nhận được theo quy ước, không chứng minh hiểu đúng intent. Không gọi provider hoặc thực thi tool.
Macro-F1 bình quân các nhãn xuất hiện trong gold hoặc dự đoán của tầng/phạm vi tương ứng; chia cho 0 cho P/R/F1 được tính là 0.

### Tổng và theo vai trò

| Phạm vi | N | T1 đúng | T1 accuracy | T1 macro-F1 | Coverage | Accepted accuracy | Không chắc | T1 Wilson 95% | Hỏi lại | T2 strict đúng | T2 strict accuracy | T2 strict macro-F1 | T2 strict Wilson 95% | T2 lenient đúng | T2 lenient accuracy | T2 lenient Wilson 95% |
|---|---:|---:|---:|---:|---:|---:|---:|---|---:|---:|---:|---:|---|---:|---:|---|
| Tổng | 105 | 86 | 81.90% | 82.24% | 58.10% | 91.80% | 41.90% | [73.46%, 88.10%] | 23 | 43 | 40.95% | 42.80% | [32.03%, 50.52%] | 66 | 62.86% | [53.31%, 71.49%] |
| Patient | 28 | 24 | 85.71% | 78.53% | 53.57% | 93.33% | 46.43% | [68.51%, 94.30%] | 2 | 14 | 50.00% | 30.33% | [32.63%, 67.37%] | 16 | 57.14% | [39.07%, 73.49%] |
| Receptionist | 17 | 14 | 82.35% | 76.39% | 64.71% | 100.00% | 35.29% | [58.97%, 93.81%] | 2 | 10 | 58.82% | 35.24% | [36.01%, 78.39%] | 12 | 70.59% | [46.87%, 86.72%] |
| Doctor | 21 | 17 | 80.95% | 69.96% | 66.67% | 85.71% | 33.33% | [60.00%, 92.33%] | 12 | 3 | 14.29% | 17.27% | [4.98%, 34.64%] | 15 | 71.43% | [50.04%, 86.19%] |
| DiagnosticTechnician | 9 | 6 | 66.67% | 51.39% | 22.22% | 100.00% | 77.78% | [35.42%, 87.94%] | 1 | 2 | 22.22% | 8.33% | [6.32%, 54.74%] | 3 | 33.33% | [12.06%, 64.58%] |
| Pharmacist | 17 | 15 | 88.24% | 80.16% | 70.59% | 91.67% | 29.41% | [65.66%, 96.71%] | 6 | 6 | 35.29% | 32.57% | [17.31%, 58.70%] | 12 | 70.59% | [46.87%, 86.72%] |
| Admin | 13 | 10 | 76.92% | 73.33% | 53.85% | 85.71% | 46.15% | [49.74%, 91.82%] | 0 | 8 | 61.54% | 55.21% | [35.52%, 82.29%] | 8 | 61.54% | [35.52%, 82.29%] |

### Source T2

Source override được giữ riêng theo production (thuộc nhánh dùng model).

| Phạm vi | rule | model | fallback | override |
|---|---:|---:|---:|---:|
| Tổng | 57 (54.29%) | 14 (13.33%) | 27 (25.71%) | 7 (6.67%) |
| Patient | 19 (67.86%) | 3 (10.71%) | 4 (14.29%) | 2 (7.14%) |
| Receptionist | 10 (58.82%) | 2 (11.76%) | 4 (23.53%) | 1 (5.88%) |
| Doctor | 14 (66.67%) | 2 (9.52%) | 4 (19.05%) | 1 (4.76%) |
| DiagnosticTechnician | 2 (22.22%) | 1 (11.11%) | 6 (66.67%) | 0 (0.00%) |
| Pharmacist | 8 (47.06%) | 4 (23.53%) | 5 (29.41%) | 0 (0.00%) |
| Admin | 4 (30.77%) | 2 (15.38%) | 4 (30.77%) | 3 (23.08%) |

### P/R/F1 theo nhãn (T2 strict)

| Nhãn | Support | T1 P | T1 R | T1 F1 | T2 P | T2 R | T2 F1 |
|---|---:|---:|---:|---:|---:|---:|---:|
| ActionRequest | 6 | 50.00% | 83.33% | 62.50% | 0.00% | 0.00% | 0.00% |
| AiHealth | 4 | 100.00% | 50.00% | 66.67% | 100.00% | 50.00% | 66.67% |
| Clarification | 0 | 0.00% | 0.00% | 0.00% | 0.00% | 0.00% | 0.00% |
| ClinicKnowledge | 6 | 100.00% | 66.67% | 80.00% | 40.00% | 66.67% | 50.00% |
| DashboardMetrics | 4 | 66.67% | 100.00% | 80.00% | 75.00% | 75.00% | 75.00% |
| DiagnosticOrders | 4 | 80.00% | 100.00% | 88.89% | 0.00% | 0.00% | 0.00% |
| DoctorQueue | 4 | 66.67% | 100.00% | 80.00% | 100.00% | 25.00% | 40.00% |
| Fallback | 0 | 0.00% | 0.00% | 0.00% | 0.00% | 0.00% | 0.00% |
| Greeting | 5 | 100.00% | 80.00% | 88.89% | 100.00% | 60.00% | 75.00% |
| Help | 6 | 85.71% | 100.00% | 92.31% | 100.00% | 33.33% | 50.00% |
| InventoryStatus | 4 | 100.00% | 100.00% | 100.00% | 100.00% | 75.00% | 85.71% |
| LookupAppointment | 4 | 100.00% | 50.00% | 66.67% | 100.00% | 50.00% | 66.67% |
| MyAppointments | 4 | 100.00% | 100.00% | 100.00% | 66.67% | 50.00% | 57.14% |
| MyBills | 4 | 100.00% | 100.00% | 100.00% | 100.00% | 100.00% | 100.00% |
| MyDiagnosticResults | 4 | 100.00% | 75.00% | 85.71% | 100.00% | 100.00% | 100.00% |
| MyPrescriptions | 4 | 100.00% | 75.00% | 85.71% | 0.00% | 0.00% | 0.00% |
| MyVisits | 4 | 75.00% | 75.00% | 75.00% | 66.67% | 50.00% | 57.14% |
| OutOfScope | 6 | 66.67% | 33.33% | 44.44% | 0.00% | 0.00% | 0.00% |
| PatientSummary | 4 | 75.00% | 75.00% | 75.00% | 0.00% | 0.00% | 0.00% |
| PrescriptionPayment | 4 | 80.00% | 100.00% | 88.89% | 0.00% | 0.00% | 0.00% |
| PrescriptionQueue | 4 | 100.00% | 75.00% | 85.71% | 100.00% | 25.00% | 40.00% |
| PrescriptionStatus | 4 | 100.00% | 75.00% | 85.71% | 0.00% | 0.00% | 0.00% |
| ProviderRequired | 0 | 0.00% | 0.00% | 0.00% | 0.00% | 0.00% | 0.00% |
| ReceptionQueue | 4 | 80.00% | 100.00% | 88.89% | 100.00% | 75.00% | 85.71% |
| StartBooking | 4 | 80.00% | 100.00% | 88.89% | 100.00% | 25.00% | 40.00% |
| TechnicianWorklist | 4 | 75.00% | 75.00% | 75.00% | 100.00% | 50.00% | 66.67% |
| TodayAppointments | 4 | 80.00% | 100.00% | 88.89% | 100.00% | 100.00% | 100.00% |

### Top 10 cặp nhầm T1

| Nhãn đúng | Nhãn dự đoán | Số lượng |
|---|---|---:|
| OutOfScope | ActionRequest | 3 |
| AiHealth | DashboardMetrics | 2 |
| ActionRequest | DiagnosticOrders | 1 |
| ClinicKnowledge | Help | 1 |
| ClinicKnowledge | ReceptionQueue | 1 |
| Greeting | TechnicianWorklist | 1 |
| LookupAppointment | ActionRequest | 1 |
| LookupAppointment | TodayAppointments | 1 |
| MyDiagnosticResults | ActionRequest | 1 |
| MyPrescriptions | MyVisits | 1 |

### Top 10 cặp nhầm T2 strict

| Nhãn đúng | Nhãn dự đoán | Số lượng |
|---|---|---:|
| OutOfScope | Fallback | 6 |
| ActionRequest | Fallback | 4 |
| DiagnosticOrders | Clarification | 4 |
| Help | Fallback | 4 |
| PrescriptionPayment | Clarification | 4 |
| PatientSummary | Clarification | 3 |
| PrescriptionStatus | Clarification | 3 |
| ClinicKnowledge | Fallback | 2 |
| DoctorQueue | Fallback | 2 |
| MyPrescriptions | ClinicKnowledge | 2 |

### Các STT dự đoán sai (T1 hoặc T2 strict)

| STT | Vai trò | Nhãn đúng | T1 dự đoán | T2 strict dự đoán | Hỏi lại | T2 lenient đúng |
|---:|---|---|---|---|---:|---:|
| 4 | Doctor | Greeting | Greeting | ClinicKnowledge | 0 | 0 |
| 5 | DiagnosticTechnician | Greeting | TechnicianWorklist | Fallback | 0 | 0 |
| 8 | Patient | Help | Help | Fallback | 0 | 0 |
| 9 | Receptionist | Help | Help | Fallback | 0 | 0 |
| 11 | DiagnosticTechnician | Help | Help | Fallback | 0 | 0 |
| 12 | Pharmacist | Help | Help | Fallback | 0 | 0 |
| 15 | Receptionist | ClinicKnowledge | ReceptionQueue | Fallback | 0 | 0 |
| 17 | DiagnosticTechnician | ClinicKnowledge | Help | Fallback | 0 | 0 |
| 20 | Patient | ActionRequest | ActionRequest | MyAppointments | 0 | 0 |
| 21 | Receptionist | ActionRequest | ActionRequest | Fallback | 0 | 0 |
| 22 | Doctor | ActionRequest | DiagnosticOrders | Clarification | 1 | 1 |
| 23 | DiagnosticTechnician | ActionRequest | ActionRequest | Fallback | 0 | 0 |
| 24 | Pharmacist | ActionRequest | ActionRequest | Fallback | 0 | 0 |
| 25 | Admin | ActionRequest | ActionRequest | Fallback | 0 | 0 |
| 26 | Patient | OutOfScope | ActionRequest | Fallback | 0 | 0 |
| 27 | Receptionist | OutOfScope | OutOfScope | Fallback | 0 | 0 |
| 28 | Doctor | OutOfScope | PatientSummary | Fallback | 0 | 0 |
| 29 | DiagnosticTechnician | OutOfScope | OutOfScope | Fallback | 0 | 0 |
| 30 | Pharmacist | OutOfScope | ActionRequest | Fallback | 0 | 0 |
| 31 | Admin | OutOfScope | ActionRequest | Fallback | 0 | 0 |
| 32 | Patient | StartBooking | StartBooking | ProviderRequired | 0 | 0 |
| 33 | Patient | StartBooking | StartBooking | ProviderRequired | 0 | 0 |
| 35 | Patient | StartBooking | StartBooking | Fallback | 0 | 0 |
| 37 | Patient | MyAppointments | MyAppointments | ProviderRequired | 0 | 0 |
| 39 | Patient | MyAppointments | MyAppointments | Fallback | 0 | 0 |
| 42 | Patient | MyVisits | StartBooking | Clarification | 1 | 1 |
| 43 | Patient | MyVisits | MyVisits | ClinicKnowledge | 0 | 0 |
| 47 | Patient | MyDiagnosticResults | ActionRequest | MyDiagnosticResults | 0 | 1 |
| 48 | Patient | MyPrescriptions | MyPrescriptions | ClinicKnowledge | 0 | 0 |
| 49 | Patient | MyPrescriptions | MyVisits | MyVisits | 0 | 0 |
| 50 | Patient | MyPrescriptions | MyPrescriptions | ClinicKnowledge | 0 | 0 |
| 51 | Patient | MyPrescriptions | MyPrescriptions | Clarification | 1 | 1 |
| 60 | Receptionist | ReceptionQueue | ReceptionQueue | Clarification | 1 | 1 |
| 66 | Receptionist | LookupAppointment | ActionRequest | Clarification | 1 | 1 |
| 67 | Receptionist | LookupAppointment | TodayAppointments | ClinicKnowledge | 0 | 0 |
| 68 | Doctor | DoctorQueue | DoctorQueue | Fallback | 0 | 0 |
| 69 | Doctor | DoctorQueue | DoctorQueue | Clarification | 1 | 1 |
| 71 | Doctor | DoctorQueue | DoctorQueue | Fallback | 0 | 0 |
| 72 | Doctor | PatientSummary | PatientSummary | Clarification | 1 | 1 |
| 73 | Doctor | PatientSummary | PatientSummary | Clarification | 1 | 1 |
| 74 | Doctor | PatientSummary | PatientSummary | Clarification | 1 | 1 |
| 75 | Doctor | PatientSummary | DoctorQueue | ClinicKnowledge | 0 | 0 |
| 76 | Doctor | DiagnosticOrders | DiagnosticOrders | Clarification | 1 | 1 |
| 77 | Doctor | DiagnosticOrders | DiagnosticOrders | Clarification | 1 | 1 |
| 78 | Doctor | DiagnosticOrders | DiagnosticOrders | Clarification | 1 | 1 |
| 79 | Doctor | DiagnosticOrders | DiagnosticOrders | Clarification | 1 | 1 |
| 80 | Doctor | PrescriptionStatus | PrescriptionStatus | Clarification | 1 | 1 |
| 81 | Doctor | PrescriptionStatus | PrescriptionStatus | Clarification | 1 | 1 |
| 82 | Doctor | PrescriptionStatus | DoctorQueue | Clarification | 1 | 1 |
| 83 | Doctor | PrescriptionStatus | PrescriptionStatus | Fallback | 0 | 0 |
| 84 | DiagnosticTechnician | TechnicianWorklist | OutOfScope | TechnicianWorklist | 0 | 1 |
| 85 | DiagnosticTechnician | TechnicianWorklist | TechnicianWorklist | Clarification | 1 | 1 |
| 86 | DiagnosticTechnician | TechnicianWorklist | TechnicianWorklist | Fallback | 0 | 0 |
| 88 | Pharmacist | PrescriptionQueue | PrescriptionQueue | Clarification | 1 | 1 |
| 90 | Pharmacist | PrescriptionQueue | PrescriptionQueue | Fallback | 0 | 0 |
| 91 | Pharmacist | PrescriptionQueue | PrescriptionPayment | Clarification | 1 | 1 |
| 93 | Pharmacist | InventoryStatus | InventoryStatus | Fallback | 0 | 0 |
| 96 | Pharmacist | PrescriptionPayment | PrescriptionPayment | Clarification | 1 | 1 |
| 97 | Pharmacist | PrescriptionPayment | PrescriptionPayment | Clarification | 1 | 1 |
| 98 | Pharmacist | PrescriptionPayment | PrescriptionPayment | Clarification | 1 | 1 |
| 99 | Pharmacist | PrescriptionPayment | PrescriptionPayment | Clarification | 1 | 1 |
| 103 | Admin | DashboardMetrics | DashboardMetrics | Fallback | 0 | 0 |
| 105 | Admin | AiHealth | DashboardMetrics | Fallback | 0 | 0 |
| 107 | Admin | AiHealth | DashboardMetrics | DashboardMetrics | 0 | 0 |

### Giới hạn

Bộ đề do 1 người viết, khoảng 4–6 câu/nhãn nên khoảng tin cậy rộng; không đại diện đầy đủ người dùng hay phân bố production. Wilson 95% là khoảng cho accuracy nhị thức, giả định các câu độc lập; câu cùng người viết có thể tương quan.
Context tối thiểu không có ca bệnh/đơn thuốc được xác minh; router có thể yêu cầu làm rõ. Strict và lenient lần này khác định nghĩa T2 của lần 1 nên không so trực tiếp thành mức cải thiện router. Không chỉnh gì thêm sau khi xem kết quả của lần chấm này.

Kiểm thử runner (XLSX tự tạo, không đọc đề thật): `dotnet test src/tools/ClinicManagement.AI.Training/HiddenEvalRunnerTests.proj --configuration Release`. MSBuild gọi self-test theo pattern Phase4 của tool, không thêm NuGet package.
