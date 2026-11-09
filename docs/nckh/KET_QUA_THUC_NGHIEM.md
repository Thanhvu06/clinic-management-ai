# Tổng hợp kết quả thực nghiệm NCKH — ClinicCare AI

Tài liệu này tổng hợp toàn bộ số liệu thực nghiệm của hệ thống trợ lý AI và mô-đun hỏi đáp tri thức phòng khám (RAG cục bộ) trong dự án **ClinicCare AI**. Mọi số liệu được trích dẫn trực tiếp từ các báo cáo thực nghiệm đã lưu trong kho mã nguồn (`docs/ai/HIDDEN_EVAL.md`, `docs/ai/RAG_EVALUATION.md`) hoặc từ các phép đo trực tiếp trên máy thật trong phiên làm việc này, tuyệt đối không ước lượng và không làm tròn số.

---

## 1. Cấu hình máy đo thực nghiệm

Các phép đo cục bộ trong phiên làm việc này (mục 4 và mục 5) được thực hiện trên cùng một máy trạm cá nhân (laptop) với thông số phần cứng và phần mềm như sau:

| Thành phần | Thông số chi tiết |
|---|---|
| CPU | AMD Ryzen 9 7945HX with Radeon Graphics |
| GPU rời (xử lý LLM/Embedding) | NVIDIA GeForce RTX 4060 Laptop GPU (`8188 MiB` VRAM, Driver `566.07` / `617.42`) |
| GPU tích hợp | AMD Radeon(TM) 610M (`0,5 GiB`) |
| RAM hệ thống | `31,19 GiB` (`33.487.876.096` byte) |
| Hệ điều hành | Windows (PowerShell / .NET runtime cục bộ) |
| .NET SDK | `10.0.300` (`net10.0`) |
| Ollama runtime | `0.40.15` (`http://127.0.0.1:11434`) |
| Mô hình sinh câu trả lời (Chat LLM) | `qwen2.5:7b` (`4,7 GB` trên đĩa; `4,8 GB` khi nạp `100% GPU`, context `4096`) |
| Mô hình nhúng (Embedding) | `bge-m3:latest` (`1,2 GB` trên đĩa; `689 MB` khi nạp `100% GPU`, context `4096`) |

---

## 2. Kết quả phân loại ý định trên bộ câu hỏi ẩn do người viết (`HIDDEN_EVAL.md`)

- **Nguồn số liệu:** `docs/ai/HIDDEN_EVAL.md`.
- **Thông tin bộ đề ẩn:** Viết tay bởi Nguyễn Thanh Vũ, 2026-10-18, không có AI hỗ trợ; nằm ngoài repo.
- **Ngày đo:** Không ghi trong nguồn.
- **Cấu hình máy:** Không ghi trong nguồn (chạy qua lệnh `ClinicManagement.AI.Training --hidden-eval`).
- **Phân định rõ ràng giữa hai tầng đánh giá:**
  - **T1 (`RoleIntentModel`):** Đánh giá mô hình học máy ML.NET phân loại ý định theo vai trò.
  - **T2 (`HybridIntentRouter`):** Đánh giá toàn bộ đường ống định tuyến nghiệp vụ kết hợp (rule, model ML.NET, ngữ cảnh tài nguyên, fallback). Số liệu T2 là độ chính xác nhãn định tuyến, **không phải là accuracy của model ML.NET**.

### 2.1. Lần chấm 1 (T2 theo cách cũ)

- **Nguồn file:** `docs/ai/HIDDEN_EVAL.md` (dòng 22–24).
- **Ngày viết đề:** 2026-10-18 (Viết tay bởi Nguyễn Thanh Vũ, không có AI hỗ trợ; nằm ngoài repo). Ngày đo: không ghi trong nguồn.
- **Chép nguyên dòng "Tổng" từ bảng accuracy tương ứng (`docs/ai/HIDDEN_EVAL.md:24`):**

| Phạm vi | N | T1 đúng | T1 accuracy | T1 macro-F1 | Coverage | Accepted accuracy | Không chắc | T1 Wilson 95% | T2 đúng | T2 accuracy | T2 macro-F1 | T2 Wilson 95% |
|---|---:|---:|---:|---:|---:|---:|---:|---|---:|---:|---:|---|
| Tổng | 105 | 86 | 81.90% | 82.24% | 58.10% | 91.80% | 41.90% | [73.46%, 88.10%] | 61 | 58.10% | 53.05% | [48.54%, 67.08%] |

*Ghi chú:* Ở Lần chấm 1, model ML.NET T1 đạt accuracy **81.90%** (86/105) và macro-F1 **82.24%**, Wilson 95% là **[73.46%, 88.10%]**. Định tuyến T2 đạt **58.10%** (61/105). Tuyệt đối không nhầm lẫn 58.10% là accuracy của model.

### 2.2. Lần chấm 2 (sau sửa router)

- **Nguồn file:** `docs/ai/HIDDEN_EVAL.md` (dòng 227–229).
- **Ngày viết đề:** 2026-10-18 (Viết tay bởi Nguyễn Thanh Vũ, không có AI hỗ trợ; nằm ngoài repo). Ngày đo: không ghi trong nguồn.
- **Chép nguyên dòng "Tổng" từ bảng accuracy tương ứng (`docs/ai/HIDDEN_EVAL.md:229`):**

| Phạm vi | N | T1 đúng | T1 accuracy | T1 macro-F1 | Coverage | Accepted accuracy | Không chắc | T1 Wilson 95% | Hỏi lại | T2 strict đúng | T2 strict accuracy | T2 strict macro-F1 | T2 strict Wilson 95% | T2 lenient đúng | T2 lenient accuracy | T2 lenient Wilson 95% |
|---|---:|---:|---:|---:|---:|---:|---:|---|---:|---:|---:|---:|---|---:|---:|---|
| Tổng | 105 | 86 | 81.90% | 82.24% | 58.10% | 91.80% | 41.90% | [73.46%, 88.10%] | 23 | 43 | 40.95% | 42.80% | [32.03%, 50.52%] | 66 | 62.86% | [53.31%, 71.49%] |

### 2.3. Lần chấm 3 (model v3, dữ liệu người viết)

- **Nguồn file:** `docs/ai/HIDDEN_EVAL.md` (dòng 411–432).
- **Ngày viết đề:** 2026-10-18 (Viết tay bởi Nguyễn Thanh Vũ, không có AI hỗ trợ; nằm ngoài repo). Ngày đo: không ghi trong nguồn.

#### T1: So sánh model v2 và model v3 (dòng 411–420)

| Model | Vai trò | N | Đúng | Accuracy | Macro-F1 | Accepted | Coverage | Accepted accuracy | Không chắc | Wilson 95% |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---|
| v2 | Tổng | 105 | 86 | 81.90% | 82.24% | 61 | 58.10% | 91.80% | 41.90% | [73.46%, 88.10%] |
| v3 | Tổng | 105 | 95 | 90.48% | 90.88% | 101 | 96.19% | 92.08% | 3.81% | [83.35%, 94.74%] |

#### T2: Định tuyến runtime đã chọn (dòng 430–432)

| Vai trò | N | Hỏi lại | Strict đúng | Strict accuracy | Strict macro-F1 | Strict Wilson 95% | Lenient đúng | Lenient accuracy | Lenient Wilson 95% | rule | model | fallback | override |
|---|---:|---:|---:|---:|---:|---|---:|---:|---|---:|---:|---:|---:|
| Tổng | 105 | 24 | 57 | 54.29% | 52.89% | [44.77%, 63.49%] | 81 | 77.14% | [68.24%, 84.13%] | 58 | 31 | 10 | 6 |

---

## 3. Kết quả truy xuất tri thức RAG trên bộ câu hỏi phát triển (`RAG_EVALUATION.md`)

- **Nguồn file:** `docs/ai/RAG_EVALUATION.md`.
- **Ngày đo:** Đo ngày 07/11/2026 (giờ Việt Nam, dòng 5) và cập nhật đo chốt ngày 08/11/2026 (dòng 345, 351–368).
- **Cấu hình máy:** Windows, AMD Ryzen 9 7945HX, RAM khả dụng báo bởi hệ thống 31,2 GiB, NVIDIA GeForce RTX 4060 Laptop GPU 8 GiB, .NET SDK 10.0.300, Ollama 0.40.15 (dòng 5).
- **Tham số đánh giá:** MinScore = 0.45; MinQueryCoverage = 0.50; MinTermIdf = 1.25; MinCosine = 0.20; TopK = 4 (dòng 166, 408, 527).

### 3.1. Bảng đo chốt sau sửa điều hướng (trích nguyên văn `docs/ai/RAG_EVALUATION.md:355-368`)

| Bộ | Chế độ | Trong/ngoài | Hit@1 | Hit@3 | MRR | Từ chối đúng | TB ms | p95 ms |
|---|---|---:|---:|---:|---:|---:|---:|---:|
| Mốc | bm25 | 156/60 | 89.10% | 97.44% | 0.9300 | 95.00% | 0.553 | 0.326 |
| Mốc | hybrid | 156/60 | 91.67% | 97.44% | 0.9434 | 95.00% | 15.889 | 46.762 |
| Mốc | end-to-end | 156/60 | 80.13% | 86.54% | 0.8317 | 98.33% | 4.294 | 7.805 |
| Mốc | end-to-end-hybrid | 156/60 | 81.41% | 86.54% | 0.8387 | 98.33% | 16.956 | 26.059 |
| V2 | bm25 | 36/10 | 97.22% | 100.00% | 0.9861 | 90.00% | 1.055 | 0.678 |
| V2 | hybrid | 36/10 | 97.22% | 100.00% | 0.9861 | 90.00% | 21.721 | 30.616 |
| V2 | end-to-end | 36/10 | 86.11% | 88.89% | 0.8750 | 90.00% | 7.101 | 15.767 |
| V2 | end-to-end-hybrid | 36/10 | 86.11% | 88.89% | 0.8750 | 90.00% | 26.098 | 43.157 |
| Gộp | bm25 | 192/70 | 90.62% | 97.92% | 0.9405 | 94.29% | 0.505 | 0.346 |
| Gộp | hybrid | 192/70 | 92.71% | 97.92% | 0.9514 | 94.29% | 12.880 | 21.503 |
| Gộp | end-to-end | 192/70 | 81.25% | 86.98% | 0.8398 | 97.14% | 4.590 | 5.534 |
| Gộp | end-to-end-hybrid | 192/70 | 82.29% | 86.98% | 0.8455 | 97.14% | 19.216 | 30.189 |

### 3.2. Bộ câu hỏi ẩn RAG (nhóm viết)

Bảng dưới đây được chừa sẵn để nhóm trưởng tự chạy trên file CSV xuất từ Excel (đặt ngoài repo) và điền số liệu. Công cụ `ClinicManagement.AI.RagEval` ở chế độ `--csv` chỉ in ra số liệu tổng hợp (`N`, `inScope`, `outside`, `hitAt1`, `hitAt3`, `mrr`, `correctRejection`, `averageMs`, `p95Ms`), tuyệt đối không in và không ghi nội dung câu hỏi ra màn hình hay ra file JSON.

- **Nguồn file:** File CSV bộ câu hỏi ẩn RAG do nhóm xuất từ Excel (đặt ngoài repo).
- **Ngày đo:** chưa đo.
- **Cấu hình máy:** chưa đo.

**Lệnh chạy để nhóm trưởng copy và thực thi:**

```powershell
# Chế độ 1: BM25 thuần (không cần Ollama)
dotnet run --project src/tools/ClinicManagement.AI.RagEval -- bm25 "D:\ket-qua-rag-an-bm25.json" --csv "D:\bo-cau-hoi-an-rag.csv"

# Chế độ 2: Hybrid BM25 + bge-m3 (cần bật Ollama)
dotnet run --project src/tools/ClinicManagement.AI.RagEval -- hybrid "D:\ket-qua-rag-an-hybrid.json" --csv "D:\bo-cau-hoi-an-rag.csv"

# Chế độ 3: End-to-end Gate + BM25 (không cần Ollama)
dotnet run --project src/tools/ClinicManagement.AI.RagEval -- end-to-end "D:\ket-qua-rag-an-e2e.json" --csv "D:\bo-cau-hoi-an-rag.csv"

# Chế độ 4: End-to-end Gate + Hybrid BM25 + bge-m3 (cần bật Ollama)
dotnet run --project src/tools/ClinicManagement.AI.RagEval -- end-to-end-hybrid "D:\ket-qua-rag-an-e2e-hybrid.json" --csv "D:\bo-cau-hoi-an-rag.csv"
```

| Chế độ | Tổng số câu (`N`) | Trong phạm vi | Ngoài phạm vi | Hit@1 | Hit@3 | MRR | Từ chối đúng | TB (ms) | p95 (ms) |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| `bm25` | chưa đo | chưa đo | chưa đo | chưa đo | chưa đo | chưa đo | chưa đo | chưa đo | chưa đo |
| `hybrid` | chưa đo | chưa đo | chưa đo | chưa đo | chưa đo | chưa đo | chưa đo | chưa đo | chưa đo |
| `end-to-end` | chưa đo | chưa đo | chưa đo | chưa đo | chưa đo | chưa đo | chưa đo | chưa đo | chưa đo |
| `end-to-end-hybrid` | chưa đo | chưa đo | chưa đo | chưa đo | chưa đo | chưa đo | chưa đo | chưa đo | chưa đo |

---

## 4. Kết quả đo thời gian trả lời thực tế trên laptop (`POST /api/v1/ai/chat`)

- **Nguồn dữ liệu câu hỏi:** 20 câu hỏi trong phạm vi thuộc bộ phát triển `src/backend/ClinicManagement.Infrastructure/AI/Knowledge/Eval/rag-dev-questions-v2.json` (tuyệt đối không dùng bộ câu hỏi ẩn), mỗi câu gọi 3 lượt liên tiếp (`20 câu × 3 lượt = 60 request` cho mỗi chế độ) qua endpoint thật `POST http://localhost:5258/api/v1/ai/chat`.
- **Thời điểm đo:** Phiên làm việc ngày 09/11/2026 (`2026-11-09T06:45:52.4498364+00:00`).
- **Cấu hình máy:** AMD Ryzen 9 7945HX, RAM `31,19 GiB`, GPU NVIDIA GeForce RTX 4060 Laptop GPU (`8188 MiB` VRAM), Ollama `0.40.15` (`qwen2.5:7b` + `bge-m3:latest`), .NET SDK `10.0.300`.
- **Phương pháp tách Cold / Warm:** Trước lượt gọi đầu tiên của chế độ bật Ollama, mô hình `qwen2.5:7b` được giải phóng khỏi bộ nhớ (`keep_alive: 0`) để đo chính xác độ trễ câu đầu tiên khi phải nạp mô hình (`Cold`, `N = 1`) tách biệt với `59` lượt gọi tiếp theo khi mô hình đã sẵn sàng trên GPU (`Warm`, `N = 59`). Tương tự ở chế độ tắt Ollama (`Rag:IsEnabled = false`), câu đầu tiên sau khi khởi động tiến trình backend được ghi riêng (`Cold`, `N = 1`) và `59` lượt tiếp theo được tổng hợp vào nhóm `Warm` (`N = 59`).

### 4.1. Bảng độ trễ End-to-End có Ollama (`qwen2.5:7b`) và khi tắt Ollama (Fallback trích đoạn)

| Chế độ vận hành | Nhóm lượt gọi | Số lượt (`N`) | Cold / Lượt đầu (ms) | TB / Mean (ms) | Min (ms) | p50 (ms) | p95 (ms) | Max (ms) | Phân bố `dialogueOutcome` |
|---|---|---:|---:|---:|---:|---:|---:|---:|---|
| Bật Ollama (`qwen2.5:7b` + `bge-m3:latest`) | Câu đầu sau khởi động (`Cold`) | 1 | 4803,73 | 4803,73 | 4803,73 | 4803,73 | 4803,73 | 4803,73 | `KnowledgeAnswer: 1` |
| Bật Ollama (`qwen2.5:7b` + `bge-m3:latest`) | Các câu sau (`Warm`) | 59 | — | 660,68 | 4,54 | 623,63 | 1399,13 | 1522,93 | `KnowledgeAnswer: 55`, `OutOfScopeMedicalRequest: 3`, `MEDICAL_PRESCRIPTION_OUT_OF_SCOPE: 1` |
| Bật Ollama (`qwen2.5:7b` + `bge-m3:latest`) | Toàn bộ (`All`) | 60 | 4803,73 | 729,73 | 4,54 | 623,63 | 1399,13 | 4803,73 | `KnowledgeAnswer: 56`, `OutOfScopeMedicalRequest: 3`, `MEDICAL_PRESCRIPTION_OUT_OF_SCOPE: 1` |
| Tắt Ollama (Fallback trích đoạn `KnowledgeExtractive`) | Câu đầu sau khởi động (`Cold`) | 1 | 373,11 | 373,11 | 373,11 | 373,11 | 373,11 | 373,11 | `KnowledgeExtractive: 1` |
| Tắt Ollama (Fallback trích đoạn `KnowledgeExtractive`) | Các câu sau (`Warm`) | 59 | — | 15,85 | 3,02 | 13,39 | 39,12 | 64,62 | `KnowledgeExtractive: 56`, `OutOfScopeMedicalRequest: 3` |
| Tắt Ollama (Fallback trích đoạn `KnowledgeExtractive`) | Toàn bộ (`All`) | 60 | 373,11 | 21,81 | 3,02 | 13,39 | 39,12 | 373,11 | `KnowledgeExtractive: 57`, `OutOfScopeMedicalRequest: 3` |

### 4.2. Bảng mức tiêu thụ bộ nhớ RAM và VRAM khi chạy thực nghiệm

- **Nguồn đo:** Đọc trực tiếp từ tiến trình hệ điều hành (`WorkingSet64`, `PrivateMemorySize64`), lệnh `ollama ps` và `nvidia-smi` ngay sau khi hoàn tất 60 lượt gọi ở mỗi chế độ trong phiên đo ngày 09/11/2026.
- **Thời điểm đo:** `2026-11-09T06:45:52.4498364+00:00`.
- **Cấu hình máy:** AMD Ryzen 9 7945HX, RAM `31,19 GiB`, NVIDIA GeForce RTX 4060 Laptop GPU (`8188 MiB` VRAM).

| Chế độ vận hành | Tiến trình / Thành phần | RAM Working Set (MiB) | RAM Private Memory (MiB) | VRAM GPU sử dụng | Ghi chú từ `ollama ps` / `nvidia-smi` |
|---|---|---:|---:|---|---|
| Bật Ollama (`qwen2.5:7b` + `bge-m3:latest`) | Backend (`ClinicManagement.Api`) | 259,52 | 134,52 | — | Đã nạp chỉ mục chunk trong bộ nhớ |
| Bật Ollama (`qwen2.5:7b` + `bge-m3:latest`) | Ollama (`ollama` + `llama-server`) | 2271,72 | 8132,28 | `5426 MiB / 8188 MiB` (toàn GPU) | `qwen2.5:7b`: `4,8 GB` (`100% GPU`, context `4096`); `bge-m3:latest`: `689 MB` (`100% GPU`, context `4096`) |
| Tắt Ollama (Fallback trích đoạn) | Backend (`ClinicManagement.Api`) | 245,23 | 145,19 | — | Chỉ dùng BM25 + ghép đoạn trích định sẵn (`DeterministicFallback`) |

---

## 5. Kết quả đo che thông tin cá nhân (`KnowledgePrivacyMask`)

- **Nguồn file test:** `src/backend/ClinicManagement.IntegrationTests/RagEvalCsvAndPrivacyTests.cs` (`GetSyntheticPrivacyDataset`, `Synthetic_privacy_mask_dataset_has_at_least_60_questions_and_achieves_expected_precision_and_recall`).
- **Đối tượng được kiểm tra:** Lớp `src/backend/ClinicManagement.Infrastructure/AI/Knowledge/KnowledgePrivacyMask.cs` (giữ nguyên toàn bộ biểu thức chính quy hiện có, không chỉnh sửa logic).
- **Ngày đo:** Phiên làm việc ngày 09/11/2026 (chạy trực tiếp trong bộ kiểm thử tích hợp xUnit trên `.NET 10.0`).
- **Cấu hình máy:** AMD Ryzen 9 7945HX, RAM `31,19 GiB`, .NET SDK `10.0.300`.
- **Cấu trúc bộ dữ liệu giả (`70` câu tổng hợp):**
  - `55` câu có chứa thông tin định danh cá nhân (PII) gồm 4 nhóm: số điện thoại nhiều định dạng (`16` câu), CCCD 12 số (`12` câu), địa chỉ email (`12` câu), mã bệnh nhân / lịch hẹn / hồ sơ `BN`, `APT`, `APPT`, `PAT`, `MRN`, `HS`, `LH`, `PK`, `RX` và ngữ cảnh mã (`15` câu).
  - `15` câu hỏi y tế/hành chính tổng quát không chứa thông tin cá nhân (chứa các con số thông thường như số giờ nhịn ăn `8 đến 12 giờ`, lượng nước `0,5 đến 1 lít`, số cấp cứu `115`, huyết áp `120/80 mmHg`, số phòng khám `PK-102`, nhóm tuổi chung `60 tuổi`) để đo tỷ lệ báo nhầm (False Positive).

| Nhóm câu hỏi thử nghiệm | Số câu (`N`) | Có PII thực tế | Che đúng (`TP`) | Lọt thông tin (`FN`) | Báo nhầm (`FP`) | Giữ nguyên đúng (`TN`) | Precision | Recall |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| Số điện thoại nhiều định dạng (`Phone`) | 16 | 16 | 15 | 1 | 0 | 0 | 100,00% | 93,75% |
| Căn cước công dân 12 số (`CCCD`) | 12 | 12 | 12 | 0 | 0 | 0 | 100,00% | 100,00% |
| Địa chỉ thư điện tử (`Email`) | 12 | 12 | 12 | 0 | 0 | 0 | 100,00% | 100,00% |
| Mã bệnh nhân / lịch hẹn / hồ sơ (`BN`/`APT`/...) | 15 | 15 | 15 | 0 | 0 | 0 | 100,00% | 100,00% |
| Câu hỏi không có PII (`Negative`) | 15 | 0 | 0 | 0 | 2 | 13 | — | — |
| **Tổng hợp toàn bộ bộ test** | **70** | **55** | **54** | **1** | **2** | **13** | **96,43%** (`54/56`) | **98,18%** (`54/55`) |

- **Chi tiết 1 trường hợp lọt (`FN = 1`):** Câu số 16 (`"Trường hợp khách ghi số ngăn bằng dấu gạch chéo 0901/234/567 trên ghi chú."`) dùng ký tự `/` làm dấu phân cách giữa các cụm số điện thoại; biểu thức chính quy `Phone` và `LongNumber` hiện chỉ cho phép khoảng trắng, dấu chấm, dấu gạch ngang và dấu ngoặc đơn (`[\s().-]*`) nên không nhận diện được chuỗi `0901/234/567`.
- **Chi tiết 2 trường hợp báo nhầm (`FP = 2`):**
  1. Câu số 69 (`"Phòng khám nội tổng quát nằm ở phòng PK-102 tầng 1 phải không?"`): ký hiệu số phòng `PK-102` trùng tiền tố `PK` của mã phiếu khám trong biểu thức `RecordCode` nên bị thay bằng `[ĐÃ CHE]`.
  2. Câu số 70 (`"Người trên 60 tuổi đi khám sức khỏe định kỳ cần nhịn ăn bao lâu?"`): cụm từ chỉ nhóm tuổi chung `60 tuổi` khớp biểu thức `Age` (`\b\d{1,3}\s*(?:tuổi|tuoi)\b`) nên bị che dù không gắn với cá nhân cụ thể.

---

## 6. Hạn chế

1. **Phân biệt rạch ròi giữa độ chính xác mô hình học máy ML.NET (T1) và độ chính xác định tuyến toàn hệ thống (T2):**
   - Độ chính xác của riêng model ML.NET (`RoleIntentModel`) ở Lần chấm 1 đạt **81.90%** (86/105) và Lần chấm 3 đạt **90.48%** (95/105).
   - Tỷ lệ **58.10%** (61/105) ở Lần chấm 1 và **54.29%** (57/105 strict) ở Lần chấm 3 là độ chính xác nhãn định tuyến cuối cùng của toàn hệ thống `HybridIntentRouter` (khi kết hợp bộ quy tắc, kiểm tra ngữ cảnh tài nguyên, bộ lọc an toàn và fallback khi thiếu context). Tuyệt đối không được gọi 58.10% hay 54.29% là accuracy của model ML.NET.
   - Nguyên nhân định tuyến T2 strict thấp hơn T1 model là do câu hỏi trong đề ẩn không truyền kèm mã hồ sơ/lịch hẹn trong ngữ cảnh màn hình (`CurrentRoute` / `ResourceContext`), buộc router phải trả về câu hỏi làm rõ (`Clarification`, 24 câu ở Lần 3) thay vì tự ý gọi công cụ.
2. **Hiện tượng thời gian trung bình (TB) lớn hơn phân vị thứ 95 (`p95`) trong các phép đo truy xuất RAG:**
   - Trong bảng kết quả `RAG_EVALUATION.md:20` và `docs/ai/RAG_EVALUATION.md:355-368` (ví dụ Mốc `bm25`: TB 0.553 ms > p95 0.326 ms; V2 `bm25`: TB 1.055 ms > p95 0.678 ms; Gộp `bm25`: TB 0.505 ms > p95 0.346 ms), giá trị trung bình cộng cao hơn p95.
   - Nguyên nhân đã được ghi rõ tại `docs/ai/RAG_EVALUATION.md:20`: *"p95 hybrid thấp hơn trung bình do một số request chậm ở đuôi phân phối; các số được ghi nguyên theo phép đo."* Trên các tập mẫu, tuyệt đại đa số truy vấn hoàn tất cực nhanh dưới mili-giây, nhưng một vài câu đầu tiên kích hoạt nạp đối tượng hoặc ngoại lai đuôi kéo trung bình cộng lên.
3. **Độ trễ khởi động lạnh (`Cold Start`) khi nạp mô hình ngôn ngữ lớn cục bộ:**
   - Khi mô hình `qwen2.5:7b` chưa nằm trong VRAM của GPU, lượt gọi đầu tiên qua `POST /api/v1/ai/chat` mất `4803,73 ms` (~4,8 giây), cao gấp ~`7,7` lần so với trung vị các lượt gọi ấm (`p50 = 623,63 ms`, `p95 = 1399,13 ms`) và tiêu tốn `5426 MiB` VRAM cùng `2271,72 MiB` RAM Working Set của tiến trình Ollama/llama-server. Trên các máy không có GPU rời đủ VRAM (dưới 6 GB), độ trễ sinh câu trả lời sẽ tăng mạnh hoặc phải chuyển sang chế độ fallback trích đoạn (`p50 = 13,39 ms`).
4. **Hạn chế của cổng lọc ứng viên (`KnowledgeQuestionPolicy`) và bộ lọc an toàn thuốc trên câu hỏi giao thoa:**
   - Trong phép đo 20 câu `v2` qua `POST /api/v1/ai/chat`, câu hỏi hỏi về bước khai báo *"thuốc đang dùng và tiền sử dị ứng"* bị `AiMedicalScopeGuard.IsPrescriptionRequest` chuyển sang `OutOfScopeMedicalRequest` (`3/60` lượt), và `1/60` lượt sinh câu trả lời từ `qwen2.5:7b` bị bộ lọc hậu kiểm đầu ra chặn (`MEDICAL_PRESCRIPTION_OUT_OF_SCOPE`) do mô hình sinh từ khóa liên quan đến chỉ định thuốc.
5. **Ngưỡng `MinCosine = 0.20` của `bge-m3` không tự loại bỏ câu ngoài phạm vi nếu đứng độc lập:**
   - Như ghi nhận tại `docs/ai/RAG_EVALUATION.md:215`: *"MinCosine 0.2 gần như không lọc trên bộ dev hiện tại; từ chối ngoài phạm vi chủ yếu nhờ độ phủ từ khóa BM25 (`MinQueryCoverage` + `MinTermIdf`) và gate trong chế độ end-to-end."* Khả năng từ chối đúng ngoài phạm vi phụ thuộc chủ yếu vào bộ lọc từ vựng BM25 và cổng phân loại đầu vào chứ không dựa đơn thuần vào cosine.
6. **Hạn chế của cơ chế che thông tin cá nhân dựa trên biểu thức chính quy (`KnowledgePrivacyMask`):**
   - Vì chỉ sử dụng tập biểu thức chính quy tĩnh mà không có mô hình nhận diện thực thể có tên (NER) ngữ cảnh, `KnowledgePrivacyMask` để lọt định dạng số điện thoại dùng dấu phân cách không chuẩn như dấu gạch chéo `0901/234/567` (`Recall = 98,18%`, lọt `1/55` câu có PII trong bộ test giả) và báo nhầm trên các ký hiệu mã phòng khám `PK-102` hoặc cụm độ tuổi tổng quát `60 tuổi` (`Precision = 96,43%`, báo nhầm `2/15` câu không có PII).
