# Hidden human-written evaluation

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
