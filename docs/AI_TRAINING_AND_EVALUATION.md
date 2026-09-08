# Quy Trình Huấn Luyện & Đánh Giá Mô Hình AI (AI Training & Evaluation)

Tài liệu này hướng dẫn vận hành công cụ huấn luyện cục bộ **`ClinicManagement.AI.Training`**, cấu trúc tập dữ liệu triệu chứng → chuyên khoa, tiêu chuẩn kiểm định và cơ chế quản trị an toàn mô hình học máy ML.NET.

---

## 1. Tổng Quan Kiến Trúc Pipeline

Dự án công cụ `src/tools/ClinicManagement.AI.Training` (.NET 10, `Microsoft.ML 4.0.3`) được thiết kế độc lập nhằm chuẩn hoá quy trình:
1. **Kiểm tra tính toàn vẹn của tập dữ liệu (Dataset Validation)**.
2. **Huấn luyện mô hình phân loại đa lớp (Multiclass Classification)** bằng giải thuật `SdcaMaximumEntropy`.
3. **Đánh giá và xuất báo cáo chỉ số khách quan (Evaluation & Metrics Report)**.
4. **Đóng gói metadata an toàn (Model Governance Metadata)** với mã băm SHA-256 và cờ `clinicallyValidated`.

---

## 2. Cấu Trúc Bản Ghi Dữ Liệu (Dataset Schema)

Tập dữ liệu lưu dưới định dạng JSON (`data/symptom_specialty_dataset.json`):

```json
{
  "caseId": "CASE-CARDIO-001",
  "text": "Tôi bị đau tức ngực trái lan lên cằm và vai, hay mệt khi gắng sức.",
  "primarySpecialtyCode": "SP-01",
  "acceptableSpecialtyCodes": ["SP-01", "SP-02"],
  "urgency": "ROUTINE",
  "redFlags": [],
  "approved": true,
  "sourceType": "SIMULATED_TEST_DATA",
  "scenarioFamily": "CHEST_DISCOMFORT_EXERTIONAL",
  "split": "train",
  "datasetVersion": "1.0.0"
}
```

### Các trường dữ liệu cốt lõi:
- `caseId`: Mã định danh ca bệnh duy nhất.
- `text`: Mô tả triệu chứng sức khỏe (bằng tiếng Việt).
- `primarySpecialtyCode`: Mã chuyên khoa chính quy (VD: `SP-01` Tim mạch, `SP-03` Da liễu).
- `acceptableSpecialtyCodes`: Các mã chuyên khoa chấp nhận được (cho đánh giá Top-K).
- `approved`: Cờ duyệt bởi chuyên gia y tế (`true`/`false`).
- `sourceType`: Nguồn dữ liệu (`CLINICAL_GROUND_TRUTH` hoặc `SIMULATED_TEST_DATA`).
- `scenarioFamily`: Họ kịch bản bệnh học, dùng để chống rò rỉ dữ liệu (leakage) giữa train và test.
- `split`: Phân chia tập (`train`, `val`, `test`).

---

## 3. Tiêu Chuẩn Kiểm Định Dữ Liệu (`DatasetValidator`)

Trước khi tiến hành huấn luyện, tập dữ liệu bắt buộc phải vượt qua các chốt chặn:
1. **Kiểm tra trường bắt buộc**: Không để trống `caseId`, `text`, `primarySpecialtyCode`.
2. **Kiểm tra trùng lặp nội dung (Text Duplicate Check)**: Chuẩn hóa khoảng trắng và chữ thường để phát hiện các câu triệu chứng trùng nhau giữa các ca bệnh.
3. **Kiểm tra rò rỉ phân tập (Train-Test Split Leakage Check)**: Phát hiện nếu cùng một họ kịch bản `scenarioFamily` xuất hiện đồng thời ở cả tập `train` và tập `test`.
4. **Lọc bản ghi chưa duyệt (Unapproved Records Filter)**: Tự động tách và cảnh báo các bản ghi có `approved: false`. Chỉ các bản ghi đã được phê duyệt mới được nạp vào pipeline huấn luyện.
5. **Đo lường độ lệch lớp (Class Imbalance Ratio)**: Báo cáo tỷ lệ phân bố giữa chuyên khoa có nhiều mẫu nhất và ít mẫu nhất. Nếu tỷ lệ vượt quá 3.0:1, hệ thống phát cảnh báo `Class imbalance warning`.

---

## 4. Giải Thuật & Cấu Hình Huấn Luyện

- **Framework**: `Microsoft.ML 4.0.3` trên nền tảng .NET 10.
- **Trích xuất đặc trưng**: `TextFeaturizingEstimator` với chuẩn hóa n-gram tiếng Việt.
- **Thuật toán phân loại**: `SdcaMaximumEntropy` (Stochastic Dual Coordinate Ascent).
- **Tính lặp lại xác định (Determinism)**: Khởi tạo với `seed: 42` đảm bảo kết quả huấn luyện hoàn toàn tái lập được qua các lần chạy.
- **Cờ bắt buộc**: Huấn luyện với dữ liệu mô phỏng bắt buộc phải truyền cờ `--allow-demo-data`. Nếu không có cờ này, công cụ sẽ từ chối chạy để tránh vô tình huấn luyện mô hình chưa kiểm chứng y khoa.

---

## 5. Hướng Dẫn Sử Dụng Công Cụ CLI

### Chạy kiểm tra dữ liệu:
```powershell
dotnet run --project src/tools/ClinicManagement.AI.Training -- --validate
```

### Chạy huấn luyện và đánh giá:
```powershell
dotnet run --project src/tools/ClinicManagement.AI.Training -- --train --allow-demo-data
```

### Kết quả đầu ra (Output Artifacts):
- `specialty_classifier_v1.zip`: File nhị phân mô hình ML.NET.
- `model_metadata.json`: File chứa chỉ số đánh giá, mã băm dữ liệu và cờ xác thực.

### Ví dụ `model_metadata.json`:
```json
{
  "modelVersion": "1.0.0",
  "datasetVersion": "1.0.0",
  "datasetHashSha256": "4b68e92c0199e8210fe5f06d6daea739f408990d7be9fdbfdfa5ba30b5037ae1",
  "trainedAtUtc": "2026-09-08T08:35:12Z",
  "clinicallyValidated": false,
  "specialtyCodes": ["SP-01", "SP-02", "SP-03", "SP-04", "SP-05", "SP-06", "SP-07", "SP-08", "SP-09", "SP-10"],
  "metrics": {
    "microAccuracy": 0.85,
    "macroAccuracy": 0.80,
    "logLoss": 0.62,
    "macroF1": 0.78,
    "macroPrecision": 0.82,
    "macroRecall": 0.76,
    "confusionMatrix": "..."
  }
}
```

---

## 6. Cơ Chế Kiểm Soát Nạp Mô Hình Ở Backend (`MlNetSpecialtyClassifier`)

Tại `ClinicManagement.Infrastructure`:
1. Khi ứng dụng khởi động, nếu `AiClassifier:Enabled = true`, dịch vụ sẽ đọc file `model_metadata.json` cùng thư mục với file `.zip`.
2. Kiểm tra thuộc tính `clinicallyValidated`.
3. Nếu `clinicallyValidated == false` và cấu hình hệ thống yêu cầu `RequireClinicallyValidated = true`:
   - Dịch vụ phát cảnh báo an toàn: *"ML.NET Classifier model is NOT clinically validated. Rejecting demo model load."*
   - Bộ phân loại tự động ở trạng thái ngủ an toàn (trả về `null`), chuyển tiếp xử lý cho các quy tắc DB grounding và LLM mà không làm gián đoạn hay sập ứng dụng.
