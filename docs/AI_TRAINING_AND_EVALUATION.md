# Quy Trình Huấn Luyện & Đánh Giá Mô Hình AI (AI Training & Evaluation)

Tài liệu này hướng dẫn vận hành công cụ huấn luyện cục bộ **`ClinicManagement.AI.Training`**, cấu trúc tập dữ liệu triệu chứng → chuyên khoa, tiêu chuẩn kiểm định, chỉ số đánh giá thực tế và cơ chế quản trị an toàn mô hình học máy ML.NET.

---

## 1. Tổng Quan Kiến Trúc Pipeline

Dự án công cụ `src/tools/ClinicManagement.AI.Training` (.NET 10, `Microsoft.ML 4.0.3`) được thiết kế độc lập nhằm chuẩn hoá quy trình:
1. **Kiểm tra tính toàn vẹn của tập dữ liệu (Dataset Validation)**.
2. **Huấn luyện mô hình phân loại đa lớp (Multiclass Classification)** bằng giải thuật `SdcaMaximumEntropy`.
3. **Đánh giá và xuất báo cáo chỉ số khách quan (Evaluation & Honest Metrics Report)**.
4. **Đóng gói metadata an toàn (Model Governance Metadata)** với mã băm SHA-256, cờ `clinicallyValidated = false` cho mô hình demo, và biên bản thẩm định lâm sàng (`ClinicalApprovalManifest`).

---

## 2. Cấu Trúc Bản Ghi Dữ Liệu (Dataset Schema)

Tập dữ liệu lưu dưới định dạng JSON (`data/symptom_specialty_dataset.json`):

```json
{
  "caseId": "CASE-001",
  "text": "Tôi bị đau tức ngực trái khi đi bộ nhanh, hồi hộp đánh trống ngực",
  "primarySpecialtyCode": "SP06",
  "acceptableSpecialtyCodes": ["SP06", "SP01"],
  "urgency": "ROUTINE",
  "redFlags": [],
  "approved": true,
  "sourceType": "SIMULATED_TEST_DATA",
  "scenarioFamily": "CHEST_DISCOMFORT_1",
  "split": "train",
  "datasetVersion": "1.0.0"
}
```

### Các trường dữ liệu cốt lõi:
- `caseId`: Mã định danh ca bệnh duy nhất.
- `text`: Mô tả triệu chứng sức khỏe (bằng tiếng Việt).
- `primarySpecialtyCode`: Mã chuyên khoa chuẩn tắc (`SP01` đến `SP11`, không dùng dấu gạch ngang).
- `acceptableSpecialtyCodes`: Các mã chuyên khoa chấp nhận được (dùng cho tính Top-K accuracy).
- `approved`: Cờ duyệt bởi chuyên gia y tế (`true`/`false`).
- `sourceType`: Nguồn dữ liệu (`SIMULATED_TEST_DATA` cho dữ liệu giả lập thử nghiệm).
- `scenarioFamily`: Họ kịch bản bệnh học, dùng để phân tách tập dữ liệu mà không gây rò rỉ (leakage) giữa train và test.
- `split`: Phân chia tập (`train`, `test`).

---

## 3. Tiêu Chuẩn Kiểm Định Dữ Liệu (`DatasetValidator`)

Trước khi tiến hành huấn luyện, tập dữ liệu bắt buộc phải vượt qua các chốt chặn nghiêm ngặt:
1. **Kiểm tra trường bắt buộc**: Không để trống `caseId`, `text`, `primarySpecialtyCode`.
2. **Kiểm tra mã chuẩn tắc (Canonical Code Regex)**: Mã chuyên khoa phải khớp regex `^SP(0[1-9]|1[0-1])$`. Mọi mã lạ hoặc không thuộc danh mục phòng khám đều bị từ chối.
3. **Cách ly dữ liệu ngoài danh mục (Quarantine)**: Các ca bệnh thuộc chuyên khoa không có trong danh mục phòng khám (ví dụ: Nha khoa `CASE-017` và `CASE-018`) được cách ly sang file `data/quarantined_records.json`.
4. **Kiểm tra trùng lặp nội dung (Duplicate Text Check)**: Chuẩn hóa khoảng trắng và chữ thường để phát hiện các câu triệu chứng trùng lặp. Trùng lặp được coi là **LỖI NGHIÊM TRỌNG (ERROR)**, không thể bỏ qua.
5. **Kiểm tra rò rỉ phân tập (Leakage Check)**: Phát hiện nếu cùng một họ kịch bản `scenarioFamily` xuất hiện đồng thời ở cả tập `train` và tập `test`. Đây là **LỖI NGHIÊM TRỌNG (ERROR)**.
6. **Lọc bản ghi chưa duyệt (Unapproved Records)**: Tách và cảnh báo các bản ghi có `approved: false`. Chỉ các bản ghi đã duyệt mới được tham gia huấn luyện.
7. **Đo lường độ lệch lớp (Class Imbalance Ratio)**: Báo cáo tỷ lệ phân bố giữa chuyên khoa có nhiều mẫu nhất và ít mẫu nhất. Nếu tỷ lệ vượt quá 3.0:1, hệ thống phát cảnh báo `Class imbalance warning`.

---

## 4. Giải Thuật & Chỉ Số Đánh Giá Thực Tế (Honest Metrics)

- **Framework**: `Microsoft.ML 4.0.3` trên nền tảng .NET 10.
- **Trích xuất đặc trưng**: `TextFeaturizingEstimator` với chuẩn hóa n-gram tiếng Việt.
- **Thuật toán phân loại**: `SdcaMaximumEntropy` (Stochastic Dual Coordinate Ascent).
- **Phân tách dữ liệu**: Xác định theo họ kịch bản `scenarioFamily` với fixed seed `42` (tuyệt đối không dùng `Guid.NewGuid()`).
- **Chỉ số đánh giá trung thực trên tập Test độc lập (9 mẫu)**:
  - **Micro Accuracy**: **22.22%**
  - **Macro Accuracy**: **18.75%**
  - **Macro Precision**: **16.67%**
  - **Macro Recall**: **18.75%**
  - **Macro F1**: **17.50%**
  - **Top-3 Accuracy**: **66.67%**
  - **Log Loss**: **2.0171**
  - **ClinicallyValidated**: **`false`**

> ⚠️ **Tuyên bố Minh bạch Y tế:** Do tập dữ liệu mô phỏng còn nhỏ (18 ca bệnh, 9 ca huấn luyện, 9 ca kiểm thử trên 8 chuyên khoa), mô hình hiện tại đạt độ chính xác Micro Accuracy ~22.2% và Macro F1 ~17.5%. Báo cáo kỹ thuật tuyệt đối không thổi phồng chỉ số và khẳng định mô hình này chỉ phục vụ mục đích trình diễn kỹ thuật (Demo), chưa đủ điều kiện triển khai lâm sàng.

---

## 5. Hướng Dẫn Vận Hành CLI

### 5.1. Kiểm tra toàn vẹn dữ liệu:
```powershell
dotnet run --project src/tools/ClinicManagement.AI.Training -- --validate src/tools/ClinicManagement.AI.Training/data/symptom_specialty_dataset.json
```

### 5.2. Huấn luyện và đánh giá mô hình:
```powershell
dotnet run --project src/tools/ClinicManagement.AI.Training -- --train --data src/tools/ClinicManagement.AI.Training/data/symptom_specialty_dataset.json --out src/tools/ClinicManagement.AI.Training/models --allow-demo-data
```

### 5.3. File kết quả đầu ra:
- `specialty_classifier_v1.zip`: Mô hình ML.NET nhị phân.
- `model_metadata.json`: Metadata kỹ thuật và quản trị an toàn.

---

## 6. Cơ Chế Quản Trị Mô Hình Tại Backend (`MlNetSpecialtyClassifier`)

Tại `ClinicManagement.Infrastructure`:
1. Khi ứng dụng khởi động, nếu `AiClassifier:Enabled = true`, dịch vụ sẽ đọc file `model_metadata.json` cùng thư mục với file `.zip`.
2. Kiểm tra thuộc tính `clinicallyValidated`.
3. Nếu `clinicallyValidated == false` và cấu hình hệ thống yêu cầu `RequireClinicallyValidated = true`:
   - Dịch vụ phát cảnh báo an toàn: *"ML.NET Classifier model is NOT clinically validated. Rejecting demo model load."*
   - Bộ phân loại tự động ở trạng thái ngủ an toàn (trả về `null`), chuyển tiếp xử lý cho các quy tắc DB grounding và LLM mà không làm gián đoạn hay sập ứng dụng.
