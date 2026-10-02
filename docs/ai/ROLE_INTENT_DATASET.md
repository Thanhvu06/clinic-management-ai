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
