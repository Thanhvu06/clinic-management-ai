# Dữ liệu intent theo vai trò v1

194 câu hạt giống do AI Claude sinh; tác giả dự án đã duyệt và sửa 9 dòng có `nguon=claude_v1_sua`. SHA256 của TSV được ghim trong test; bản sao giữ nguyên byte nguồn. Đây không phải tập blind test.

## Nhãn, vai trò và tool

`Chung` trong hạt giống và bản ghi sinh đại diện cho cả 6 vai trò; các mã khác được đổi sang tên vai trò tiếng Anh trong đầu ra. `targetTool=null` không gọi tool. StartBooking dùng nút gợi ý `patient.start_booking` để mở booking wizard.

| Nhãn | Vai trò | Tool |
|---|---|---|
| Greeting | Cả 6 vai trò | null |
| Help | Cả 6 vai trò | null |
| ClinicKnowledge | Cả 6 vai trò | clinic.search_knowledge |
| ActionRequest | Cả 6 vai trò | null |
| OutOfScope | Cả 6 vai trò | null |
| StartBooking | Patient | null |
| MyAppointments | Patient | patient.get_my_appointments |
| MyVisits | Patient | patient.get_my_visits |
| MyDiagnosticResults | Patient | patient.get_my_diagnostic_results |
| MyPrescriptions | Patient | patient.get_my_prescriptions |
| MyBills | Patient | patient.get_my_bills |
| TodayAppointments | Receptionist | reception.get_today_appointments |
| ReceptionQueue | Receptionist | reception.get_queue |
| LookupAppointment | Receptionist | reception.lookup_appointment |
| DoctorQueue | Doctor | doctor.get_my_queue |
| PatientSummary | Doctor | doctor.get_patient_summary |
| DiagnosticOrders | Doctor | doctor.get_diagnostic_orders |
| PrescriptionStatus | Doctor | doctor.get_prescription_status |
| TechnicianWorklist | DiagnosticTechnician | technician.get_worklist |
| PrescriptionQueue | Pharmacist | pharmacist.get_prescription_queue |
| InventoryStatus | Pharmacist | pharmacist.get_inventory_status |
| PrescriptionPayment | Pharmacist | pharmacist.get_prescription_payment_status |
| DashboardMetrics | Admin | admin.get_dashboard_metrics |
| AiHealth | Admin | admin.get_ai_health |

## Sinh và chia tập

Bộ sinh dùng `new Random(20261002)`, duyệt theo thứ tự TSV, đánh `seedId=RI-<label>-<thứ tự 2 chữ số trong nhãn>`, không dùng thời gian hay Guid. Ứng viên theo thứ tự: gốc; bỏ dấu (đ→d); chữ thường và bỏ `? . !` ở cuối; viết tắt; một lỗi gõ; một tiền/hậu tố; bỏ dấu rồi viết tắt; bỏ dấu rồi lỗi gõ; bỏ dấu rồi tiền/hậu tố.

Từ điển cố định áp theo ranh giới từ cho cả dạng có/không dấu: không→ko hoặc k (lần lượt hai ứng viên nếu có từ đó), được→dc, bệnh nhân→bn, danh sách→ds, số lượng→sl, xét nghiệm→xn, kết quả→kq, gì→j, rồi→r, vậy→v, với→vs. Mỗi ứng viên viết tắt thay mọi mục hiện diện. Lỗi gõ chọn một từ dài ít nhất 4 ký tự bằng RNG rồi đảo hai ký tự liền nhau, mất hoặc lặp một ký tự; không sửa mã LH. Nếu không có từ đủ dài, ứng viên không đổi và sẽ bị loại trùng.

Mỗi phép tiền/hậu tố chọn đúng một mục bằng RNG trong tiền tố `cho hỏi `, `cho mình hỏi `, `ơi ` hoặc hậu tố ` ạ`, ` nhé`, ` với`, ` giúp mình`. Greeting và OutOfScope bỏ qua phép này. LookupAppointment giữ mã gốc ở bản gốc; các ứng viên khác dùng số ngẫu nhiên 3–5 chữ số và dạng LH-1234 / LH1234 / lh 1234.

Loại trùng chính xác, giữ tối đa 12 ứng viên đầu tiên. Chuẩn hóa so trùng: chữ thường, bỏ dấu, bỏ dấu câu, gộp khoảng trắng. Hai câu gốc trùng giữa nhãn làm bộ sinh dừng trước khi ghi đầu ra; mọi câu gốc được giữ chỗ trước để biến thể không chiếm câu gốc của nhãn khác. Biến thể trùng nhãn khác bị bỏ và đếm trong manifest. Dạng chuẩn hóa trùng trong cùng nhãn được phép.

Sau khi sinh biến thể, xáo Fisher–Yates các câu gốc trong từng nhãn bằng cùng RNG; đúng 2 câu gốc đầu vào validation, còn lại vào train. Tất cả biến thể đi theo seedId. Hai JSON dữ liệu và manifest dùng UTF-8 không BOM, LF, thụt lề 2 và thứ tự ổn định. Manifest ghi SHA256 byte của seeds, labels, train, validation; số lượng theo nhãn/tập; tổng; số ứng viên bỏ do trùng nhãn khác và provenance.

## Sinh lại

Chạy từ gốc repo (cũng tìm được thư mục data mặc định từ thư mục con):

```powershell
dotnet run --project src/tools/ClinicManagement.AI.Training/ClinicManagement.AI.Training.csproj --configuration Release -- --gen-role-intent
```

Để dùng thư mục khác có sẵn cả hai file seeds/labels:

```powershell
dotnet run --project src/tools/ClinicManagement.AI.Training/ClinicManagement.AI.Training.csproj --configuration Release -- --gen-role-intent --data-dir C:\duong-dan\data
```

Lệnh chỉ sinh dữ liệu, không train model hoặc gọi Gemini. `RoleIntentDatasetTests` kiểm tra hash/nhãn/vai trò, tái sinh byte, chia tập, trùng giữa nhãn, mã LH, giới hạn biến thể và không trùng `inputVi` trong phase5_blind_holdout.json.

## Giới hạn

Chưa có tập blind độc lập do người viết cho 24 nhãn này. Các biến thể vẫn có chung nguồn câu gốc; tập validation chỉ đo trên các câu gốc được tách ra. Dữ liệu sinh không thay thế câu thật từ người dùng và không chứng minh chất lượng mô hình.

## Tập đánh giá bổ sung (do AI viết)

`role_intent_eval_ai_v1.tsv` gồm 240 câu, 24 nhãn × 10 câu, do AI Claude viết một lần ngày 2026-10-02; không có người viết tay. Đây KHÔNG phải tập blind độc lập. Chỉ dùng tập này để đo, không dùng để train hay chọn mô hình. Manifest ghi số dòng từng nhãn, kích thước byte và nguồn gốc.

File được khóa bằng SHA-256 `4104b49e57d2f82758602faa63c255cdddf5a6ec2e895d045c27e04382dd4686`; không sửa sau khi commit. Test dùng đúng hàm chuẩn hóa của bộ sinh để kiểm tra không trùng seeds/train/validation hoặc tập blind đã có. Chưa có tập do người viết; cùng nguồn AI với hạt giống nên kết quả đo có thể lạc quan, không chứng minh chất lượng mô hình.

## Thí nghiệm ML.NET role-intent offline v1

Model này chỉ nằm trong tool training, chưa nối classifier runtime/API và không gọi Gemini/LLM live. Microsoft.ML 4.0.3 đã có sẵn, target net10.0. Artifact riêng ở `src/tools/ClinicManagement.AI.Training/models/role-intent-v1/`: `role_intent_model_v1.zip`, `role_intent_model_meta_v1.json`, `role_intent_eval_report_v1.json`. Các model/metadata production và dữ liệu cũ giữ nguyên.

### Lệnh và quy trình chốt

Hai lệnh nhận `--data-dir` và `--out-dir`; mặc định dùng data của tool và thư mục artifact riêng ở trên:

```powershell
dotnet run --project src/tools/ClinicManagement.AI.Training/ClinicManagement.AI.Training.csproj --configuration Release -- --train-role-intent --data-dir C:\duong-dan\data --out-dir C:\duong-dan\thi-nghiem-chua-do
dotnet run --project src/tools/ClinicManagement.AI.Training/ClinicManagement.AI.Training.csproj --configuration Release -- --eval-role-intent --data-dir C:\duong-dan\data --out-dir C:\duong-dan\thi-nghiem-chua-do
```

Train chỉ đọc train/validation/labels, thử trọng số lớp và chọn ngưỡng bằng validation. Trước lần eval đầu tiên, chạy train hai lần với cùng đầu vào để kiểm tra metadata byte và SHA-256 xác suất validation giống nhau. Eval nạp model đã lưu, kiểm tra checksum đầu vào đã chốt và eval, rồi tạo report bằng `FileMode.CreateNew` trước khi đọc/scoring eval. Khi report tồn tại, cả train và eval đều dừng; không xóa report để train hoặc đo lại. Artifact v1 trong repo đã được đo một lần, nên hai lệnh mặc định sẽ bảo vệ thí nghiệm đã chốt. Không dùng eval để quyết định cấu hình hay ngưỡng, kể cả trong một thư mục đầu ra khác.

Pipeline dùng lại đúng `RoleIntentDatasetGenerator.Normalize`, word n-gram 1–2 và ba bộ char n-gram riêng 2, 3, 4 (không có char unigram), ghép và chuẩn hóa L2. L-BFGS maximum entropy dùng seed 20261002, một luồng, tối đa 100 iteration; các option khác theo mặc định Microsoft.ML 4.0.3. Trọng số lớp là `N / (24 * số_mẫu_lớp)`. Score được chuẩn hóa toàn bộ nhãn hoặc mask theo catalog vai trò rồi chuẩn hóa lại; role không biết bị từ chối, `Chung` là hợp của sáu role. Dưới ngưỡng trả `không chắc`; tool không thực thi fallback luật. Caller runtime trong tương lai cần xử lý fallback, nhưng PR này chưa có tích hợp đó.

Phép toán SIMD đã cho sai khác nhỏ về trọng số/xác suất và hash trong kiểm tra lặp. Hai lệnh role-intent tự chạy lại trong tiến trình scalar với `DOTNET_EnableHWIntrinsic=0`, `DOTNET_TieredCompilation=0`; test host dùng `role-intent.runsettings` tương ứng. Project tool/test cũng tắt JIT phân tầng; project API không đổi. Thiết lập JIT theo [tài liệu .NET](https://learn.microsoft.com/en-us/dotnet/core/runtime-config/compilation). Tái lập byte được kiểm trên cùng Windows/.NET/ML.NET; chưa khẳng định hash giống giữa hệ điều hành hoặc CPU khác.

### Cấu hình thử trên validation

Các thử nghiệm SDCA ban đầu cũng chỉ đọc train/validation. Chúng bị loại vì không lặp lại đúng artifact/xác suất qua các tiến trình, dù top-1 và metric ổn định. Không có cấu hình SDCA nào được đo trên eval. Hai cấu hình L-BFGS đáp ứng kiểm tra tái lập được chọn bằng macro-F1 top-1 sau lọc vai trò; tie chọn không trọng số. Metadata ghi đầy đủ hai trial L-BFGS và mọi trial ngưỡng.

| Cấu hình | Accuracy không lọc | Macro-F1 không lọc | Accuracy lọc role | Macro-F1 lọc role | Quyết định |
|---|---:|---:|---:|---:|---|
| SDCA không trọng số, thử ban đầu | 49.12% | 0.4325 | 67.49% | 0.6305 | Loại: tái lập byte thất bại |
| SDCA trọng số lớp, thử ban đầu | 49.47% | 0.4285 | 67.49% | 0.6287 | Loại: tái lập byte thất bại |
| L-BFGS không trọng số | 42.40% | 0.3579 | 57.60% | 0.5318 | Không chọn |
| L-BFGS trọng số lớp | 44.88% | 0.3743 | 61.13% | 0.5546 | Chọn |

Ngưỡng cố định grid 0.00–0.95, bước 0.05, chọn coverage lớn nhất thỏa accepted accuracy ≥90% và ít nhất 29 câu validation; tie chọn ngưỡng thấp. Chọn **0.25**: 118/283 câu được giữ, coverage 41.70%, accepted accuracy 90.68%. Ở 0.20, 168 câu được giữ nhưng accepted accuracy chỉ 83.33%, không đạt tiêu chí. Đây là tiêu chí trên validation tổng hợp, không chứng minh xác suất đã calibrated trên người dùng thật.

### Số đo của cấu hình đã chốt

Accuracy, P/R/F1 và confusion matrix dùng top-1 trước abstention trên toàn bộ tập, không bỏ câu khó khỏi mẫu số. Accepted accuracy và tỷ lệ `không chắc` được báo riêng. Eval 240 câu được đo **một lần**, sau khi metadata/cấu hình/ngưỡng đã chốt; train 866 và validation 283 câu không thay đổi.

| Tập | N | Accuracy không lọc | Macro-F1 không lọc | Accuracy lọc role | Macro-F1 lọc role |
|---|---:|---:|---:|---:|---:|
| Train | 866 | 71.82% | 0.6778 | 80.83% | 0.7742 |
| Validation | 283 | 44.88% | 0.3743 | 61.13% | 0.5546 |
| Eval AI bổ sung | 240 | 57.08% | 0.5246 | 68.33% | 0.6524 |

Mỗi nhãn eval có support 10. Các ô dưới đây là P/R/F1:

| Nhãn | Không lọc | Lọc role |
|---|---|---|
| ActionRequest | 0.000/0.000/0.000 | 0.000/0.000/0.000 |
| AiHealth | 0.429/0.900/0.581 | 0.556/1.000/0.714 |
| ClinicKnowledge | 0.389/0.700/0.500 | 0.292/0.700/0.412 |
| DashboardMetrics | 0.357/0.500/0.417 | 0.667/0.600/0.632 |
| DiagnosticOrders | 0.667/1.000/0.800 | 0.833/1.000/0.909 |
| DoctorQueue | 0.000/0.000/0.000 | 0.000/0.000/0.000 |
| Greeting | 0.700/0.700/0.700 | 0.583/0.700/0.636 |
| Help | 0.667/0.400/0.500 | 0.444/0.400/0.421 |
| InventoryStatus | 1.000/0.200/0.333 | 1.000/0.700/0.824 |
| LookupAppointment | 0.600/0.900/0.720 | 0.818/0.900/0.857 |
| MyAppointments | 0.571/0.400/0.471 | 0.500/0.400/0.444 |
| MyBills | 0.667/0.200/0.308 | 0.833/0.500/0.625 |
| MyDiagnosticResults | 0.909/1.000/0.952 | 0.909/1.000/0.952 |
| MyPrescriptions | 0.529/0.900/0.667 | 0.909/1.000/0.952 |
| MyVisits | 0.636/0.700/0.667 | 0.700/0.700/0.700 |
| OutOfScope | 0.000/0.000/0.000 | 0.000/0.000/0.000 |
| PatientSummary | 0.636/0.700/0.667 | 0.778/0.700/0.737 |
| PrescriptionPayment | 0.538/0.700/0.609 | 0.727/0.800/0.762 |
| PrescriptionQueue | 0.700/0.700/0.700 | 0.769/1.000/0.870 |
| PrescriptionStatus | 0.545/0.600/0.571 | 0.900/0.900/0.900 |
| ReceptionQueue | 0.538/0.700/0.609 | 0.833/1.000/0.909 |
| StartBooking | 0.500/0.500/0.500 | 0.500/0.500/0.500 |
| TechnicianWorklist | 0.444/0.400/0.421 | 0.900/0.900/0.900 |
| TodayAppointments | 0.900/0.900/0.900 | 1.000/1.000/1.000 |

Yếu nhất: ActionRequest, DoctorQueue, OutOfScope đều F1=0 ở cả hai chế độ. ActionRequest→PrescriptionPayment: 1 câu; chiều ngược lại: 0, ở cả hai chế độ. Sau lọc role, các cặp nhầm lớn (3 câu/cặp): DoctorQueue→ClinicKnowledge, DoctorQueue→OutOfScope, MyAppointments→StartBooking, MyBills→MyVisits, StartBooking→ClinicKnowledge. Không lọc: InventoryStatus→MyPrescriptions, MyBills→MyVisits, StartBooking→ClinicKnowledge (3 câu/cặp). Report chứa hai ma trận 24×24, top 10 cặp và đầy đủ 103 câu sai không lọc / 76 câu sai sau lọc cùng text, nhãn thật, top-1, quyết định, confidence.

| Tập | Không chắc không lọc | Không chắc lọc role |
|---|---:|---:|
| Train | 83.14% (720/866) | 38.91% (337/866) |
| Validation | 91.87% (260/283) | 58.30% (165/283) |
| Eval | 88.75% (213/240) | 54.58% (131/240) |

Eval lọc role giữ 109/240 câu, accepted accuracy 90.83%; không lọc giữ 27/240, accepted accuracy 92.59%. Không diễn giải các số accepted accuracy này thành accuracy trên toàn bộ eval.

### Kiểm chứng và giới hạn

Build Release 0 lỗi. Sáu xUnit mới kiểm tra lặp seed/xác suất, metadata byte, save/load, role mask, abstention, chọn ngưỡng và bảo vệ eval; fixture train 72 câu gốc, validation 48 câu gốc, không có eval TSV trong test train/save/load. RoleIntentDatasetTests (8), RoleIntentEvalSetTests (6), GateBModelPipelineTests (11) cũng được chạy. Gate B như CI: registry IsValid=true, quyết định NOT_PROMOTED. SHA-256 của 20 file dữ liệu/model/metadata cũ khớp trước/sau.

Model mới SHA-256 `2462a7ec63bcd5a461c629840f2cd92aef8c78d41c1ae541a41f6714f68e30ba`; hash xác suất validation `6055a91b89645475e00b912eec68a196ba129f5a233d25b94c62e72bddaad1ac`. Metadata lưu hash train/validation/labels, catalog/score labels, seed, package, cấu hình, trial/ngưỡng và metric. ZIP dùng thứ tự entry và timestamp cố định.

Eval do AI Claude viết, cùng nguồn tổng hợp với seeds nên số đo có thể lạc quan; không có tập blind độc lập do người viết và chưa đo trên câu thật từ người dùng. Validation chứa biến thể tương quan trong cùng family; mỗi nhãn eval chỉ 10 câu nên P/R/F1 còn nhiều bất định. Ba nhãn F1=0 và hơn nửa eval bị abstain sau lọc cho thấy model này chưa đủ bằng chứng để triển khai. Xác suất chưa calibrated trên traffic thật. Model chưa nối runtime, chưa thay classifier luật và chưa chứng minh hiệu quả của fallback.
